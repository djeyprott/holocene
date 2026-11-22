namespace Holocene.Core.World;

/// <summary>Broad vegetation zones used by ecology and shown on maps.</summary>
public enum Biome : byte
{
    Ocean, Ice, Tundra, BorealForest, TemperateForest, TemperateGrassland, Mediterranean,
    Desert, XericShrubland, Savanna, TropicalDryForest, TropicalRainforest, Lake,
}

/// <summary>
/// Climate classification and productivity from monthly climate (design §3.4).
/// Köppen–Geiger classes follow the rules of Peel, Finlayson &amp; McMahon (2007).
/// Net primary productivity uses the Miami model (Lieth 1975).
/// </summary>
public static class Biomes
{
    /// <param name="t">12 monthly mean temperatures (°C)</param>
    /// <param name="p">12 monthly precipitation totals (mm/month)</param>
    /// <param name="northern">true north of the equator (summer = Apr–Sep)</param>
    public static string Koppen(double[] t, double[] p, bool northern)
    {
        double mat = t.Average(), map = p.Sum();
        double tHot = t.Max(), tCold = t.Min();
        int months10 = t.Count(x => x >= 10);
        int[] summer = northern ? new[] { 3, 4, 5, 6, 7, 8 } : new[] { 9, 10, 11, 0, 1, 2 };
        int[] winter = northern ? new[] { 9, 10, 11, 0, 1, 2 } : new[] { 3, 4, 5, 6, 7, 8 };
        double pSummer = summer.Sum(m => p[m]), pWinter = winter.Sum(m => p[m]);
        double pDry = p.Min();
        double pSdry = summer.Min(m => p[m]), pSwet = summer.Max(m => p[m]);
        double pWdry = winter.Min(m => p[m]), pWwet = winter.Max(m => p[m]);
        double pth = pWinter >= 0.7 * map ? 2 * mat : pSummer >= 0.7 * map ? 2 * mat + 28 : 2 * mat + 14;

        if (tHot < 10) return tHot < 0 ? "EF" : "ET";
        if (map < 10 * pth)
            return (map < 5 * pth ? "BW" : "BS") + (mat >= 18 ? "h" : "k");
        if (tCold >= 18)
        {
            if (pDry >= 60) return "Af";
            if (pDry >= 100 - map / 25) return "Am";
            return pSdry < pWdry ? "As" : "Aw";
        }
        string first = tCold > 0 ? "C" : "D";
        string second = (pSdry < 40 && pSdry < pWwet / 3) ? "s" : (pWdry < pSwet / 10) ? "w" : "f";
        string third = tHot >= 22 ? "a" : months10 >= 4 ? "b" : (first == "D" && tCold < -38) ? "d" : "c";
        return first + second + third;
    }

    public static Biome FromKoppen(string k, bool glacier)
    {
        if (glacier || k == "EF") return Biome.Ice;
        return k switch
        {
            "ET" => Biome.Tundra,
            "Af" or "Am" => Biome.TropicalRainforest,
            "Aw" or "As" => Biome.Savanna,
            "BWh" or "BWk" => Biome.Desert,
            "BSh" or "BSk" => Biome.XericShrubland,
            _ when k.StartsWith("Cs") => Biome.Mediterranean,
            _ when k.StartsWith("C") => Biome.TemperateForest,
            _ when k is "Dfc" or "Dfd" or "Dwc" or "Dwd" or "Dsc" or "Dsd" => Biome.BorealForest,
            _ when k.StartsWith("D") => Biome.TemperateForest,
            _ => Biome.TemperateGrassland,
        };
    }

    /// <summary>Refines the Köppen-derived biome with simulated vegetation where the classes are ambiguous
    /// (continental steppe vs. forest, dry tropical forest vs. savanna).</summary>
    public static Biome Refine(Biome b, double forest, double veg, double mat)
    {
        if (b == Biome.TemperateForest && forest < 0.3) return veg > 0.3 ? Biome.TemperateGrassland : Biome.XericShrubland;
        if (b == Biome.Savanna && forest > 0.6) return Biome.TropicalDryForest;
        if (b == Biome.BorealForest && forest < 0.2) return Biome.Tundra;
        return b;
    }

    /// <summary>Net primary productivity, g dry matter /m²/yr, Miami model (Lieth 1975):
    /// NPP = min(3000/(1+e^(1.315−0.119T)), 3000(1−e^(−0.000664P))).</summary>
    public static double MiamiNpp(double meanTempC, double annualPrecipMm)
    {
        double nT = 3000.0 / (1 + Math.Exp(1.315 - 0.119 * meanTempC));
        double nP = 3000.0 * (1 - Math.Exp(-0.000664 * Math.Max(0, annualPrecipMm)));
        return Math.Min(nT, nP);
    }
}
