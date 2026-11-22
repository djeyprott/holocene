using System.Text;
using System.Text.Json;
using Holocene.Core.Grid;

namespace Holocene.Core.Climate;

/// <summary>Observed monthly climatology on a regular lat/lon grid (prepared by tools/prep_validation.py).</summary>
public sealed class ObsField
{
    public int Nt, NLat, NLon;
    public double[] Lat = Array.Empty<double>(), Lon = Array.Empty<double>();
    public float[] Data = Array.Empty<float>();
    public string Description = "";

    public static ObsField Load(string basePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(basePath + ".json"));
        var r = doc.RootElement;
        var f = new ObsField
        {
            Nt = r.GetProperty("nt").GetInt32(), NLat = r.GetProperty("nlat").GetInt32(), NLon = r.GetProperty("nlon").GetInt32(),
            Lat = r.GetProperty("lat").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            Lon = r.GetProperty("lon").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            Description = r.GetProperty("description").GetString() ?? "",
        };
        var bytes = File.ReadAllBytes(basePath + ".bin");
        f.Data = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f.Data, 0, bytes.Length);
        return f;
    }

    /// <summary>Bilinear sample (lat descending, lon ascending from 0, periodic).</summary>
    public double Sample(int t, double lat, double lon)
    {
        lon = ((lon % 360) + 360) % 360;
        int a = 0;
        while (a < NLat - 2 && Lat[a + 1] > lat) a++;
        double fy = Math.Clamp((Lat[a] - lat) / (Lat[a] - Lat[a + 1]), 0, 1);
        double dl = Lon[1] - Lon[0];
        double x = (lon - Lon[0]) / dl;
        int b = (int)Math.Floor(x); double fx = x - b;
        int b0 = ((b % NLon) + NLon) % NLon, b1 = (b0 + 1) % NLon;
        double V(int la, int lo) => Data[(t * NLat + la) * NLon + lo];
        return (1 - fy) * ((1 - fx) * V(a, b0) + fx * V(a, b1)) + fy * ((1 - fx) * V(a + 1, b0) + fx * V(a + 1, b1));
    }
}

/// <summary>Compares the model's seasonal climate with observations and writes a report.</summary>
public static class ClimateValidation
{
    public sealed record Site(string Name, double Lat, double Lon);

    /// <summary>Named places that test specific mechanisms (maritime vs continental, monsoon, deserts, rain shadows).</summary>
    public static readonly Site[] Sites =
    {
        new("London (maritime west coast)", 51.5, -0.1), new("Goose Bay, Labrador (same latitude, east coast)", 53.3, -60.4),
        new("Yakutsk (continental)", 62.0, 129.7), new("Moscow", 55.8, 37.6), new("Cairo (desert)", 30.0, 31.2),
        new("Riyadh (desert)", 24.7, 46.7), new("Mumbai (monsoon)", 19.1, 72.9), new("Kolkata (monsoon)", 22.6, 88.4),
        new("Beijing", 39.9, 116.4), new("Manaus (rainforest)", -3.1, -60.0), new("Kinshasa", -4.3, 15.3),
        new("Singapore", 1.3, 103.8), new("Lima (cold coastal desert)", -12.0, -77.0), new("Atacama", -23.5, -69.0),
        new("Sydney", -33.9, 151.2), new("Alice Springs", -23.7, 133.9), new("Seattle (windward)", 47.6, -122.3),
        new("Yakima (rain shadow)", 46.6, -120.5), new("Chicago", 41.9, -87.6), new("Mexico City (highland)", 19.4, -99.1),
        new("Lhasa (Tibet)", 29.7, 91.1), new("Baghdad (Mesopotamia)", 33.3, 44.4), new("Jericho (Levant)", 31.9, 35.5),
        new("Niger bend (Sahel)", 16.0, -1.0), new("Cape Town", -33.9, 18.4), new("Buenos Aires", -34.6, -58.4),
    };

