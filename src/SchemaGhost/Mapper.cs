using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SchemaGhost;

/// <summary>Offline baseline: matches by equal values first, then by name similarity. No AI involved.</summary>
public static class Heuristic
{
    public static List<FieldMapping> Propose(JsonNode legacy, JsonNode modern)
    {
        var modernLeaves = JsonPaths.Flatten(modern);
        var result = new List<FieldMapping>();

        foreach (var l in JsonPaths.Flatten(legacy))
        {
            var lv = JsonPaths.Get(legacy, l.Path);
            var lCanon = JsonPaths.Canon(lv);
            var lName = Last(l.Path);
            var scored = modernLeaves.Select(m => new
            {
                M = m,
                ValueEqual = JsonPaths.Canon(JsonPaths.Get(modern, m.Path)) == lCanon,
                Sim = Dice(lName, Last(m.Path))
            }).ToList();

            var byValue = scored.Where(x => x.ValueEqual).OrderByDescending(x => x.Sim).ToList();
            var weak = l.Type is "null" or "boolean" || lCanon.Length == 0;

            if (byValue.Count > 0)
            {
                var best = byValue[0];
                var tie = byValue.Count > 1 && byValue[1].Sim >= best.Sim - 0.05;
                var conf = byValue.Count == 1 ? 0.85 + 0.15 * best.Sim : 0.5 + 0.4 * best.Sim;
                if (tie) conf = Math.Min(conf, 0.4);
                if (weak) conf = Math.Min(conf, 0.5);
                var why = tie ? "several fields hold the same value; cannot tell which" : "same value, name similarity " + best.Sim.ToString("0.00");
                result.Add(new FieldMapping
                {
                    LegacyPath = l.Path, ModernPath = best.M.Path,
                    Transform = Transforms.Infer(l.Type, best.M.Type),
                    Confidence = Math.Round(conf, 2), Reason = why
                });
                continue;
            }

            var named = scored.Where(x => x.M.Type == l.Type).OrderByDescending(x => x.Sim).FirstOrDefault();
            if (named is not null && named.Sim >= 0.8)
                result.Add(new FieldMapping { LegacyPath = l.Path, ModernPath = named.M.Path, Confidence = Math.Round(0.4 + 0.4 * named.Sim, 2), Reason = "similar name, value differs" });
            else
                result.Add(new FieldMapping { LegacyPath = l.Path, ModernPath = null, Confidence = 0, Reason = "no safe match found" });
        }
        return result;
    }

    private static string Last(string path) => path[(path.LastIndexOf('.') + 1)..];
    private static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static double Dice(string a, string b)
    {
        a = Norm(a); b = Norm(b);
        if (a == b) return 1;
        if (a.Length < 2 || b.Length < 2) return 0;
        var ba = Bigrams(a); var bb = Bigrams(b);
        var sim = 2.0 * ba.Intersect(bb).Count() / (ba.Count + bb.Count);
        if (a.Contains(b) || b.Contains(a)) sim = Math.Max(sim, 0.8);
        return sim;
    }

    private static HashSet<string> Bigrams(string s)
    {
        var set = new HashSet<string>();
        for (var i = 0; i < s.Length - 1; i++) set.Add(s.Substring(i, 2));
        return set;
    }
}

/// <summary>Asks a local Ollama model for mappings. Output is forced into a JSON schema, never code.</summary>
public class OllamaMapper(string baseUrl, string model)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private const string SchemaJson = """
    {
      "type": "object",
      "properties": {
        "mappings": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "legacyPath": { "type": "string" },
              "modernPath": { "type": ["string", "null"] },
              "transform": { "type": "string", "enum": ["none", "toString", "toInt", "toDouble", "toBool"] },
              "confidence": { "type": "number" },
              "reason": { "type": "string" }
            },
            "required": ["legacyPath", "modernPath", "transform", "confidence", "reason"]
          }
        }
      },
      "required": ["mappings"]
    }
    """;

    public async Task<List<FieldMapping>> ProposeAsync(JsonNode legacy, JsonNode modern, CancellationToken ct = default)
    {
        var (system, user) = MappingPrompts.Build(legacy, modern);

        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["think"] = false,
            ["format"] = JsonNode.Parse(SchemaJson),
            ["options"] = new JsonObject { ["temperature"] = 0, ["num_ctx"] = 4096 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user })
        };

        using var resp = await Http.PostAsync($"{baseUrl.TrimEnd('/')}/api/chat",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");

        var doc = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        var content = doc?["message"]?["content"]?.GetValue<string>()
                      ?? throw new InvalidOperationException("Empty response from Ollama.");
        return JsonNode.Parse(content)?["mappings"]?.Deserialize<List<FieldMapping>>(Json.Options) ?? new();
    }
}

public static class MappingPrompts
{
    public static (string System, string User) Build(JsonNode legacy, JsonNode modern)
    {
        static string Fmt(IEnumerable<Leaf> ls) => string.Join("\n", ls.Select(l => $"- {l.Path} ({l.Type}) e.g. {l.Sample}"));
        var system = "You migrate API integrations. Given the fields an old application expects (LEGACY) and the fields " +
                     "the API returns now (MODERN), decide for each LEGACY field which MODERN field carries the same information. " +
                     "Rules: use ONLY paths that appear in the lists. If no MODERN field safely matches, set modernPath to null and confidence to 0. " +
                     "Never invent data. confidence is 0 to 1 and must reflect real uncertainty. Use transform only when types differ.";
        var user = $"LEGACY fields:\n{Fmt(JsonPaths.Flatten(legacy))}\n\nMODERN fields:\n{Fmt(JsonPaths.Flatten(modern))}\n\n" +
                   "Return one entry per LEGACY field.";
        return (system, user);
    }
}
