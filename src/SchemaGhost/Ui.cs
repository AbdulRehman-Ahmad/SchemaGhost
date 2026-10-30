using Spectre.Console;

namespace SchemaGhost;

public static class Ui
{
    private static string E(string? s) => Markup.Escape(s ?? "");

    public static void Banner()
    {
        AnsiConsole.Write(new FigletText("SchemaGhost").Color(Color.MediumPurple1));
        AnsiConsole.MarkupLine("[grey]AI-assisted API migration for forgotten & legacy apps  ·  local-first  ·  verified, never blind[/]");
        AnsiConsole.WriteLine();
    }

    public static void Section(string title) => AnsiConsole.Write(new Rule($"[bold mediumpurple1]{E(title)}[/]").LeftJustified().RuleStyle("grey"));

    public static void Help()
    {
        var t = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        t.AddColumn("Command"); t.AddColumn("What it does");
        t.AddRow("[bold]map[/] --legacy old.json --modern new.json", "Compare, propose mappings, approve, verify, emit adapter");
        t.AddRow("   [grey]--engine ollama|heuristic  --model <tag>  --out <dir>  --yes  --threshold 0.7[/]", "");
        t.AddRow("[bold]verify[/] --mapping m.json --legacy old.json --modern new.json", "Re-check an existing mapping profile");
        t.AddRow("[bold]apply[/] --mapping m.json --input new.json", "Transform a modern payload into the legacy shape");
        t.AddRow("[bold]bench[/] --cases benchmark/cases.json [[--with-ollama]]", "Score the mapper against known schema changes");
        AnsiConsole.Write(t);
    }

    public static void DiffTable(SchemaDiff d)
    {
        var t = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        t.AddColumn("Change"); t.AddColumn("Path"); t.AddColumn("Detail");
        foreach (var l in d.Removed) t.AddRow("[red]− missing[/]", E(l.Path), $"[grey]{E(l.Type)} {E(l.Sample)}[/]");
        foreach (var a in d.Added) t.AddRow("[green]+ new[/]", E(a.Path), $"[grey]{E(a.Type)} {E(a.Sample)}[/]");
        foreach (var (o, n) in d.TypeChanged) t.AddRow("[yellow]~ type[/]", E(o.Path), $"{E(o.Type)} → {E(n.Type)}");
        AnsiConsole.Write(t);
        AnsiConsole.MarkupLine($"[grey]{d.Unchanged} field(s) unchanged, {d.Removed.Count} missing, {d.Added.Count} new, {d.TypeChanged.Count} type change(s)[/]");
        AnsiConsole.WriteLine();
    }

    private static string Bar(double c)
    {
        var n = (int)Math.Round(c * 10);
        var color = c >= 0.8 ? "green" : c >= 0.5 ? "yellow" : "red";
        return $"[{color}]{new string('█', n)}[/][grey]{new string('░', 10 - n)}[/] {c:P0}";
    }

    public static void MappingTable(List<FieldMapping> ms)
    {
        var t = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        t.AddColumn("Legacy field"); t.AddColumn("← Modern source"); t.AddColumn("Transform"); t.AddColumn("Confidence"); t.AddColumn("Why");
        foreach (var m in ms)
            t.AddRow($"[bold]{E(m.LegacyPath)}[/]",
                m.ModernPath is null ? "[red]— no safe match[/]" : E(m.ModernPath),
                m.ModernPath is null ? "" : E(m.Transform),
                m.ModernPath is null ? "[grey]n/a[/]" : Bar(m.Confidence),
                $"[grey]{E(m.Reason)}[/]");
        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
    }

    public static void ValidationTable(Validation v)
    {
        var t = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
        t.AddColumn("Field"); t.AddColumn("Result"); t.AddColumn("Detail");
        foreach (var c in v.Checks)
        {
            var s = c.Status switch { "pass" => "[green]pass[/]", "fail" => "[red]FAIL[/]", _ => "[grey]skipped[/]" };
            t.AddRow(E(c.LegacyPath), s, $"[grey]{E(c.Detail)}[/]");
        }
        AnsiConsole.Write(t);
        AnsiConsole.WriteLine();
    }

    public static void Summary(Validation v, string outDir)
    {
        var color = v.Failed > 0 ? "red" : v.Coverage >= 1 ? "green" : "yellow";
        var body = $"[bold]Coverage:[/] [{color}]{v.Coverage:P0}[/] of legacy fields restored\n" +
                   $"[bold]Passed:[/] {v.Passed}   [bold]Failed:[/] {v.Failed}   [bold]Unmapped:[/] {v.LegacyLeaves - v.Passed - v.Failed}\n" +
                   (outDir.Length > 0 ? $"[bold]Output:[/] {E(outDir)}  [grey](mapping.json, CompatibilityAdapter.cs, adapted.json)[/]" : "");
        AnsiConsole.Write(new Panel(body).Header("[bold mediumpurple1] Result [/]").BorderColor(Color.MediumPurple1).Border(BoxBorder.Rounded));
    }
}
