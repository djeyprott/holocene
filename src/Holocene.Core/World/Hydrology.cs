using Holocene.Core.Grid;

namespace Holocene.Core.World;

/// <summary>A lake occupying a closed depression of the routing surface.</summary>
public sealed record Lake(int Id, int ExitCell, double Level, double SpillLevel, double Area, bool Terminal, int[] Cells, double InflowM3s);

/// <summary>
/// River network and lakes from simulated runoff (design §3.3).
/// 1. Routing surface: the lowest ground of each cell's land area — rivers run in valley floors and gorges.
/// 2. Priority-flood (Barnes, Lehman &amp; Mulla 2014) from the coast gives every land cell a downstream neighbour
///    and the level to which closed depressions would have to fill before they overflow.
/// 3. Runoff is accumulated downstream. Each depression holds a lake whose evaporation (open-water PET over the
///    lake area) is weighed against its inflow: if inflow cannot fill it to the spill level the lake is terminal
///    (endorheic, like the Caspian or Lake Chad) and passes nothing on; otherwise it overflows and the excess
///    continues downstream. Lake sizes therefore respond to climate.
/// </summary>
public sealed class Hydrology
{
    public readonly int[] Downstream;     // −1 for ocean cells and terminal lakes' exits
    public readonly double[] Discharge;   // m³/s leaving each cell
    public readonly double[] RouteElev;   // m
    public readonly double[] FillLevel;   // m
    public readonly int[] LakeOf;         // lake id or −1
    public readonly int[] BasinOf;        // id of the coastal outlet or terminal lake exit
    public readonly List<Lake> Lakes = new();

    /// <summary>Depressions shallower than these are treated as noise in the 10 %-quantile routing surface of
    /// ~100 km cells (flat floodplains), not as lakes. Numerical thresholds, not physical constants.</summary>
    public const double MinFloodDepth = 2.0, MinLakeDepth = 10.0;

    /// <summary>Rivers run along the lowest ground of each cell: the minimum of its land part. Higher quantiles miss
    /// gorges narrower than a cell (the Yangtze through the Three Gorges) and turn whole basins into false lakes.</summary>
    public const double RouteQuantile = 0.0;

