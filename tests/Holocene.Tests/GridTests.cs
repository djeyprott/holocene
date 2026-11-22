using Holocene.Core.Grid;
using Holocene.Core.Mathx;
using Holocene.Core.World;

namespace Holocene.Tests;

public static class GridTests
{
    private static readonly Lazy<GeodesicGrid> G = new(() => new GeodesicGrid(16));

    public static void TestCellCountAndArea()
    {
        var g = G.Value;
        Assert.True(g.Count == 2562, $"count {g.Count}");
        double total = g.Area.Sum();
        Assert.Near(total, 4 * Math.PI * g.Radius * g.Radius, 1e-9 * total, "total area");
        double mean = total / g.Count;
        Assert.True(g.Area.Max() / g.Area.Min() < 1.3, $"area ratio {g.Area.Max() / g.Area.Min()}");
        Assert.True(Math.Abs(g.Area.Min() / mean - 1) < 0.25, "smallest cell within 25% of mean");
    }

    public static void TestNeighbourSymmetryAndDegree()
    {
        var g = G.Value;
        int pent = 0;
        for (int i = 0; i < g.Count; i++)
        {
            int d = g.Degree(i);
            Assert.True(d == 5 || d == 6, $"degree {d}");
            if (d == 5) pent++;
            for (int k = g.NbrStart[i]; k < g.NbrStart[i + 1]; k++)
            {
                int r = g.EdgeReverse[k];
                Assert.True(g.Nbr[r] == i, "reverse edge");
                Assert.Near(g.EdgeLength[r], g.EdgeLength[k], 1e-6, "edge length symmetric");
            }
        }
        Assert.True(pent == 12, $"pentagons {pent}");
    }

    public static void TestDivergenceTheoremOnCells()
    {
        // Sum over a cell's edges of edgeLength * (outward normal · constant vector field) must be ~0.
        var g = G.Value;
        double worst = 0;
        for (int i = 0; i < g.Count; i++)
        {
            var field = g.East[i] * 3 + g.North[i] * 2;
            double s = 0, per = 0;
            for (int k = g.NbrStart[i]; k < g.NbrStart[i + 1]; k++) { s += g.EdgeLength[k] * Vec3.Dot(g.EdgeDir[k], field); per += g.EdgeLength[k]; }
            worst = Math.Max(worst, Math.Abs(s) / per);
        }
        Assert.True(worst < 0.05, $"constant-field flux imbalance {worst}");
    }

    public static void TestLookupMatchesBruteForce()
    {
        var g = G.Value;
        var rng = new Rng(42);
        for (int t = 0; t < 2000; t++)
        {
            double lat = Math.Asin(2 * rng.NextDouble() - 1) * 180 / Math.PI, lon = rng.NextDouble() * 360 - 180;
            var v = Vec3.FromLatLon(lat * Math.PI / 180, lon * Math.PI / 180);
            int best = 0; double bd = -2;
            for (int i = 0; i < g.Count; i++) { double d = Vec3.Dot(g.Center[i], v); if (d > bd) { bd = d; best = i; } }
            Assert.True(g.CellAt(lat, lon) == best, $"lookup mismatch at {lat},{lon}");
        }
    }

    public static void TestRngDeterminism()
    {
        var a = Rng.For(7, 1, 2, 3); var b = Rng.For(7, 1, 2, 3); var c = Rng.For(7, 1, 2, 4);
        for (int i = 0; i < 100; i++) { ulong x = a.NextULong(); Assert.True(x == b.NextULong(), "same key same stream"); Assert.True(x != c.NextULong(), "different key differs"); }
        var r = new Rng(1); double s = 0, s2 = 0; int n = 200000;
        for (int i = 0; i < n; i++) { double z = r.NextGaussian(); s += z; s2 += z * z; }
        Assert.Near(s / n, 0, 0.01, "gaussian mean"); Assert.Near(s2 / n, 1, 0.02, "gaussian variance");
    }

    public static void TestHypsometry()
    {
        var g = new GeodesicGrid(2);
        var h = new float[g.Count * Terrain.Q];
        for (int c = 0; c < g.Count; c++) for (int q = 0; q < Terrain.Q; q++) h[c * Terrain.Q + q] = -100 + 200f * q / (Terrain.Q - 1);
        var t = new Terrain(g, h);
        Assert.Near(t.FractionAbove(0, 0), 0.5, 1e-9, "half above 0");
        Assert.Near(t.FractionAbove(0, 50), 0.25, 1e-9, "quarter above 50");
        Assert.Near(t.FractionAbove(0, -200), 1, 0, "all above");
        Assert.Near(t.MeanAbove(0, 0), 50, 1e-6, "mean above 0");
        Assert.Near(t.Mean(0), 0, 1e-6, "mean");
    }
}
