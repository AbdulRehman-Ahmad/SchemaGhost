using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace SchemaGhost;

public class Options
{
    private readonly Dictionary<string, string> _v = new();
    private readonly HashSet<string> _flags = new();

    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) continue;
            var key = a[i][2..];
            if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) o._v[key] = a[++i];
            else o._flags.Add(key);
        }
        return o;
    }

    public string? Get(string k) => _v.TryGetValue(k, out var v) ? v : null;
    public string Get(string k, string def) => Get(k) ?? def;
    public bool Has(string k) => _flags.Contains(k);
    public string Require(string k) => Get(k) ?? throw new ArgumentException($"Missing required option --{k}");
}

public static class Commands
{
    private static JsonNode Load(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException($"{path} is empty or not valid JSON.");

    private static string DefaultModel(Options o) =>
        o.Get("model") ?? Environment.GetEnvironmentVariable("SCHEMAGHOST_MODEL") ?? "qwen3:0.6b";

    public static async Task<int> MapAsync(Options o)
    {
        var engine = o.Get("engine", "ollama");
        var model = DefaultModel(o);
        var threshold = double.Parse(o.Get("threshold", "0.7"), CultureInfo.InvariantCulture);
        var outDir = o.Get("out", "ghost-out");

        Ui.Banner();
        var legacy = Load(o.Require("legacy"));
        var modern = Load(o.Require("modern"));

        Ui.Section("1 · Structural comparison");
        Ui.DiffTable(Comparer.Compare(JsonPaths.Flatten(legacy), JsonPaths.Flatten(modern)));

        Ui.Section(engine == "heuristic" ? "2 · Mapping (offline heuristic)" : $"2 · Semantic mapping ({model} via Ollama)");
        List<FieldMapping> raw;
        if (engine == "heuristic") raw = Heuristic.Propose(legacy, modern);
        else
        {
            try
            {
                raw = await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync($"Asking {Markup.Escape(model)}...", _ => new OllamaMapper(o.Get("ollama-url", "http://localhost:11434"), model).ProposeAsync(legacy, modern));
            }
            catch (HttpRequestException ex)
            {
                AnsiConsole.MarkupLine($"[red]Could not reach Ollama:[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine($"[grey]Start it and pull the model (ollama pull {Markup.Escape(model)}), or run with --engine heuristic.[/]");
                return 2;
            }
        }
        var proposals = Engine.Sanitize(raw, legacy, modern);
        Ui.MappingTable(proposals);

        Ui.Section("3 · Developer review");
        var approved = new List<FieldMapping>();
        foreach (var m in proposals.Where(p => p.ModernPath is not null))
        {
            var take = o.Has("yes")
                ? m.Confidence >= threshold
                : AnsiConsole.Confirm($"Approve [bold]{Markup.Escape(m.LegacyPath)}[/] ← [bold]{Markup.Escape(m.ModernPath!)}[/] ({m.Confidence:P0})?", m.Confidence >= threshold);
            if (take) approved.Add(m);
        }
        var profile = new MappingProfile { Mappings = approved };

        Ui.Section("4 · Verification");
        var validation = Validator.Validate(profile, legacy, modern);
        Ui.ValidationTable(validation);

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "mapping.json"), JsonSerializer.Serialize(profile, Json.Options));
        File.WriteAllText(Path.Combine(outDir, "CompatibilityAdapter.cs"), AdapterGenerator.Generate(profile));
        File.WriteAllText(Path.Combine(outDir, "adapted.json"), validation.Output.ToJsonString(Json.Options));
        Ui.Summary(validation, outDir);
        return validation.Failed > 0 ? 3 : 0;
    }

    public static int Verify(Options o)
    {
        Ui.Banner();
        var profile = JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(o.Require("mapping")), Json.Options)!;
        var v = Validator.Validate(profile, Load(o.Require("legacy")), Load(o.Require("modern")));
        Ui.ValidationTable(v);
        Ui.Summary(v, "");
        return v.Failed > 0 ? 3 : 0;
    }

    public static int Apply(Options o)
    {
        var profile = JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(o.Require("mapping")), Json.Options)!;
        Console.WriteLine(Engine.Apply(profile, Load(o.Require("input"))).ToJsonString(Json.Options));
        return 0;
    }

    public static async Task<int> BenchAsync(Options o)
    {
        Ui.Banner();
        var cases = JsonSerializer.Deserialize<List<BenchCase>>(File.ReadAllText(o.Get("cases", "benchmark/cases.json")), Json.Options)!;
        var threshold = double.Parse(o.Get("threshold", "0.7"), CultureInfo.InvariantCulture);
        var withOllama = o.Has("with-ollama");
        var mapper = new OllamaMapper(o.Get("ollama-url", "http://localhost:11434"), DefaultModel(o));

        var t = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        t.AddColumn("Case"); t.AddColumn("Fields"); t.AddColumn("Heuristic (ok/wrong/abstain)");
        if (withOllama) t.AddColumn($"{Markup.Escape(DefaultModel(o))} (ok/wrong/abstain)");

        var totals = new List<BenchResult>(); var totalsLlm = new List<BenchResult>();
        foreach (var c in cases)
        {
            var h = Bench.Score(c, Engine.Sanitize(Heuristic.Propose(c.Legacy!, c.Modern!), c.Legacy!, c.Modern!), threshold);
            totals.Add(h);
            var row = new List<string> { Markup.Escape(c.Name), h.Total.ToString(), Cell(h) };
            if (withOllama)
            {
                try
                {
                    var raw = await mapper.ProposeAsync(c.Legacy!, c.Modern!);
                    var r = Bench.Score(c, Engine.Sanitize(raw, c.Legacy!, c.Modern!), threshold);
                    totalsLlm.Add(r); row.Add(Cell(r));
                }
                catch (Exception ex) { row.Add("[red]error: " + Markup.Escape(ex.Message) + "[/]"); }
            }
            t.AddRow(row.ToArray());
        }
        AnsiConsole.Write(t);
        Report("Heuristic baseline", totals);
        if (withOllama) Report($"Local model ({DefaultModel(o)})", totalsLlm);
        AnsiConsole.MarkupLine("[grey]ok = correct   wrong = confident but incorrect (unsafe)   abstain = declined[/]");
        return 0;
    }

    private static string Cell(BenchResult r) => $"[green]{r.Correct}[/] / [red]{r.Wrong}[/] / [grey]{r.Abstained}[/]";

    private static void Report(string name, List<BenchResult> rs)
    {
        var total = rs.Sum(r => r.Total);
        if (total == 0) return;
        double P(int n) => (double)n / total;
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(name)}[/]: accuracy [green]{P(rs.Sum(r => r.Correct)):P0}[/], unsafe [red]{P(rs.Sum(r => r.Wrong)):P0}[/], abstained {P(rs.Sum(r => r.Abstained)):P0}  [grey]({total} fields)[/]");
    }
}
