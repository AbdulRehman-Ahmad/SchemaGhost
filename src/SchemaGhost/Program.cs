using SchemaGhost;
using Spectre.Console;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Ui.Banner();
    Ui.Help();
    return 0;
}

var opts = Options.Parse(args.Skip(1).ToArray());
int exit;
try
{
    exit = args[0].ToLowerInvariant() switch
    {
        "map" => await Commands.MapAsync(opts),
        "verify" => Commands.Verify(opts),
        "apply" => Commands.Apply(opts),
        "bench" => await Commands.BenchAsync(opts),
        _ => Unknown(args[0])
    };
}
catch (Exception ex) when (ex is ArgumentException or IOException or System.Text.Json.JsonException or InvalidDataException)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
    exit = 1;
}

if (opts.Has("pause"))
{
    AnsiConsole.MarkupLine("[grey]Press any key to close...[/]");
    Console.ReadKey(true);
}
return exit;

static int Unknown(string cmd)
{
    AnsiConsole.MarkupLine($"[red]Unknown command '{Markup.Escape(cmd)}'.[/] Run [bold]schemaghost help[/].");
    return 1;
}
