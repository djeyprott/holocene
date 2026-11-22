using Holocene.Core.Grid;
using Holocene.Core.Mathx;
using Holocene.Core.World;

namespace Holocene.Core.Earth;

/// <summary>
/// Reader for ETOPO5 (NOAA/NGDC 5-arc-minute global relief): 2160 rows × 4320 columns of big-endian int16
/// metres, row 0 at 90°N, column 0 at 0°E, 1/12° spacing.
/// </summary>
public static class Etopo5
{
    public const int Rows = 2160, Cols = 4320;

    public static short[] Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != Rows * Cols * 2) throw new InvalidDataException($"{path}: unexpected size {bytes.Length}");
        var a = new short[Rows * Cols];
        for (int i = 0; i < a.Length; i++) a[i] = (short)(bytes[2 * i] << 8 | bytes[2 * i + 1]);
        return a;
    }

    /// <summary>
    /// Builds per-cell hypsometric curves by assigning every ETOPO5 pixel to its nearest cell, weighted by the
    /// pixel's area (∝ cos latitude), and taking area-weighted quantiles.
    /// </summary>
    public static Terrain BuildTerrain(GeodesicGrid grid, short[] data)
    {
        var cellOf = new int[Rows * Cols];
        // Parallel over rows; each row walks the grid from the previous pixel's cell.
        Parallel.For(0, Rows, r =>
        {
            double lat = (90 - (r + 0.5) / 12.0);
            int cur = grid.CellAt(lat, 0.5 / 12.0);
            for (int c = 0; c < Cols; c++)
            {
                double lon = (c + 0.5) / 12.0;
                cur = grid.Walk(Vec3.FromLatLon(lat * Math.PI / 180, lon * Math.PI / 180), cur);
                cellOf[r * Cols + c] = cur;
            }
        });

        // Bucket pixels per cell.
        var counts = new int[grid.Count + 1];
        foreach (var c in cellOf) counts[c + 1]++;
        for (int i = 0; i < grid.Count; i++) counts[i + 1] += counts[i];
        var fill = (int[])counts.Clone();
        var elev = new short[cellOf.Length];
        var row = new short[cellOf.Length];
        for (int p = 0; p < cellOf.Length; p++) { int k = fill[cellOf[p]]++; elev[k] = data[p]; row[k] = (short)(p / Cols); }

        var hyps = new float[grid.Count * Terrain.Q];
        Parallel.For(0, grid.Count, i =>
        {
            int s = counts[i], e = counts[i + 1], n = e - s;
            if (n == 0) throw new InvalidOperationException($"cell {i} received no pixels");
            var pairs = new (short h, double w)[n];
            double total = 0;
            for (int k = 0; k < n; k++)
            {
                double w = Math.Cos((90 - (row[s + k] + 0.5) / 12.0) * Math.PI / 180);
                pairs[k] = (elev[s + k], w); total += w;
            }
            Array.Sort(pairs, (a, b) => a.h.CompareTo(b.h));
            // Quantile q/(Q-1): elevation where cumulative weight (mid-point rule) reaches that fraction.
            int idx = 0; double cum = pairs[0].w * 0.5;
            for (int q = 0; q < Terrain.Q; q++)
            {
                double target = total * q / (Terrain.Q - 1);
                while (idx < n - 1 && cum + 0.5 * (pairs[idx].w + pairs[idx + 1].w) < target)
                { cum += 0.5 * (pairs[idx].w + pairs[idx + 1].w); idx++; }
                hyps[i * Terrain.Q + q] = q == 0 ? pairs[0].h : q == Terrain.Q - 1 ? pairs[n - 1].h : pairs[idx].h;
            }
        });
        return new Terrain(grid, hyps);
    }
}
