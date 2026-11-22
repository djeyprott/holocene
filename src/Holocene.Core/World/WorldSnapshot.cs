using Holocene.Core.Climate;
using Holocene.Core.Grid;

namespace Holocene.Core.World;

/// <summary>
/// The physical world at one epoch: climate climatology, rivers and lakes, Köppen class, biome and productivity.
/// This is the environment that the population layers (phase 2) will live in.
/// </summary>
public sealed class WorldSnapshot
{
    public double YearCE { get; init; }
    public double SeaLevel { get; init; }
    public double Co2 { get; init; }
    public required GeodesicGrid Grid { get; init; }
    public required Dictionary<string, float[]> Layers { get; init; }
    public required Hydrology Hydro { get; init; }
    public required string[] Koppen { get; init; }
    public required Biome[] Biome { get; init; }

    public static WorldSnapshot Build(ClimateModel m, Terrain terrain, ClimateForcing f, double glacierSwe)
    {
        var g = m.G; int n = m.N;
        var layers = ClimateIO.Layers(m);
        // Runoff (mm/yr) and open-water evaporation (≈ potential evaporation, mm/yr) drive rivers and lakes.
        var runoff = Enumerable.Range(0, n).Select(i => (double)layers["runoff"][i]).ToArray();
        var lakeEvap = Enumerable.Range(0, n).Select(i => (double)layers["pet_ann"][i]).ToArray();
        var hydro = new Hydrology(g, terrain, m.IsLand, f.SeaLevel, runoff, lakeEvap);

        var koppen = new string[n]; var biome = new Biome[n];
        var npp = new float[n];
        Parallel.For(0, n, i =>
        {
            if (!m.IsLand[i]) { koppen[i] = ""; biome[i] = World.Biome.Ocean; return; }
            var t = Enumerable.Range(0, 12).Select(mo => m.MonthT[mo][i]).ToArray();
            var p = Enumerable.Range(0, 12).Select(mo => m.MonthP[mo][i] * 30.4).ToArray();
            koppen[i] = Biomes.Koppen(t, p, g.Lat[i] >= 0);
            bool glacier = m.Snow[i] >= glacierSwe;
            biome[i] = hydro.LakeOf[i] >= 0 ? World.Biome.Lake : Biomes.Refine(Biomes.FromKoppen(koppen[i], glacier), m.Forest[i], m.Veg[i], t.Average());
            npp[i] = biome[i] is World.Biome.Ice or World.Biome.Lake ? 0 : (float)Biomes.MiamiNpp(t.Average(), p.Sum());
        });
        layers["npp"] = npp;
        layers["biome"] = biome.Select(b => (float)(int)b).ToArray();
        layers["discharge"] = hydro.Discharge.Select(x => (float)x).ToArray();
        layers["downstream"] = hydro.Downstream.Select(x => (float)x).ToArray();
        layers["lake"] = hydro.LakeOf.Select(x => (float)x).ToArray();
        layers["glacier"] = Enumerable.Range(0, n).Select(i => m.IsLand[i] && m.Snow[i] >= glacierSwe ? 1f : 0f).ToArray();
        return new WorldSnapshot { YearCE = f.YearCE, SeaLevel = f.SeaLevel, Co2 = f.Co2Ppm, Grid = g, Layers = layers, Hydro = hydro, Koppen = koppen, Biome = biome };
    }
}