    public static string Report(ClimateModel m, ObsField era, ObsField gpcp)
    {
        var g = m.G; int n = m.N;
        var sb = new StringBuilder();
        double[] lat = Enumerable.Range(0, n).Select(g.LatDeg).ToArray();
        double[] lon = Enumerable.Range(0, n).Select(g.LonDeg).ToArray();

        sb.AppendLine("# Climate validation (present-day forcing)\n");
        sb.AppendLine($"Observations: {era.Description}; {gpcp.Description}.\n");

        // Global diagnostics
        double gT = 0, gP = 0, aw = 0, landT = 0, landW = 0;
        for (int i = 0; i < n; i++)
        {
            double t = 0, p = 0; for (int mo = 0; mo < 12; mo++) { t += m.MonthT[mo][i]; p += m.MonthP[mo][i]; }
            gT += t / 12 * g.Area[i]; gP += p / 12 * g.Area[i]; aw += g.Area[i];
            if (m.IsLand[i]) { landT += t / 12 * g.Area[i]; landW += g.Area[i]; }
        }
        double oT = 0, oP = 0;
        for (int i = 0; i < n; i++)
        {
            double t = 0, p = 0; for (int mo = 0; mo < 12; mo++) { t += era.Sample(mo, lat[i], lon[i]); p += gpcp.Sample(mo, lat[i], lon[i]); }
            oT += t / 12 * g.Area[i]; oP += p / 12 * g.Area[i];
        }
        sb.AppendLine("## Global means\n");
        sb.AppendLine("| Quantity | Model | Observed |\n|---|---|---|");
        sb.AppendLine($"| Surface temperature (°C) | {gT / aw:F2} | {oT / aw:F2} |");
        sb.AppendLine($"| Precipitation (mm/day) | {gP / aw:F2} | {oP / aw:F2} |");
        sb.AppendLine($"| Planetary albedo (annual) | {m.AnnualPlanetaryAlbedo:F3} | ~0.29 (CERES) |");
        sb.AppendLine($"| Cloud cover (annual) | {m.GlobalMean(i => m.AnnualCloud[i]):F2} | ~0.65 (ISCCP) |");
        sb.AppendLine($"| Absorbed solar / OLR (W/m²) | {m.AnnualAbsorbed:F1} / {m.AnnualOlr:F1} | 240 / 239 (CERES) |");
        double iceN = 0, iceS = 0;
        for (int i = 0; i < n; i++) if (m.SeaIceMax[i] > 0) { if (lat[i] > 0) iceN += g.Area[i]; else iceS += g.Area[i]; }
        sb.AppendLine($"| Max sea-ice area NH / SH (M km²) | {iceN / 1e12:F1} / {iceS / 1e12:F1} | ~15 / ~18 (NSIDC) |");
        sb.AppendLine();

        // Error statistics
        sb.AppendLine("## Errors (area-weighted)\n");
        sb.AppendLine("| Field | Region | Bias | RMSE | Correlation |\n|---|---|---|---|---|");
        foreach (var (name, filter) in new (string, Func<int, bool>)[] { ("all", _ => true), ("land", i => m.IsLand[i] && Math.Abs(lat[i]) < 70), ("ocean", i => !m.IsLand[i]) })
        {
            var (b, r, c) = Stats(n, g, filter, i => Enumerable.Range(0, 12).Select(mo => (m.MonthT[mo][i], era.Sample(mo, lat[i], lon[i]))));
            sb.AppendLine($"| Monthly T (°C) | {name} | {b:F2} | {r:F2} | {c:F3} |");
        }
        foreach (var (name, filter) in new (string, Func<int, bool>)[] { ("all", _ => true), ("land", i => m.IsLand[i] && Math.Abs(lat[i]) < 70), ("ocean", i => !m.IsLand[i]) })
        {
            var (b, r, c) = Stats(n, g, filter, i => Enumerable.Range(0, 12).Select(mo => (m.MonthP[mo][i], gpcp.Sample(mo, lat[i], lon[i]))));
            sb.AppendLine($"| Monthly P (mm/day) | {name} | {b:F2} | {r:F2} | {c:F3} |");
        }
        {
            var (b, r, c) = Stats(n, g, i => m.IsLand[i] && Math.Abs(lat[i]) < 70, i =>
            {
                var mt = Enumerable.Range(0, 12).Select(mo => m.MonthT[mo][i]).ToArray();
                var ot = Enumerable.Range(0, 12).Select(mo => era.Sample(mo, lat[i], lon[i])).ToArray();
                return new[] { (mt.Max() - mt.Min(), ot.Max() - ot.Min()) };
            });
            sb.AppendLine($"| Seasonal T range (°C) | land | {b:F2} | {r:F2} | {c:F3} |");
        }
        {
            var (b, r, c) = Stats(n, g, i => m.IsLand[i] && Math.Abs(lat[i]) < 70, i =>
                new[] { (Enumerable.Range(0, 12).Sum(mo => m.MonthP[mo][i]) * 30.4, Enumerable.Range(0, 12).Sum(mo => gpcp.Sample(mo, lat[i], lon[i])) * 30.4) });
            sb.AppendLine($"| Annual P (mm/yr) | land | {b:F0} | {r:F0} | {c:F3} |");
        }
        sb.AppendLine();

        // Zonal means
        sb.AppendLine("## Zonal means (annual)\n");
        sb.AppendLine("| Lat band | T model | T obs | P model | P obs |\n|---|---|---|---|---|");
        for (int band = -80; band <= 80; band += 10)
        {
            double tm = 0, to = 0, pm = 0, po = 0, w = 0;
            for (int i = 0; i < n; i++)
            {
                if (lat[i] < band - 5 || lat[i] >= band + 5) continue;
                for (int mo = 0; mo < 12; mo++)
                {
                    tm += m.MonthT[mo][i] * g.Area[i]; to += era.Sample(mo, lat[i], lon[i]) * g.Area[i];
                    pm += m.MonthP[mo][i] * g.Area[i]; po += gpcp.Sample(mo, lat[i], lon[i]) * g.Area[i];
                }
                w += 12 * g.Area[i];
            }
            sb.AppendLine($"| {band,4}° | {tm / w,6:F1} | {to / w,6:F1} | {pm / w,5:F2} | {po / w,5:F2} |");
        }
        sb.AppendLine();

        // Energy budget by band
        sb.AppendLine("## Annual energy budget by latitude band (W/m², land | ocean)\n");
        sb.AppendLine("| Lat | SWabs | OLR | TOA net | sfc SW | LWnet | Sens | Latent | sfc net | albedo | Ts | Ta |\n|---|---|---|---|---|---|---|---|---|---|---|---|");
        for (int band = -70; band <= 70; band += 20)
        {
            string Cell(Func<int, bool> sel)
            {
                double[] acc = new double[9]; double w = 0;
                for (int i = 0; i < n; i++) { if (lat[i] < band - 10 || lat[i] >= band + 10 || !sel(i)) continue; for (int b = 0; b < 9; b++) acc[b] += m.Budget[b][i] * g.Area[i]; w += g.Area[i]; }
                if (w == 0) return "";
                for (int b = 0; b < 9; b++) acc[b] /= w;
                return $"{acc[0]:F0} | {acc[1]:F0} | {acc[0] - acc[1]:F0} | {acc[2]:F0} | {acc[3]:F0} | {acc[4]:F0} | {acc[5]:F0} | {acc[2] - acc[3] - acc[4] - acc[5]:F0} | {acc[6]:F2} | {acc[7]:F1} | {acc[8]:F1}";
            }
            sb.AppendLine($"| {band}° land | {Cell(i => m.IsLand[i])} |");
            sb.AppendLine($"| {band}° ocean | {Cell(i => !m.IsLand[i])} |");
        }
        sb.AppendLine();

        // Sites
        sb.AppendLine("## Named sites\n");
        sb.AppendLine("| Site | Jan T m/o | Jul T m/o | Annual P m/o (mm) | Wettest month m/o |\n|---|---|---|---|---|");
        foreach (var s in Sites)
        {
            int c = g.CellAt(s.Lat, s.Lon);
            double cl = g.LatDeg(c), cn = g.LonDeg(c);
            var pm = Enumerable.Range(0, 12).Select(mo => m.MonthP[mo][c]).ToArray();
            var po = Enumerable.Range(0, 12).Select(mo => gpcp.Sample(mo, cl, cn)).ToArray();
            string[] mn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            sb.AppendLine($"| {s.Name} | {m.MonthT[0][c],5:F1} / {era.Sample(0, cl, cn),5:F1} | {m.MonthT[6][c],5:F1} / {era.Sample(6, cl, cn),5:F1} | {pm.Sum() * 30.4,5:F0} / {po.Sum() * 30.4,5:F0} | {mn[Array.IndexOf(pm, pm.Max())]} / {mn[Array.IndexOf(po, po.Max())]} |");
        }
        return sb.ToString();
    }

    private static (double bias, double rmse, double corr) Stats(int n, GeodesicGrid g, Func<int, bool> filter, Func<int, IEnumerable<(double m, double o)>> pairs)
    {
        double sw = 0, sm = 0, so = 0, smm = 0, soo = 0, smo = 0, se2 = 0;
        for (int i = 0; i < n; i++)
        {
            if (!filter(i)) continue;
            foreach (var (mv, ov) in pairs(i))
            {
                if (double.IsNaN(ov)) continue;
                double w = g.Area[i];
                sw += w; sm += w * mv; so += w * ov; smm += w * mv * mv; soo += w * ov * ov; smo += w * mv * ov; se2 += w * (mv - ov) * (mv - ov);
            }
        }
        double mm = sm / sw, mo = so / sw;
        double cov = smo / sw - mm * mo, vm = smm / sw - mm * mm, vo = soo / sw - mo * mo;
        return (mm - mo, Math.Sqrt(se2 / sw), cov / Math.Sqrt(vm * vo));
    }
}
