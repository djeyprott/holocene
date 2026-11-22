using Holocene.Core.Grid;

namespace Holocene.Core.World;

/// <summary>
/// Per-cell terrain stored as a hypsometric curve: elevation quantiles of the cell's area (metres relative to
/// present mean sea level). Keeping the whole curve rather than one mean lets sea-level change flood or expose
/// the right fraction of a cell, and gives ruggedness for movement, farming and defence.
/// </summary>
public sealed class Terrain
{
    /// <summary>Number of quantile points per cell (0%, 2.5%, …, 100%).</summary>
    public const int Q = 41;
    public GeodesicGrid Grid { get; }
    /// <summary>Quantiles, [cell*Q + q], ascending.</summary>
    public float[] Hyps { get; }

    public Terrain(GeodesicGrid grid, float[] hyps)
    {
        if (hyps.Length != grid.Count * Q) throw new ArgumentException("hypsometry size mismatch");
        Grid = grid; Hyps = hyps;
    }

    public float Min(int c) => Hyps[c * Q];
    public float Max(int c) => Hyps[c * Q + Q - 1];
    public float Median(int c) => Hyps[c * Q + Q / 2];

    public double Mean(int c)
    {
        // Trapezoid integral of the quantile function.
        double s = 0;
        for (int q = 0; q < Q - 1; q++) s += 0.5 * (Hyps[c * Q + q] + Hyps[c * Q + q + 1]);
        return s / (Q - 1);
    }

    /// <summary>Standard deviation of elevation within the cell (m) — ruggedness.</summary>
    public double StdDev(int c)
    {
        double m = Mean(c), s = 0;
        for (int q = 0; q < Q - 1; q++) { double v = 0.5 * (Hyps[c * Q + q] + Hyps[c * Q + q + 1]) - m; s += v * v; }
        return Math.Sqrt(s / (Q - 1));
    }

    /// <summary>Fraction of the cell's area above the given level (m).</summary>
    public double FractionAbove(int c, double level)
    {
        int b = c * Q;
        if (level <= Hyps[b]) return 1;
        if (level >= Hyps[b + Q - 1]) return 0;
        for (int q = 0; q < Q - 1; q++)
        {
            double lo = Hyps[b + q], hi = Hyps[b + q + 1];
            if (level < hi)
            {
                double frac = hi > lo ? (level - lo) / (hi - lo) : 0;
                double below = (q + frac) / (Q - 1);
                return 1 - below;
            }
        }
        return 0;
    }

    /// <summary>Elevation (m) at fraction <paramref name="p"/> (0..1) of the part of the cell lying above <paramref name="level"/>.</summary>
    public double QuantileAbove(int c, double level, double p)
    {
        double above = FractionAbove(c, level);
        if (above <= 0) return level;
        double f = (1 - above) + p * above;      // fraction of the whole cell
        double x = f * (Q - 1);
        int q = Math.Clamp((int)Math.Floor(x), 0, Q - 2);
        double r = x - q;
        return Math.Max(level, Hyps[c * Q + q] * (1 - r) + Hyps[c * Q + q + 1] * r);
    }

    /// <summary>Mean elevation of the part of the cell above <paramref name="level"/> (m); the level itself if none.</summary>
    public double MeanAbove(int c, double level)
    {
        int b = c * Q; double s = 0, w = 0;
        for (int q = 0; q < Q - 1; q++)
        {
            double lo = Hyps[b + q], hi = Hyps[b + q + 1];
            if (hi <= level) continue;
            double l = Math.Max(lo, level);
            double frac = hi > lo ? (hi - l) / (hi - lo) : 1;
            s += 0.5 * (l + hi) * frac; w += frac;
        }
        return w > 0 ? s / w : level;
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Grid.Frequency); w.Write(Q);
        foreach (var v in Hyps) w.Write(v);
    }

    public static Terrain Load(BinaryReader r, GeodesicGrid grid)
    {
        int f = r.ReadInt32(), q = r.ReadInt32();
        if (f != grid.Frequency || q != Q) throw new InvalidDataException("terrain cache does not match grid");
        var h = new float[grid.Count * Q];
        for (int i = 0; i < h.Length; i++) h[i] = r.ReadSingle();
        return new Terrain(grid, h);
    }
}
