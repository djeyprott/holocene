using Holocene.Core.Mathx;

namespace Holocene.Core.Grid;

/// <summary>
/// Icosahedral geodesic grid: cells are the Voronoi regions around the vertices of an icosahedron whose
/// faces are subdivided <c>Frequency</c> times. N = 10·f² + 2 cells, 12 pentagons, the rest hexagons.
/// All geometry is on the unit sphere; multiply by the planet radius for metres.
/// </summary>
public sealed class GeodesicGrid
{
    public int Frequency { get; }
    public int Count { get; }
    public double Radius { get; }

    /// <summary>Cell centres (unit vectors).</summary>
    public Vec3[] Center { get; }
    public double[] Lat { get; }   // radians
    public double[] Lon { get; }   // radians
    /// <summary>Cell areas in m².</summary>
    public double[] Area { get; }
    public Vec3[] East { get; }
    public Vec3[] North { get; }

    // Compressed neighbour lists (CSR). Neighbours are ordered counter-clockwise seen from outside.
    public int[] NbrStart { get; }
    public int[] Nbr { get; }
    /// <summary>For each (cell, neighbour) entry: length of the shared Voronoi edge, m.</summary>
    public double[] EdgeLength { get; }
    /// <summary>For each (cell, neighbour) entry: great-circle distance between centres, m.</summary>
    public double[] EdgeDist { get; }
    /// <summary>For each (cell, neighbour) entry: unit tangent at the cell centre pointing toward the neighbour.</summary>
    public Vec3[] EdgeDir { get; }
    /// <summary>For each (cell, neighbour) entry: index of the reverse entry (neighbour → cell).</summary>
    public int[] EdgeReverse { get; }
    /// <summary>Voronoi corners of each cell (CSR with the same start indices as neighbours).</summary>
    public Vec3[] Corner { get; }

    private readonly int[] _lookup; // 1°×1° table of starting cells for nearest-cell search
    private const int LookupRes = 1;

    public GeodesicGrid(int frequency, double radius = 6.371e6)
    {
        Frequency = frequency;
        Radius = radius;
        var (verts, tris) = BuildMesh(frequency);
        Count = verts.Count;
        if (Count != 10 * frequency * frequency + 2) throw new InvalidOperationException($"Grid build error: {Count} vertices");

        Center = verts.ToArray();
        Lat = new double[Count]; Lon = new double[Count];
        East = new Vec3[Count]; North = new Vec3[Count];
        for (int i = 0; i < Count; i++)
        {
            Lat[i] = Center[i].Lat; Lon[i] = Center[i].Lon;
            East[i] = Vec3.East(Center[i]); North[i] = Vec3.North(Center[i]);
        }

        // Triangles around each vertex.
        var vt = new List<int>[Count];
        for (int i = 0; i < Count; i++) vt[i] = new List<int>(6);
        for (int t = 0; t < tris.Count; t++) { var (a, b, c) = tris[t]; vt[a].Add(t); vt[b].Add(t); vt[c].Add(t); }
        var circ = new Vec3[tris.Count];
        for (int t = 0; t < tris.Count; t++) { var (a, b, c) = tris[t]; circ[t] = Sphere.Circumcenter(Center[a], Center[b], Center[c]); }

        NbrStart = new int[Count + 1];
        var nbr = new List<int>(Count * 6);
        var corners = new List<Vec3>(Count * 6);
        Area = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            NbrStart[i] = nbr.Count;
            var p = Center[i];
            var e = East[i]; var n = North[i];
            // Neighbours sorted counter-clockwise by angle in the local tangent plane.
            var ns = new SortedList<double, int>();
            foreach (int t in vt[i])
            {
                var (a, b, c) = tris[t];
                foreach (int v in new[] { a, b, c })
                {
                    if (v == i) continue;
                    var d = Center[v] - p;
                    double ang = Math.Atan2(Vec3.Dot(d, n), Vec3.Dot(d, e));
                    if (!ns.ContainsValue(v)) ns.Add(ang, v);
                }
            }
            var ordered = ns.Values.ToList();
            nbr.AddRange(ordered);
            // Corner k lies between neighbour k and neighbour k+1: circumcentre of triangle (i, nk, nk+1).
            int m = ordered.Count;
            var cs = new Vec3[m];
            for (int k = 0; k < m; k++)
            {
                int a = ordered[k], b = ordered[(k + 1) % m];
                int tri = vt[i].First(t => { var (x, y, z) = tris[t]; return Has(x, y, z, a) && Has(x, y, z, b); });
                cs[k] = circ[tri];
            }
            corners.AddRange(cs);
            double area = 0;
            for (int k = 0; k < m; k++) area += Sphere.TriangleArea(p, cs[k], cs[(k + 1) % m]);
            Area[i] = area * radius * radius;
        }
        NbrStart[Count] = nbr.Count;
        Nbr = nbr.ToArray();
        Corner = corners.ToArray();

