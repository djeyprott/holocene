namespace Holocene.Core.Climate;

/// <summary>
/// Saves the climatology of the last simulated year as named float32 layers:
/// header "HCLM", version, cell count, layer count, then for each layer (name, float[N]).
/// </summary>
public static class ClimateIO
{
    public static Dictionary<string, float[]> Layers(ClimateModel m)
    {
        int n = m.N;
        var d = new Dictionary<string, float[]>();
        float[] F(Func<int, double> f) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = (float)f(i); return a; }
        for (int mo = 0; mo < 12; mo++)
        {
            int k = mo;
            d[$"t{k:00}"] = F(i => m.MonthT[k][i]);
            d[$"p{k:00}"] = F(i => m.MonthP[k][i]);
        }
        d["land"] = F(i => m.IsLand[i] ? 1 : 0);
        d["landfrac"] = F(i => m.LandFrac[i]);
        d["elev"] = F(i => m.Elev[i]);
        d["t_ann"] = F(i => Enumerable.Range(0, 12).Average(mo => m.MonthT[mo][i]));
        d["p_ann"] = F(i => Enumerable.Range(0, 12).Sum(mo => m.MonthP[mo][i]) * 365.0 / 12);
        d["pet_ann"] = F(i => Enumerable.Range(0, 12).Sum(mo => m.MonthPET[mo][i]) * 365.0 / 12);
        d["runoff"] = F(i => m.AnnualRunoff[i]);
        d["snow_max"] = F(i => Enumerable.Range(0, 12).Max(mo => m.MonthSnow[mo][i]));
        d["seaice_max"] = F(i => m.SeaIceMax[i]);
        d["veg"] = F(i => m.Veg[i]);
        d["forest"] = F(i => m.Forest[i]);
        d["pressure"] = F(i => m.AnnualPressure[i]);
        d["wind_e"] = F(i => Holocene.Core.Mathx.Vec3.Dot(m.AnnualWind[i], m.G.East[i]));
        d["wind_n"] = F(i => Holocene.Core.Mathx.Vec3.Dot(m.AnnualWind[i], m.G.North[i]));
        d["psi"] = F(i => m.Psi[i] / 1e6); // Sverdrups
        return d;
    }

    public static void Save(ClimateModel m, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var layers = Layers(m);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("HCLM".ToCharArray()); w.Write(1); w.Write(m.N); w.Write(layers.Count);
        foreach (var (name, a) in layers) { w.Write(name); foreach (var v in a) w.Write(v); }
    }

    public static Dictionary<string, float[]> Load(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (new string(r.ReadChars(4)) != "HCLM") throw new InvalidDataException("not a climate file");
        r.ReadInt32(); int n = r.ReadInt32(), count = r.ReadInt32();
        var d = new Dictionary<string, float[]>();
        for (int k = 0; k < count; k++)
        {
            string name = r.ReadString(); var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = r.ReadSingle();
            d[name] = a;
        }
        return d;
    }
}
