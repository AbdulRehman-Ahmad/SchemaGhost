using System.Text.Json;
using System.Text.Json.Nodes;

namespace SchemaGhost;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public record Leaf(string Path, string Type, string Sample);

public record FieldMapping
{
    public string LegacyPath { get; init; } = "";
    public string? ModernPath { get; init; }
    public string Transform { get; init; } = "none";
    public double Confidence { get; init; }
    public string Reason { get; init; } = "";
    public string Source { get; init; } = "";
}

public record MappingProfile
{
    public int Version { get; init; } = 1;
    public List<FieldMapping> Mappings { get; init; } = new();
}

public static class JsonPaths
{
    public static string TypeOf(JsonNode? n) => n is null ? "null" : n.GetValueKind() switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => "null"
    };

    /// <summary>Flattens objects into dotted leaf paths. Arrays are treated as leaves (MVP limitation).</summary>
    public static List<Leaf> Flatten(JsonNode? root)
    {
        var acc = new List<Leaf>();
        Walk(root, "", acc);
        return acc;
    }

    private static void Walk(JsonNode? n, string path, List<Leaf> acc)
    {
        if (n is JsonObject o && o.Count > 0)
        {
            foreach (var kv in o) Walk(kv.Value, path.Length == 0 ? kv.Key : path + "." + kv.Key, acc);
            return;
        }
        if (path.Length == 0) return;
        var s = n?.ToJsonString() ?? "null";
        if (s.Length > 40) s = s[..37] + "...";
        acc.Add(new Leaf(path, TypeOf(n), s));
    }

    public static bool TryGet(JsonNode? root, string path, out JsonNode? value)
    {
        var cur = root;
        foreach (var part in path.Split('.'))
        {
            if (cur is JsonObject o && o.TryGetPropertyValue(part, out var next)) cur = next;
            else { value = null; return false; }
        }
        value = cur;
        return true;
    }

    public static JsonNode? Get(JsonNode? root, string path) => TryGet(root, path, out var v) ? v : null;

    public static void Put(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var cur = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (cur[parts[i]] is not JsonObject next) { next = new JsonObject(); cur[parts[i]] = next; }
            cur = next;
        }
        cur[parts[^1]] = value?.DeepClone();
    }

    /// <summary>Canonical string form of a value, so 1024 and "1024" compare equal.</summary>
    public static string Canon(JsonNode? n)
    {
        if (n is null) return "null";
        if (n is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return n.ToJsonString();
    }
}

public record SchemaDiff(List<Leaf> Removed, List<Leaf> Added, List<(Leaf Old, Leaf New)> TypeChanged, int Unchanged);

public static class Comparer
{
    public static SchemaDiff Compare(List<Leaf> legacy, List<Leaf> modern)
    {
        var mm = modern.ToDictionary(x => x.Path);
        var lm = legacy.ToDictionary(x => x.Path);
        var removed = legacy.Where(l => !mm.ContainsKey(l.Path)).ToList();
        var added = modern.Where(m => !lm.ContainsKey(m.Path)).ToList();
        var changed = legacy.Where(l => mm.TryGetValue(l.Path, out var m) && m.Type != l.Type)
                            .Select(l => (l, mm[l.Path])).ToList();
        var same = legacy.Count(l => mm.TryGetValue(l.Path, out var m) && m.Type == l.Type);
        return new SchemaDiff(removed, added, changed, same);
    }
}
