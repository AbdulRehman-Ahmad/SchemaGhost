using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SchemaGhost.Api;

public static class Limits
{
    public const int MaxFields = 2000;
    public const int MaxMappings = 500;
    public const int MaxBodyBytes = 1_000_000;
}

public static class Guards
{
    public static IResult? Payloads(JsonNode? legacy, JsonNode? modern)
    {
        string? Problem(JsonNode? n, string name)
        {
            if (n is not JsonObject o) return $"{name} must be a JSON object.";
            var count = JsonPaths.Flatten(o).Count;
            if (count == 0) return $"{name} has no fields.";
            if (count > Limits.MaxFields) return $"{name} has more than {Limits.MaxFields} fields.";
            return null;
        }
        var errors = new Dictionary<string, string[]>();
        var l = Problem(legacy, "legacy"); if (l is not null) errors["legacy"] = new[] { l };
        var m = Problem(modern, "modern"); if (m is not null) errors["modern"] = new[] { m };
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    /// <summary>Mappings are untrusted input: they end up inside generated source code, so they are strictly checked.</summary>
    public static IResult? Mappings(List<FieldMapping>? mappings)
    {
        if (mappings is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["mappings"] = new[] { "mappings is required." } });
        string? Err()
        {
            if (mappings.Count > Limits.MaxMappings) return $"At most {Limits.MaxMappings} mappings are allowed.";
            if (mappings.Select(m => m.LegacyPath).Distinct().Count() != mappings.Count) return "Each legacy field may be mapped only once.";
            foreach (var m in mappings)
            {
                if (!ValidPath(m.LegacyPath)) return $"Invalid legacy path '{Trim(m.LegacyPath)}'.";
                if (m.ModernPath is not null && !ValidPath(m.ModernPath)) return $"Invalid modern path '{Trim(m.ModernPath)}'.";
                if (!Transforms.All.Contains(m.Transform)) return $"Unsupported transformation '{Trim(m.Transform)}'. Supported: {string.Join(", ", Transforms.All)}.";
            }
            return null;
        }
        var e = Err();
        return e is null ? null : Results.ValidationProblem(new Dictionary<string, string[]> { ["mappings"] = new[] { e } });
    }

    public static bool ValidPath(string? p) =>
        !string.IsNullOrEmpty(p) && p.Length <= 200 && !p.Any(char.IsControl) && p.Split('.').All(seg => seg.Length > 0);

    public static bool ValidNamespace(string? ns) => ns is not null && Regex.IsMatch(ns, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") && ns.Length <= 100;

    private static string Trim(string? s) => s is null ? "" : (s.Length > 40 ? s[..40] + "..." : s);
}

public static class CompareService
{
    private static string Parent(string p) => p.Contains('.') ? p[..p.LastIndexOf('.')] : "";

    public static CompareResponse Compare(JsonNode legacy, JsonNode modern)
    {
        var ll = JsonPaths.Flatten(legacy);
        var ml = JsonPaths.Flatten(modern);
        var diff = SchemaGhost.Comparer.Compare(ll, ml);
        var changes = new List<ChangeDto>();

        var guesses = Heuristic.Propose(legacy, modern)
            .Where(g => g.ModernPath is not null && g.Confidence >= 0.5)
            .ToDictionary(g => g.LegacyPath);
        var addedPaths = diff.Added.Select(a => a.Path).ToHashSet();
        var consumed = new HashSet<string>();

        foreach (var r in diff.Removed)
        {
            if (guesses.TryGetValue(r.Path, out var g) && addedPaths.Contains(g.ModernPath!))
            {
                var mt = diff.Added.First(a => a.Path == g.ModernPath).Type;
                consumed.Add(g.ModernPath!);
                var moved = Parent(r.Path) != Parent(g.ModernPath!);
                changes.Add(new("renamedGuess", r.Path, g.ModernPath, r.Type, mt, true, true, g.Confidence,
                    (moved ? "Possibly moved/nested. " : "Possibly renamed. ") + "Inferred from matching values and names, not confirmed."));
            }
            else
                changes.Add(new("removed", r.Path, null, r.Type, null, true, false, null, "Field no longer exists in the new response."));
        }
        foreach (var (o, n) in diff.TypeChanged)
            changes.Add(new("typeChanged", o.Path, n.Path, o.Type, n.Type, true, false, null, $"Type changed from {o.Type} to {n.Type}."));
        foreach (var a in diff.Added.Where(a => !consumed.Contains(a.Path)))
            changes.Add(new("added", null, a.Path, null, a.Type, false, false, null, "New field. Not breaking for the legacy app."));

        return new CompareResponse(changes, ll, ml, diff.Unchanged);
    }
}

public class SuggestionService(AiGateway ai)
{
    public async Task<SuggestResponse> SuggestAsync(JsonNode legacy, JsonNode modern, string mode, CancellationToken ct)
    {
        var ll = JsonPaths.Flatten(legacy).ToDictionary(l => l.Path);
        var ml = JsonPaths.Flatten(modern).ToDictionary(l => l.Path);
        var heur = Engine.Sanitize(Heuristic.Propose(legacy, modern), legacy, modern)
            .Select(m => m with { Source = "heuristic" }).ToList();

        var aiMap = new Dictionary<string, FieldMapping>();
        var status = new AiStatus(mode != "heuristic", false, ai.Provider, ai.Model, null);

        if (mode != "heuristic")
        {
            if (!ai.Enabled)
                status = status with { Message = "No AI provider is configured on this server. Showing heuristic results only." };
            else
            {
                try
                {
                    var raw = await ai.ProposeAsync(legacy, modern, ct);
                    aiMap = Engine.Sanitize(raw, legacy, modern)
                        .Where(m => m.ModernPath is not null)
                        .ToDictionary(m => m.LegacyPath, m => m with { Source = "qwen3", Confidence = Math.Min(m.Confidence, 0.85) });
                    status = status with { Used = true };
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    status = status with { Message = $"AI unavailable ({Describe(ex)}). Showing heuristic results only." };
                }
            }
        }

        var result = new List<SuggestionDto>();
        foreach (var h in heur)
        {
            aiMap.TryGetValue(h.LegacyPath, out var a);
            var chosen = h;
            string? altPath = null, altReason = null;

            if (mode == "qwen3" && status.Used)
                chosen = a ?? new FieldMapping { LegacyPath = h.LegacyPath, Source = "qwen3", Reason = "the model found no safe match" };
            else
            {
                var strong = h.ModernPath is not null && h.Confidence >= 0.7;
                if (strong) { if (a is not null && a.ModernPath != h.ModernPath) { altPath = a.ModernPath; altReason = a.Reason; } }
                else if (a is not null) chosen = a;
            }

            var transform = chosen.Transform;
            if (chosen.ModernPath is not null && transform == "none"
                && ll.TryGetValue(chosen.LegacyPath, out var lf) && ml.TryGetValue(chosen.ModernPath, out var mf)
                && lf.Type != mf.Type && lf.Type != "null")
                transform = Transforms.Infer(lf.Type, mf.Type);

            var needsReview = chosen.ModernPath is null || chosen.Source == "qwen3" || chosen.Confidence < 0.9;
            result.Add(new SuggestionDto(chosen.LegacyPath, chosen.ModernPath, transform, chosen.Confidence,
                chosen.Source, chosen.Reason, needsReview, altPath, altReason));
        }
        return new SuggestResponse(result, status);
    }

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => "request timed out",
        InvalidModelResponseException e => "the model returned an unusable response: " + e.Message,
        HttpRequestException => "could not reach the provider",
        _ => "unexpected error"
    };
}

