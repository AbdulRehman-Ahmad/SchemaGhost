using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SchemaGhost;

public static class Transforms
{
    public static readonly string[] All = { "none", "toString", "toInt", "toDouble", "toBool" };

    public static string Infer(string legacyType, string modernType)
    {
        if (legacyType == modernType) return "none";
        return legacyType switch
        {
            "string" => "toString",
            "number" => "toDouble",
            "boolean" => "toBool",
            _ => "none"
        };
    }

    public static JsonNode? Apply(string transform, JsonNode? v)
    {
        if (v is null) return null;
        var s = JsonPaths.Canon(v);
        switch (transform)
        {
            case "toString": return JsonValue.Create(s);
            case "toInt":
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return JsonValue.Create(i);
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d1) && d1 == Math.Floor(d1)) return JsonValue.Create((long)d1);
                return null;
            case "toDouble":
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2) ? JsonValue.Create(d2) : null;
            case "toBool":
                return bool.TryParse(s, out var b) ? JsonValue.Create(b) : null;
            default: return v.DeepClone();
        }
    }
}

public static class Engine
{
    /// <summary>Deterministic guard: whatever the model proposed, only valid paths and known transforms survive.</summary>
    public static List<FieldMapping> Sanitize(List<FieldMapping> raw, JsonNode legacy, JsonNode modern)
    {
        var modernPaths = JsonPaths.Flatten(modern).Select(l => l.Path).ToHashSet();
        var result = new List<FieldMapping>();
        foreach (var leaf in JsonPaths.Flatten(legacy))
        {
            var m = raw.FirstOrDefault(x => x.LegacyPath == leaf.Path);
            if (m is null)
                result.Add(new FieldMapping { LegacyPath = leaf.Path, Reason = "no proposal returned" });
            else if (m.ModernPath is not null && !modernPaths.Contains(m.ModernPath))
                result.Add(new FieldMapping { LegacyPath = leaf.Path, Reason = $"rejected: proposed path '{m.ModernPath}' does not exist" });
            else
                result.Add(m with
                {
                    Transform = Transforms.All.Contains(m.Transform) ? m.Transform : "none",
                    Confidence = m.ModernPath is null ? 0 : Math.Clamp(m.Confidence, 0, 1)
                });
        }
        return result;
    }

    public static JsonObject Apply(MappingProfile profile, JsonNode modern)
    {
        var result = new JsonObject();
        foreach (var m in profile.Mappings.Where(m => m.ModernPath is not null))
            if (JsonPaths.TryGet(modern, m.ModernPath!, out var src))
                JsonPaths.Put(result, m.LegacyPath, Transforms.Apply(m.Transform, src));
        return result;
    }
}

public record Check(string LegacyPath, string Status, string Detail);

public record Validation(List<Check> Checks, JsonObject Output, int LegacyLeaves)
{
    public int Passed => Checks.Count(c => c.Status == "pass");
    public int Failed => Checks.Count(c => c.Status == "fail");
    public double Coverage => LegacyLeaves == 0 ? 0 : (double)Passed / LegacyLeaves;
}

public static class Validator
{
    public static Validation Validate(MappingProfile profile, JsonNode legacy, JsonNode modern)
    {
        var legacyLeaves = JsonPaths.Flatten(legacy);
        var checks = new List<Check>();

        foreach (var leaf in legacyLeaves)
        {
            var m = profile.Mappings.FirstOrDefault(x => x.LegacyPath == leaf.Path && x.ModernPath is not null);
            if (m is null) { checks.Add(new Check(leaf.Path, "skip", "no approved mapping")); continue; }

            if (!JsonPaths.TryGet(modern, m.ModernPath!, out var src))
            { checks.Add(new Check(leaf.Path, "fail", $"source '{m.ModernPath}' not found")); continue; }

            var converted = Transforms.Apply(m.Transform, src);
            if (converted is null && src is not null)
            { checks.Add(new Check(leaf.Path, "fail", $"transform '{m.Transform}' failed")); continue; }

            var gotType = JsonPaths.TypeOf(converted);
            if (leaf.Type != "null" && gotType != leaf.Type)
            { checks.Add(new Check(leaf.Path, "fail", $"type {gotType}, expected {leaf.Type}")); continue; }

            var legacyValue = JsonPaths.Get(legacy, leaf.Path);
            var same = JsonPaths.Canon(converted) == JsonPaths.Canon(legacyValue);
            checks.Add(new Check(leaf.Path, "pass", same ? "type ok, value identical" : "type ok, value differs (different sample?)"));
        }
        return new Validation(checks, Engine.Apply(profile, modern), legacyLeaves.Count);
    }
}

