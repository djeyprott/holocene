using System.Net;
using System.Text.Json;
using Holocene.Core.Grid;
using Holocene.Core.World;

namespace Holocene.Cli;

/// <summary>
/// Writes a world snapshot for the browser viewer: binary float32 layers plus a JSON manifest.
/// viewer/data/grid_f{F}.bin holds geometry shared by all snapshots of a resolution.
/// </summary>
public static class ViewerExport
{
    private static string B64(float[] a) { var b = new byte[a.Length * 4]; Buffer.BlockCopy(a, 0, b, 0, b.Length); return Convert.ToBase64String(b); }
    private static string B64(int[] a) { var b = new byte[a.Length * 4]; Buffer.BlockCopy(a, 0, b, 0, b.Length); return Convert.ToBase64String(b); }

    /// <summary>Grid geometry as JSON with base64 little-endian arrays: cell lat/lon, corner start indices, corner lat/lon.</summary>
    public static void ExportGrid(GeodesicGrid g, string dir)
    {
        Directory.CreateDirectory(dir);
        var grid = new
        {
            n = g.Count,
            lat = B64(Enumerable.Range(0, g.Count).Select(i => (float)g.LatDeg(i)).ToArray()),
            lon = B64(Enumerable.Range(0, g.Count).Select(i => (float)g.LonDeg(i)).ToArray()),
            start = B64(g.NbrStart),
            clat = B64(g.Corner.Select(c => (float)(c.Lat * 180 / Math.PI)).ToArray()),
            clon = B64(g.Corner.Select(c => (float)(c.Lon * 180 / Math.PI)).ToArray()),
        };
        File.WriteAllText(Path.Combine(dir, $"grid_f{g.Frequency}.json"), JsonSerializer.Serialize(grid));
    }

    /// <summary>One self-contained JSON bundle per snapshot: manifest fields plus every layer as base64 float32.</summary>
    public static void ExportSnapshot(WorldSnapshot s, string dir, string id, string title, string? notes = null)
    {
        Directory.CreateDirectory(dir);
        var lakes = s.Hydro.Lakes.Where(l => l.Area > 5e9).OrderByDescending(l => l.Area).Select(l => new
        {
            id = l.Id, level = Math.Round(l.Level, 1), spill = Math.Round(l.SpillLevel, 1), area_km2 = Math.Round(l.Area / 1e6),
            terminal = l.Terminal, inflow_m3s = Math.Round(l.InflowM3s), cells = l.Cells.Length,
            lat = Math.Round(l.Cells.Average(c => s.Grid.LatDeg(c)), 2), lon = Math.Round(l.Cells.Average(c => s.Grid.LonDeg(c)), 2),
        });
        var bundle = new
        {
            id, title, notes, year = s.YearCE, sea_level = s.SeaLevel, co2 = s.Co2, grid = $"grid_f{s.Grid.Frequency}.json",
            koppen = s.Koppen, lakes, biomes = Enum.GetNames<Biome>(),
            layers = s.Layers.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => B64(kv.Value)),
        };
        File.WriteAllText(Path.Combine(dir, id + ".json"), JsonSerializer.Serialize(bundle));
        string idx = Path.Combine(dir, "index.json");
        var list = File.Exists(idx) ? JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(File.ReadAllText(idx))! : new();
        list.RemoveAll(e => e["id"].GetString() == id);
        list.Add(new Dictionary<string, JsonElement> { ["id"] = JsonSerializer.SerializeToElement(id), ["title"] = JsonSerializer.SerializeToElement(title), ["year"] = JsonSerializer.SerializeToElement(s.YearCE) });
        File.WriteAllText(idx, JsonSerializer.Serialize(list.OrderBy(e => e["year"].GetDouble())));
    }

    /// <summary>Reads a snapshot bundle back: (root JSON, layers).</summary>
    public static (JsonDocument doc, Dictionary<string, float[]> layers) ReadSnapshot(string dir, string id)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, id + ".json")));
        var layers = new Dictionary<string, float[]>();
        foreach (var p in doc.RootElement.GetProperty("layers").EnumerateObject())
        {
            var b = Convert.FromBase64String(p.Value.GetString()!); var a = new float[b.Length / 4];
            Buffer.BlockCopy(b, 0, a, 0, b.Length); layers[p.Name] = a;
        }
        return (doc, layers);
    }

    /// <summary>Minimal static file server for the viewer (browsers block file:// data loading).</summary>
    public static void Serve(string root, int port)
    {
        var l = new HttpListener();
        l.Prefixes.Add($"http://localhost:{port}/");
        l.Start();
        Console.WriteLine($"Viewer at http://localhost:{port}/  (Ctrl+C to stop)");
        var types = new Dictionary<string, string> { [".html"] = "text/html", [".js"] = "text/javascript", [".css"] = "text/css", [".json"] = "application/json", [".bin"] = "application/octet-stream" };
        while (true)
        {
            var ctx = l.GetContext();
            string rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath.TrimStart('/'));
            if (rel == "") rel = "index.html";
            string full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root)) || !File.Exists(full)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }
            ctx.Response.ContentType = types.GetValueOrDefault(Path.GetExtension(full), "application/octet-stream");
            var bytes = File.ReadAllBytes(full);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.Close();
        }
    }
}