    public Hydrology(GeodesicGrid g, Terrain t, bool[] isLand, double seaLevel, double[] runoffMmYr, double[] lakeEvapMmYr)
    {
        int n = g.Count;
        Downstream = Enumerable.Repeat(-1, n).ToArray();
        Discharge = new double[n]; RouteElev = new double[n]; FillLevel = new double[n];
        LakeOf = Enumerable.Repeat(-1, n).ToArray(); BasinOf = Enumerable.Repeat(-1, n).ToArray();
        for (int i = 0; i < n; i++)
            RouteElev[i] = isLand[i] ? Math.Max(seaLevel + 1, t.QuantileAbove(i, seaLevel, RouteQuantile)) - seaLevel : 0;

        // ---- priority flood ----
        var visited = new bool[n];
        var pq = new PriorityQueue<int, (double, int)>();
        var order = new List<int>(n);
        int seq = 0;
        for (int i = 0; i < n; i++)
        {
            if (isLand[i]) continue;
            visited[i] = true; FillLevel[i] = 0;
            // ocean cells adjacent to land seed the flood
            for (int k = g.NbrStart[i]; k < g.NbrStart[i + 1]; k++)
                if (isLand[g.Nbr[k]]) { pq.Enqueue(i, (0.0, seq++)); break; }
        }
        while (pq.TryDequeue(out int c, out _))
        {
            if (isLand[c]) order.Add(c);
            for (int k = g.NbrStart[c]; k < g.NbrStart[c + 1]; k++)
            {
                int j = g.Nbr[k];
                if (visited[j] || !isLand[j]) continue;
                visited[j] = true;
                FillLevel[j] = Math.Max(RouteElev[j], FillLevel[c]);
                Downstream[j] = c;
                pq.Enqueue(j, (FillLevel[j], seq++));
            }
        }

        // ---- depressions: connected cells whose fill level exceeds their own floor; exit = first cell downstream
        //      that is not flooded at the same level ----
        var depressionOf = Enumerable.Repeat(-1, n).ToArray();
        var depressions = new List<List<int>>();
        var depExit = new List<int>(); var depSpill = new List<double>();
        var bySpill = new Dictionary<int, int>(); // spill cell → depression (several flooded cells may border it)
        foreach (int c in order)
        {
            if (FillLevel[c] <= RouteElev[c] + MinFloodDepth) continue;
            int d = Downstream[c];
            if (d >= 0 && isLand[d] && depressionOf[d] >= 0 && Math.Abs(FillLevel[d] - FillLevel[c]) < 1e-6)
            {
                depressionOf[c] = depressionOf[d]; depressions[depressionOf[c]].Add(c);
            }
            else if (bySpill.TryGetValue(d, out int dep) && Math.Abs(depSpill[dep] - FillLevel[c]) < 1e-6)
            {
                depressionOf[c] = dep; depressions[dep].Add(c);
            }
            else
            {
                depressionOf[c] = depressions.Count;
                bySpill[d] = depressions.Count;
                depressions.Add(new List<int> { c }); depExit.Add(d); depSpill.Add(FillLevel[c]);
            }
        }
        // The first cell of each depression in flood order is where its water leaves; all other members drain to it
        // or to another member. Record it so accumulation can apply the lake balance there.
        var depExitCell = depressions.Select(l => l[0]).ToArray();

        // ---- accumulate discharge from upstream to downstream (reverse flood order) ----
        var inflow = new double[n];
        var depInflow = new double[depressions.Count];
        var depDone = new bool[depressions.Count];
        for (int idx = order.Count - 1; idx >= 0; idx--)
        {
            int c = order[idx];
            double q = inflow[c] + Math.Max(0, runoffMmYr[c]) / 1000.0 * g.Area[c] / (365.25 * 86400); // m³/s
            int dep = depressionOf[c];
            int d = Downstream[c];
            bool leavesDepression = dep >= 0 && (d < 0 || depressionOf[d] != dep);
            if (leavesDepression && c != depExitCell[dep])
            {
                // Another member draining straight to the shared spill cell: route its water to the exit cell instead
                // so the whole depression's inflow is balanced once.
                inflow[depExitCell[dep]] += q; Discharge[c] = q; Downstream[c] = depExitCell[dep]; continue;
            }
            if (dep >= 0 && !leavesDepression) { Discharge[c] = q; if (d >= 0) inflow[d] += q; continue; }
            if (leavesDepression && !depDone[dep])
            {
                depDone[dep] = true;
                depInflow[dep] = q;
                var cells = depressions[dep].OrderBy(x => RouteElev[x]).ToArray();
                // Lake level where open-water evaporation over the flooded area balances inflow.
                double Evap(double level) => cells.Where(x => RouteElev[x] < level)
                    .Sum(x => Math.Max(0, lakeEvapMmYr[x]) / 1000.0 * g.Area[x] / (365.25 * 86400));
                double spill = depSpill[dep];
                double eFull = Evap(spill + 1e-6);
                bool terminal = q <= eFull;
                double level = spill;
                if (terminal)
                {
                    double lo = RouteElev[cells[0]], hi = spill;
                    for (int it = 0; it < 40; it++) { double mid = 0.5 * (lo + hi); if (Evap(mid) > q) hi = mid; else lo = mid; }
                    level = 0.5 * (lo + hi);
                }
                var lakeCells = cells.Where(x => level - RouteElev[x] >= MinFloodDepth).ToArray();
                if (lakeCells.Length > 0 && level - RouteElev[cells[0]] >= MinLakeDepth)
                {
                    var lake = new Lake(Lakes.Count, c, level + seaLevel, spill + seaLevel, lakeCells.Sum(x => g.Area[x]), terminal, lakeCells, q);
                    foreach (var x in lakeCells) LakeOf[x] = lake.Id;
                    Lakes.Add(lake);
                }
                double outQ = terminal ? 0 : q - eFull;
                Discharge[c] = outQ;
                if (terminal) Downstream[c] = -1;
                else if (d >= 0) inflow[d] += outQ;
                continue;
            }
            Discharge[c] = q;
            if (d >= 0) inflow[d] += q;
        }

        // ---- basins: follow downstream pointers to the ocean outlet or terminal lake ----
        for (int i = 0; i < n; i++)
        {
            if (!isLand[i]) continue;
            int c = i, steps = 0;
            while (Downstream[c] >= 0 && isLand[Downstream[c]] && steps++ < n) c = Downstream[c];
            BasinOf[i] = c;
        }
    }

    /// <summary>Strahler-like size class from discharge (m³/s): 0 none, 1 stream, 2 river, 3 large river, 4 great river.</summary>
    public static int RiverClass(double q) => q < 30 ? 0 : q < 300 ? 1 : q < 3000 ? 2 : q < 15000 ? 3 : 4;
}