public static class AdapterGenerator
{
    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string Generate(MappingProfile profile, string ns = "Legacy.Compat")
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated by SchemaGhost>. Review before use.");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Turns the modern API response into the shape the legacy application expects.</summary>");
        sb.AppendLine("public static class CompatibilityAdapter");
        sb.AppendLine("{");
        sb.AppendLine("    public static JsonObject Adapt(JsonNode modern)");
        sb.AppendLine("    {");
        sb.AppendLine("        var result = new JsonObject();");
        foreach (var m in profile.Mappings.Where(m => m.ModernPath is not null))
            sb.AppendLine($"        Put(result, \"{Esc(m.LegacyPath)}\", Coerce(Get(modern, \"{Esc(m.ModernPath!)}\"), \"{m.Transform}\")); // confidence {m.Confidence:0.00}");
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine(Helpers);
        sb.AppendLine("}");
        return sb.ToString();
    }

    private const string Helpers = """

        private static JsonNode? Get(JsonNode? root, string path)
        {
            var cur = root;
            foreach (var part in path.Split('.'))
            {
                if (cur is JsonObject o && o.TryGetPropertyValue(part, out var next)) cur = next;
                else return null;
            }
            return cur;
        }

        private static void Put(JsonObject root, string path, JsonNode? value)
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

        private static JsonNode? Coerce(JsonNode? v, string transform)
        {
            if (v is null) return null;
            var s = v is JsonValue jv && jv.TryGetValue<string>(out var str) ? str : v.ToJsonString();
            switch (transform)
            {
                case "toString": return JsonValue.Create(s);
                case "toInt":
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return JsonValue.Create(i);
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d1) && d1 == Math.Floor(d1)) return JsonValue.Create((long)d1);
                    return null;
                case "toDouble":
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2) ? JsonValue.Create(d2) : null;
                case "toBool":
                    return bool.TryParse(s, out var b) ? JsonValue.Create(b) : null;
                default: return v;
            }
        }
    """;
}

public record BenchCase
{
    public string Name { get; init; } = "";
    public string Source { get; init; } = "";
    public JsonNode? Legacy { get; init; }
    public JsonNode? Modern { get; init; }
    public Dictionary<string, string?> Expected { get; init; } = new();
}

public record BenchResult(string Name, int Total, int Correct, int Wrong, int Abstained);

public static class Bench
{
    /// <summary>Correct = matches expectation (incl. correctly abstaining). Wrong = confident but incorrect (unsafe). Abstained = declined a mappable field.</summary>
    public static BenchResult Score(BenchCase c, List<FieldMapping> sanitized, double threshold)
    {
        int ok = 0, wrong = 0, abst = 0;
        foreach (var (path, expected) in c.Expected)
        {
            var m = sanitized.FirstOrDefault(x => x.LegacyPath == path);
            var pred = m is { ModernPath: not null } && m.Confidence >= threshold ? m.ModernPath : null;
            if (pred == expected) ok++;
            else if (pred is null) abst++;
            else wrong++;
        }
        return new BenchResult(c.Name, c.Expected.Count, ok, wrong, abst);
    }
}