        EdgeLength = new double[Nbr.Length];
        EdgeDist = new double[Nbr.Length];
        EdgeDir = new Vec3[Nbr.Length];
        EdgeReverse = new int[Nbr.Length];
        for (int i = 0; i < Count; i++)
        {
            int s = NbrStart[i], m = NbrStart[i + 1] - s;
            for (int k = 0; k < m; k++)
            {
                int j = Nbr[s + k];
                // The edge shared with neighbour k is bounded by corners k-1 and k.
                var c0 = Corner[s + (k + m - 1) % m];
                var c1 = Corner[s + k];
                EdgeLength[s + k] = Vec3.Angle(c0, c1) * radius;
                EdgeDist[s + k] = Vec3.Angle(Center[i], Center[j]) * radius;
                var d = Center[j] - Center[i] * Vec3.Dot(Center[j], Center[i]);
                EdgeDir[s + k] = d.Normalized();
                int rs = NbrStart[j], re = NbrStart[j + 1];
                for (int r = rs; r < re; r++) if (Nbr[r] == i) { EdgeReverse[s + k] = r; break; }
            }
        }

        _lookup = new int[180 * 360];
        int start = 0;
        for (int la = 0; la < 180; la++)
            for (int lo = 0; lo < 360; lo++)
            {
                var v = Vec3.FromLatLon((89.5 - la) * Math.PI / 180, (lo + 0.5 - 180) * Math.PI / 180);
                start = Walk(v, start);
                _lookup[la * 360 + lo] = start;
            }
    }

    private static bool Has(int x, int y, int z, int v) => x == v || y == v || z == v;

    /// <summary>Bumped whenever cell positions change, so caches keyed on the grid are rebuilt.
    /// v3: great-circle subdivision without Lloyd relaxation (max/min cell area 1.23; relaxation made it worse).</summary>
    public const int Version = 3;

    public int Degree(int i) => NbrStart[i + 1] - NbrStart[i];

    /// <summary>Nearest cell to a unit vector, starting a greedy walk from <paramref name="start"/>.
    /// On a Delaunay triangulation of points on a sphere the greedy walk always reaches the nearest vertex.</summary>
    public int Walk(Vec3 v, int start)
    {
        int cur = start;
        double best = Vec3.Dot(Center[cur], v);
        while (true)
        {
            int next = cur;
            for (int k = NbrStart[cur]; k < NbrStart[cur + 1]; k++)
            {
                int j = Nbr[k];
                double d = Vec3.Dot(Center[j], v);
                if (d > best) { best = d; next = j; }
            }
            if (next == cur) return cur;
            cur = next;
        }
    }

    /// <summary>Nearest cell to a latitude/longitude given in degrees.</summary>
    public int CellAt(double latDeg, double lonDeg)
    {
        int la = Math.Clamp((int)(90 - latDeg), 0, 179);
        double l = lonDeg + 180; l -= 360 * Math.Floor(l / 360);
        int lo = Math.Clamp((int)l, 0, 359);
        return Walk(Vec3.FromLatLon(latDeg * Math.PI / 180, lonDeg * Math.PI / 180), _lookup[la * 360 + lo]);
    }

    public double LatDeg(int i) => Lat[i] * 180 / Math.PI;
    public double LonDeg(int i) => Lon[i] * 180 / Math.PI;

    private static Vec3 Slerp(Vec3 a, Vec3 b, double t)
    {
        double om = Vec3.Angle(a, b);
        if (om < 1e-15) return a;
        return (a * Math.Sin((1 - t) * om) + b * Math.Sin(t * om)) / Math.Sin(om);
    }

    private static (List<Vec3>, List<(int, int, int)>) BuildMesh(int f)
    {
        double t = (1 + Math.Sqrt(5)) / 2;
        var ico = new[]
        {
            new Vec3(-1, t, 0), new Vec3(1, t, 0), new Vec3(-1, -t, 0), new Vec3(1, -t, 0),
            new Vec3(0, -1, t), new Vec3(0, 1, t), new Vec3(0, -1, -t), new Vec3(0, 1, -t),
            new Vec3(t, 0, -1), new Vec3(t, 0, 1), new Vec3(-t, 0, -1), new Vec3(-t, 0, 1),
        }.Select(v => v.Normalized()).ToArray();
        // Rotate so that a vertex is not exactly at a pole (avoids degenerate east vectors) — tilt by a small irrational angle.
        double ang = 0.1234567;
        ico = ico.Select(v => new Vec3(v.X, v.Y * Math.Cos(ang) - v.Z * Math.Sin(ang), v.Y * Math.Sin(ang) + v.Z * Math.Cos(ang))).ToArray();
        int[,] faces =
        {
            {0,11,5},{0,5,1},{0,1,7},{0,7,10},{0,10,11},{1,5,9},{5,11,4},{11,10,2},{10,7,6},{7,1,8},
            {3,9,4},{3,4,2},{3,2,6},{3,6,8},{3,8,9},{4,9,5},{2,4,11},{6,2,10},{8,6,7},{9,8,1},
        };
        var verts = new List<Vec3>();
        var index = new Dictionary<(long, long, long), int>();
        var tris = new List<(int, int, int)>();
        int Get(Vec3 p)
        {
            p = p.Normalized();
            var key = ((long)Math.Round(p.X * 1e9), (long)Math.Round(p.Y * 1e9), (long)Math.Round(p.Z * 1e9));
            if (!index.TryGetValue(key, out int id)) { id = verts.Count; verts.Add(p); index[key] = id; }
            return id;
        }
        for (int fi = 0; fi < 20; fi++)
        {
            var a = ico[faces[fi, 0]]; var b = ico[faces[fi, 1]]; var c = ico[faces[fi, 2]];
            var ids = new int[f + 1, f + 1];
            for (int i = 0; i <= f; i++)
                for (int j = 0; j <= f - i; j++)
                {
                    // Great-circle subdivision: row r = i + j steps from vertex a, interpolated between the
                    // points at that distance along edges a→b and a→c. Shared edges reproduce Slerp exactly.
                    int r = i + j;
                    if (r == 0) { ids[i, j] = Get(a); continue; }
                    var p1 = Slerp(a, b, (double)r / f);
                    var p2 = Slerp(a, c, (double)r / f);
                    ids[i, j] = Get(Slerp(p1, p2, (double)j / r));
                }
            for (int i = 0; i < f; i++)
                for (int j = 0; j < f - i; j++)
                {
                    tris.Add((ids[i, j], ids[i + 1, j], ids[i, j + 1]));
                    if (j < f - i - 1) tris.Add((ids[i + 1, j], ids[i + 1, j + 1], ids[i, j + 1]));
                }
        }
        return (verts, tris);
    }
}
