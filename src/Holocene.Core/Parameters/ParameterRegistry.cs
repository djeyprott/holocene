using System.Text.Json;

namespace Holocene.Core.Parameters;

/// <summary>One registered quantity: value, unit and the reason it has that value.</summary>
public sealed record Parameter(string Key, double Value, string Unit, string Kind, string Source, string Note);

/// <summary>
/// Every number in the simulation that is not a physical constant computed in code, measured data, or
/// an emergent result lives here, with a unit and a justification (design principle 1).
/// Kinds: "constant" (physical constant), "empirical" (published measurement/regression),
/// "numerical" (needed by the numerical method, e.g. resolution-dependent friction),
/// "tunable" (calibrated; must say against what).
/// </summary>
public sealed class ParameterRegistry
{
    private readonly Dictionary<string, Parameter> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new();

    public IReadOnlyCollection<Parameter> All => _items.Values;
    public IReadOnlyCollection<string> Used => _used;

    public static ParameterRegistry Load(string path)
    {
        var reg = new ParameterRegistry();
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var group in doc.RootElement.EnumerateObject())
        {
            foreach (var p in group.Value.EnumerateObject())
            {
                var e = p.Value;
                string key = group.Name + "." + p.Name;
                string kind = e.TryGetProperty("kind", out var k) ? k.GetString()! : "";
                string source = e.TryGetProperty("source", out var s) ? s.GetString()! : "";
                if (kind is not ("constant" or "empirical" or "numerical" or "tunable"))
                    throw new InvalidDataException($"Parameter {key}: kind must be constant/empirical/numerical/tunable");
                if (string.IsNullOrWhiteSpace(source))
                    throw new InvalidDataException($"Parameter {key}: every parameter needs a source or justification");
                reg._items[key] = new Parameter(key, e.GetProperty("value").GetDouble(),
                    e.TryGetProperty("unit", out var u) ? u.GetString()! : "",
                    kind, source, e.TryGetProperty("note", out var n) ? n.GetString()! : "");
            }
        }
        return reg;
    }

    public double this[string key]
    {
        get
        {
            if (!_items.TryGetValue(key, out var p)) throw new KeyNotFoundException($"Unregistered parameter '{key}'. Add it to data/parameters.json with a source.");
            lock (_used) _used.Add(key);
            return p.Value;
        }
    }

    /// <summary>Override a value (for sensitivity experiments and tests). Keeps provenance.</summary>
    public void Override(string key, double value)
    {
        var p = _items[key];
        _items[key] = p with { Value = value, Note = p.Note + $" [overridden from {p.Value}]" };
    }
}
