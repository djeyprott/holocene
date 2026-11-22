using System.Globalization;

namespace Holocene.Core.Earth;

/// <summary>
/// Earth-mode external forcing histories (design §4.1): ice-core CO2 and the deglacial sea-level curve.
/// Years use astronomical numbering (10,000 BCE = −9999); tables are in years before 1950 CE.
/// </summary>
public sealed class EarthForcing
{
    private readonly (double age, double v)[] _co2, _sea;

    public EarthForcing(string dataDir)
    {
        _co2 = ReadCsv(Path.Combine(dataDir, "co2_epica_domec.csv"));
        _sea = ReadCsv(Path.Combine(dataDir, "sea_level_lambeck2014.csv"));
    }

    private static (double, double)[] ReadCsv(string path) =>
        File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#') && char.IsDigit(l[0]))
            .Select(l => l.Split(','))
            .Select(p => (double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture)))
            .OrderBy(t => t.Item1).ToArray();

    public static double AgeBP(double yearCE) => 1950.0 - yearCE;

    /// <summary>CO2 (ppm) for a year. Within the industrial era (after 1850) uses the observed rise approximately
    /// (Mauna Loa / Law Dome); before the record's youngest point holds the last value.</summary>
    public double Co2(double yearCE)
    {
        if (yearCE > 1850) return 285 + 85 * Math.Pow(Math.Max(0, yearCE - 1850) / 150.0, 2.5); // ~285 in 1850, ~370 in 2000
        return Interp(_co2, AgeBP(yearCE));
    }

    public double SeaLevel(double yearCE) => Interp(_sea, Math.Max(0, AgeBP(yearCE)));

    private static double Interp((double age, double v)[] t, double age)
    {
        if (age <= t[0].age) return t[0].v;
        if (age >= t[^1].age) return t[^1].v;
        int k = 0; while (t[k + 1].age < age) k++;
        double f = (age - t[k].age) / (t[k + 1].age - t[k].age);
        return t[k].v + f * (t[k + 1].v - t[k].v);
    }
}