public static class Safety
{
    public static List<WarningDto> Analyze(List<FieldMapping> mappings, JsonNode legacy, JsonNode modern)
    {
        var w = new List<WarningDto>();
        foreach (var m in mappings.Where(m => m.ModernPath is not null))
        {
            JsonPaths.TryGet(modern, m.ModernPath!, out var src);
            var s = JsonPaths.Canon(src);
            var isNum = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d);

            if (m.Transform == "toBool" && !bool.TryParse(s, out _))
                w.Add(new(m.LegacyPath, "error", $"'{s}' cannot be safely converted to a boolean. Only the strings \"true\" and \"false\" are supported; inferring a meaning for other values is unsafe."));
            if (m.Transform == "toInt" && !isNum)
                w.Add(new(m.LegacyPath, "error", $"'{s}' is not numeric, so it cannot be converted to an integer."));
            if (m.Transform == "toInt" && isNum && d != Math.Floor(d))
                w.Add(new(m.LegacyPath, "error", $"Converting {s} to an integer would silently lose its fractional part."));
            if (m.Transform == "toDouble" && !isNum)
                w.Add(new(m.LegacyPath, "error", $"'{s}' is not numeric, so it cannot be converted to a number."));
            if (m.Confidence < 0.7 && m.Confidence > 0)
                w.Add(new(m.LegacyPath, "warning", "Low confidence. Check that this field really carries the same meaning."));
            if (m.Source == "qwen3")
                w.Add(new(m.LegacyPath, "info", "Suggested by an AI model. The model's confidence is not proof; verify the meaning yourself."));
        }
        foreach (var g in mappings.Where(m => m.ModernPath is not null).GroupBy(m => m.ModernPath!).Where(g => g.Count() > 1))
            w.Add(new(null, "warning", $"Several legacy fields ({string.Join(", ", g.Select(x => x.LegacyPath))}) map to the same source '{g.Key}'. This is usually ambiguous."));

        var mapped = mappings.Where(m => m.ModernPath is not null).Select(m => m.LegacyPath).ToHashSet();
        foreach (var leaf in JsonPaths.Flatten(legacy).Where(l => !mapped.Contains(l.Path)))
            w.Add(new(leaf.Path, "warning", "The legacy application expects this field, but no mapping was approved. It will be missing."));
        return w;
    }
}

public static class ValidationService
{
    public static ValidateResponse Run(List<FieldMapping> mappings, JsonNode legacy, JsonNode modern)
    {
        var profile = new MappingProfile { Mappings = mappings };
        var v = Validator.Validate(profile, legacy, modern);
        var warnings = Safety.Analyze(mappings, legacy, modern);
        var unmapped = v.Checks.Count(c => c.Status == "skip");
        var safe = v.Failed == 0 && !warnings.Any(x => x.Severity == "error");
        return new ValidateResponse(v.Checks, warnings, v.Output, v.Passed, v.Failed, unmapped, Math.Round(v.Coverage, 4), safe);
    }
}

public static class Artifacts
{
    public static GenerateResponse Build(List<FieldMapping> mappings, JsonObject adapted, string ns)
    {
        var profile = new MappingProfile { Mappings = mappings.Where(m => m.ModernPath is not null).ToList() };
        return new GenerateResponse(
            AdapterGenerator.Generate(profile, ns),
            JsonSerializer.Serialize(profile, Json.Options),
            adapted.ToJsonString(Json.Options));
    }

    public static byte[] Zip(GenerateResponse g)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Add(zip, "CompatibilityAdapter.cs", g.AdapterCs);
            Add(zip, "mapping.json", g.MappingJson);
            Add(zip, "adapted.json", g.AdaptedJson);
        }
        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
