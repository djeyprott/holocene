using System.Diagnostics;
using Holocene.Core.Earth;
using Holocene.Core.Grid;
using Holocene.Core.World;
using Holocene.Core.Climate;
using Holocene.Core.Parameters;

namespace Holocene.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var opts = Options.Parse(args);
        switch (opts.Command)
        {
            case "":
            case "view":
                // Double-clicking the executable lands here: open the bundled worlds in the browser.
                return View(opts);
            case "help": case "--help": case "-h":
                Usage(); return 0;
            case "build-world": BuildWorld(opts); return 0;
            case "validate-climate": ValidateClimate(opts); return 0;
            case "debug-climate": DebugClimate(opts); return 0;
            case "world": BuildEpoch(opts); return 0;
            case "rehydro": ReHydro(opts); return 0;
            case "serve": ViewerExport.Serve(Path.Combine(opts.Root, "viewer"), opts.Extra.TryGetValue("port", out var pt) ? int.Parse(pt) : 8080); return 0;
            default:
                Usage(); return 1;
        }
    }

    private static int View(Options o)
    {
        int port = o.Extra.TryGetValue("port", out var pt) ? int.Parse(pt) : FreePort();
        string url = $"http://localhost:{port}/";
        Console.WriteLine("Holocene world viewer. Close this window to stop it.\nRun with 'help' for the simulation commands.");
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { Console.WriteLine($"Open {url} in your browser."); }
        ViewerExport.Serve(Path.Combine(o.Root, "viewer"), port);
        return 0;
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start(); int p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    private static void Usage()
    {
                Console.WriteLine("usage: holocene [command] [options]\n" +
                    "  (no command) / view              open the world viewer in your browser\n" +
                    "  build-world                      import Earth terrain onto the grid\n" +
                    "  validate-climate [--years N]     present-day climate vs ERA-Interim/GPCP -> docs/climate-validation.md\n" +
                    "  world --year Y [--years N]       physical world at year Y (astronomical: 10,000 BCE = -9999) -> viewer data\n" +
                    "  serve [--port 8080]              serve the browser viewer\n" +
                    "common: --freq F (grid frequency, default 64; 32 is ~4x faster), --<group.param> value (override a parameter)");
    }

    public static (GeodesicGrid, Terrain) LoadOrBuildWorld(Options o)
    {
        var sw = Stopwatch.StartNew();
        var grid = new GeodesicGrid(o.Freq);
        var hexAreas = Enumerable.Range(0, grid.Count).Where(i => grid.Degree(i) == 6).Select(i => grid.Area[i]).ToArray();
        Console.WriteLine($"grid f={o.Freq}: {grid.Count} cells, mean spacing {Math.Sqrt(grid.Area.Average()) / 1000:F0} km, area max/min {grid.Area.Max() / grid.Area.Min():F3} (hexagons {hexAreas.Max() / hexAreas.Min():F3}, pentagon/mean {Enumerable.Range(0, grid.Count).Where(i => grid.Degree(i) == 5).Average(i => grid.Area[i]) / grid.Area.Average():F3}) ({sw.ElapsedMilliseconds} ms)");
        string cache = Path.Combine(o.Root, "data", "cache", $"terrain_f{o.Freq}_v{GeodesicGrid.Version}.bin");
        if (File.Exists(cache))
        {
            using var r = new BinaryReader(File.OpenRead(cache));
            return (grid, Terrain.Load(r, grid));
        }
        sw.Restart();
        var data = Etopo5.Read(Path.Combine(o.Root, "data", "earth", "ETOPO5.DAT"));
        var terrain = Etopo5.BuildTerrain(grid, data);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        using (var w = new BinaryWriter(File.Create(cache))) terrain.Save(w);
        Console.WriteLine($"terrain imported from ETOPO5 ({sw.ElapsedMilliseconds} ms)");
        return (grid, terrain);
    }

    public static ParameterRegistry LoadParameters(Options o)
    {
        var p = ParameterRegistry.Load(Path.Combine(o.Root, "data", "parameters.json"));
        foreach (var (k, v) in o.Extra) if (k.Contains('.')) p.Override(k, double.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
        return p;
    }

    private static void ValidateClimate(Options o)
    {
        var (g, t) = LoadOrBuildWorld(o);
        var p = LoadParameters(o);
        int years = o.Extra.TryGetValue("years", out var ys) ? int.Parse(ys) : (int)p["numerics.spinup_years"];
        var forcing = new ClimateForcing(o.Year, o.Extra.TryGetValue("co2", out var c) ? double.Parse(c) : 370, 0, p["planet.solar_constant"]);
        Console.WriteLine($"orbit {o.Year}: e={forcing.Orbit.Eccentricity:F5} obliquity={forcing.Orbit.ObliquityRad * 180 / Math.PI:F3}° perihelion={forcing.Orbit.PerihelionDeg:F1}°");
        var model = new ClimateModel(g, t, p, forcing) { VegetationSpinupFast = o.Extra.GetValueOrDefault("fastveg", "1") == "1" };
        var sw = Stopwatch.StartNew();
        model.RunYears(years, y =>
        {
            double gt = model.GlobalMean(i => Enumerable.Range(0, 12).Average(m => model.MonthT[m][i]));
            double gp = model.GlobalMean(i => Enumerable.Range(0, 12).Average(m => model.MonthP[m][i]));
            double ice = 0; for (int i = 0; i < g.Count; i++) if (model.SeaIceMax[i] > 0) ice += g.Area[i];
            double glac = 0; for (int i = 0; i < g.Count; i++) if (model.IsLand[i] && model.Snow[i] > p["hydro.glacier_swe"]) glac += g.Area[i];
            Console.WriteLine($"year {y + 1,3}: T {gt,6:F2} °C  P {gp,5:F2} mm/d  sea-ice max {ice / 1e12,5:F1} Mkm²  glacier {glac / 1e12,5:F1} Mkm²  ITCZ(last) {model.ItczLat,5:F1}  SW {model.AnnualAbsorbed:F1} OLR {model.AnnualOlr:F1} alb {model.AnnualPlanetaryAlbedo:F3} cld {model.GlobalMean(i => model.AnnualCloud[i]):F2}  ({sw.Elapsed.TotalSeconds:F0} s)");
            Console.WriteLine("   profile: " + string.Join(", ", model.Profile.Select(kv => $"{kv.Key} {kv.Value:F1}s")) + $"; iters T {model.LastTempIters} q {model.LastMoistIters} newton {model.LastNewton}");
        });
        var era = ObsField.Load(Path.Combine(o.Root, "data", "validation", "era_t2m_2005"));
        var gpcp = ObsField.Load(Path.Combine(o.Root, "data", "validation", "gpcp_precip_clim"));
        string report = ClimateValidation.Report(model, era, gpcp);
        string outPath = o.Extra.TryGetValue("report", out var rp) ? rp : Path.Combine(o.Root, "docs", "climate-validation.md");
        File.WriteAllText(outPath, report);
        Console.WriteLine(report);
        string state = o.Extra.TryGetValue("state", out var sp) ? sp : Path.Combine(o.Root, "data", "cache", $"climate_f{o.Freq}_{o.Year}.bin");
        ClimateIO.Save(model, state);
        File.WriteAllLines(Path.Combine(o.Root, "data", "cache", $"grid_f{o.Freq}.csv"), Enumerable.Range(0, g.Count).Select(i => $"{g.LatDeg(i):F4},{g.LonDeg(i):F4}"));
    }

    private static void DebugClimate(Options o)
    {
        var (g, t) = LoadOrBuildWorld(o);
        var p = LoadParameters(o);
        var model = new ClimateModel(g, t, p, new ClimateForcing(o.Year, 370, 0, p["planet.solar_constant"])) { VegetationSpinupFast = true };
        string R(double[] a) => $"[{a.Min():G4}, {a.Average():G4}, {a.Max():G4}]";
        model.AfterComponent = name =>
        {
            double ws = model.Wind.Max(v => v.Length), wr = model.WindRot.Max(v => v.Length);
            Console.WriteLine($"  {name,-12} Ta {R(model.Ta)} Ts {R(model.Ts)} W {R(model.W)} P {R(model.Precip)} alb {R(model.Albedo)} pres {R(model.Pressure)} wind max {ws:F1}/{wr:F1} ice {R(model.SeaIce)} it T{model.LastTempIters} q{model.LastMoistIters}/{model.LastNewton}");
        };
        if (o.Extra.TryGetValue("probe", out var probeYears))
        {
            model.AfterComponent = null;
            model.RunYears(int.Parse(probeYears));
            var sites = new[] { ("Moscow", 55.8, 37.6), ("Cairo", 30.0, 31.2), ("Yakutsk", 62.0, 129.7) };
            model.AfterComponent = name =>
            {
                if (name != "surface") return;
                foreach (var (sn, la, lo) in sites)
                {
                    int c = g.CellAt(la, lo);
                    Console.WriteLine($"  {sn,-8} Ta {model.SurfaceT(c),6:F1} Ts {model.SkinT(c),6:F1} ins {model.Insol[c],4:F0} alb {model.Albedo[c]:F2} cld {model.Cloud[c]:F2} snow {model.Snow[c],6:F0} soil {model.Soil[c],4:F0} E {model.Evap[c],5:F2} P {model.Precip[c],5:F2} veg {model.Veg[c]:F2} for {model.Forest[c]:F2} rh {model.RhEff[c]:F2} wind {model.Wind[c].Length,4:F1} pres {model.Pressure[c],6:F1}");
                }
            };
            for (int s = 0; s < model.Steps; s++) { Console.WriteLine($"step {s}"); model.Step(s); }
            return;
        }
        int steps = o.Extra.TryGetValue("steps", out var st) ? int.Parse(st) : 3;
        for (int s = 0; s < steps; s++)
        {
            Console.WriteLine($"step {s}"); model.Step(s);
            var top = Enumerable.Range(0, g.Count).OrderByDescending(i => model.Precip[i]).Take(4);
            foreach (var i in top)
                Console.WriteLine($"    wet cell {i} ({g.LatDeg(i):F1},{g.LonDeg(i):F1}) land={model.IsLand[i]} elev={model.Elev[i]:F0} P={model.Precip[i]:F1} E={model.Evap[i]:F1} W={model.W[i]:F1} Ta={model.Ta[i]:F1} Ts={model.Ts[i]:F1} wind={model.Wind[i].Length:F1}");
            var hot = Enumerable.Range(0, g.Count).OrderByDescending(i => model.Ta[i]).First();
            Console.WriteLine($"    hottest {hot} ({g.LatDeg(hot):F1},{g.LonDeg(hot):F1}) Ta={model.Ta[hot]:F1} P={model.Precip[hot]:F1}");
        }
    }

    /// <summary>Simulates the physical world for one epoch with Earth-mode forcing and exports it to the viewer.</summary>
    private static void BuildEpoch(Options o)
    {
        var (g, t) = LoadOrBuildWorld(o);
        var p = LoadParameters(o);
        var earth = new EarthForcing(Path.Combine(o.Root, "data", "earth"));
        double year = o.Year;
        var forcing = new ClimateForcing(year, earth.Co2(year), earth.SeaLevel(year), p["planet.solar_constant"]);
        string label = year <= 0 ? $"{1 - year:0} BCE" : $"{year:0} CE";
        Console.WriteLine($"{label}: CO2 {forcing.Co2Ppm:F0} ppm, sea level {forcing.SeaLevel:F0} m, e={forcing.Orbit.Eccentricity:F4}, obliquity {forcing.Orbit.ObliquityRad * 180 / Math.PI:F2}°, perihelion {forcing.Orbit.PerihelionDeg:F0}°");
        int years = o.Extra.TryGetValue("years", out var ys) ? int.Parse(ys) : (int)p["numerics.spinup_years"];
        var model = new ClimateModel(g, t, p, forcing) { VegetationSpinupFast = true };
        var sw = Stopwatch.StartNew();
        model.RunYears(years, y => { if ((y + 1) % 5 == 0) Console.WriteLine($"  year {y + 1}/{years}  T {model.GlobalMean(i => model.Budget[8][i]):F2} °C  ({sw.Elapsed.TotalSeconds:F0} s)"); });
        var snap = WorldSnapshot.Build(model, t, forcing, p["hydro.glacier_swe"]);
        if (o.Extra.ContainsKey("validate"))
        {
            var era = ObsField.Load(Path.Combine(o.Root, "data", "validation", "era_t2m_2005"));
            var gpcp = ObsField.Load(Path.Combine(o.Root, "data", "validation", "gpcp_precip_clim"));
            File.WriteAllText(Path.Combine(o.Root, "docs", "climate-validation.md"),
                $"Grid f{o.Freq} ({g.Count} cells), {years}-year spin-up, year {year}, CO2 {forcing.Co2Ppm:F0} ppm.\n\n" + ClimateValidation.Report(model, era, gpcp));
            Console.WriteLine("validation report written to docs/climate-validation.md");
        }
        var viewerData = Path.Combine(o.Root, "viewer", "data");
        ViewerExport.ExportGrid(g, viewerData);
        string id = o.Extra.TryGetValue("id", out var idv) ? idv : $"y{year:0}_f{o.Freq}";
        ViewerExport.ExportSnapshot(snap, viewerData, id, $"{label} (grid f{o.Freq})", o.Extra.GetValueOrDefault("notes"));
        double land = Enumerable.Range(0, g.Count).Where(i => model.IsLand[i]).Sum(i => g.Area[i]);
        Console.WriteLine($"land {land / 1e12:F1} M km², lakes {snap.Hydro.Lakes.Count(l => l.Area > 5e9)} (>5000 km²), largest rivers:");
        foreach (var c in Enumerable.Range(0, g.Count).Where(i => model.IsLand[i] && snap.Hydro.Downstream[i] >= 0 && !model.IsLand[snap.Hydro.Downstream[i]]).OrderByDescending(i => snap.Hydro.Discharge[i]).Take(8))
            Console.WriteLine($"   mouth at ({g.LatDeg(c):F1}, {g.LonDeg(c):F1}): {snap.Hydro.Discharge[c]:F0} m³/s");
        foreach (var l in snap.Hydro.Lakes.OrderByDescending(l => l.Area).Take(6))
            Console.WriteLine($"   lake at ({l.Cells.Average(c => g.LatDeg(c)):F1}, {l.Cells.Average(c => g.LonDeg(c)):F1}): {l.Area / 1e6:F0} km², level {l.Level:F0} m, {(l.Terminal ? "terminal" : "overflowing")}");
        Console.WriteLine($"exported snapshot '{id}' to {viewerData}");
    }

    /// <summary>Recomputes rivers, lakes and biomes of an exported snapshot from its climate layers (no climate rerun).</summary>
    private static void ReHydro(Options o)
    {
        var (g, t) = LoadOrBuildWorld(o);
        string dataDir = Path.Combine(o.Root, "viewer", "data");
        var (doc, old) = ViewerExport.ReadSnapshot(dataDir, o.Extra["id"]);
        var root = doc.RootElement;
        float[] L(string name) => old[name];
        double sea = root.GetProperty("sea_level").GetDouble();
        var land = L("land").Select(v => v > 0.5f).ToArray();
        var hydro = new Hydrology(g, t, land, sea, L("runoff").Select(v => (double)v).ToArray(), L("pet_ann").Select(v => (double)v).ToArray());
        var koppen = root.GetProperty("koppen").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var forest = L("forest"); var veg = L("veg"); var glac = L("glacier"); var tann = L("t_ann");
        var biome = new Biome[g.Count];
        for (int i = 0; i < g.Count; i++)
            biome[i] = !land[i] ? Biome.Ocean : hydro.LakeOf[i] >= 0 ? Biome.Lake : Biomes.Refine(Biomes.FromKoppen(koppen[i], glac[i] > 0), forest[i], veg[i], tann[i]);
        var layers = new Dictionary<string, float[]>(old);
        layers["discharge"] = hydro.Discharge.Select(x => (float)x).ToArray();
        layers["downstream"] = hydro.Downstream.Select(x => (float)x).ToArray();
        layers["lake"] = hydro.LakeOf.Select(x => (float)x).ToArray();
        layers["biome"] = biome.Select(b => (float)(int)b).ToArray();
        var snap = new WorldSnapshot { YearCE = root.GetProperty("year").GetDouble(), SeaLevel = sea, Co2 = root.GetProperty("co2").GetDouble(), Grid = g, Layers = layers, Hydro = hydro, Koppen = koppen, Biome = biome };
        ViewerExport.ExportSnapshot(snap, Path.Combine(o.Root, "viewer", "data"), o.Extra["id"], root.GetProperty("title").GetString()!, root.TryGetProperty("notes", out var nt) ? nt.GetString() : null);
        foreach (var c in Enumerable.Range(0, g.Count).Where(i => land[i] && hydro.Downstream[i] >= 0 && !land[hydro.Downstream[i]]).OrderByDescending(i => hydro.Discharge[i]).Take(8))
            Console.WriteLine($"   mouth at ({g.LatDeg(c):F1}, {g.LonDeg(c):F1}): {hydro.Discharge[c]:F0} m³/s");
        foreach (var l in hydro.Lakes.OrderByDescending(l => l.Area).Take(8))
            Console.WriteLine($"   lake at ({l.Cells.Average(c => g.LatDeg(c)):F1}, {l.Cells.Average(c => g.LonDeg(c)):F1}): {l.Area / 1e6:F0} km², level {l.Level:F0} m, {(l.Terminal ? "terminal" : "overflowing")}");
    }

    private static void BuildWorld(Options o)
    {
        var (g, t) = LoadOrBuildWorld(o);
        foreach (double sl in new[] { 0.0, -60.0, -130.0 })
        {
            double land = 0;
            for (int i = 0; i < g.Count; i++) land += g.Area[i] * t.FractionAbove(i, sl);
            Console.WriteLine($"sea level {sl,5} m: land {land / 1e12:F1} M km² ({land / g.Area.Sum() * 100:F1}%)");
        }
        foreach (var (name, lat, lon) in new[] { ("Everest", 27.99, 86.93), ("Tibet", 33.0, 88.0), ("Dogger Bank", 54.8, 2.5), ("Bering Strait", 65.8, -169.0), ("Amazonia", -3.0, -60.0) })
        {
            int c = g.CellAt(lat, lon);
            Console.WriteLine($"{name,-14} cell {c}: mean {t.Mean(c),7:F0} m  min {t.Min(c),6}  max {t.Max(c),6}  std {t.StdDev(c),5:F0}  land@0 {t.FractionAbove(c, 0):F2}  land@-60 {t.FractionAbove(c, -60):F2}");
        }
    }
}

public sealed class Options
{
    public string Command = "";
    public int Freq = 64;
    public string Root = FindRoot();
    public int Year = 1990;
    public Dictionary<string, string> Extra = new();

    public static Options Parse(string[] a)
    {
        var o = new Options { Command = a.Length > 0 ? a[0] : "" };
        for (int i = 1; i < a.Length - 1; i++)
        {
            switch (a[i])
            {
                case "--freq": o.Freq = int.Parse(a[++i]); break;
                case "--root": o.Root = a[++i]; break;
                case "--year": o.Year = int.Parse(a[++i]); break;
                default: if (a[i].StartsWith("--")) o.Extra[a[i][2..]] = a[++i]; break;
            }
        }
        return o;
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        // Source checkout (Holocene.sln) or release package (data/parameters.json next to the executable).
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Holocene.sln")) && !File.Exists(Path.Combine(d.FullName, "data", "parameters.json"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }
}
