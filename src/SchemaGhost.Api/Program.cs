using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using SchemaGhost;
using SchemaGhost.Api;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = Limits.MaxBodyBytes);
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("ai");
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Ai").Get<AiSettings>() ?? new AiSettings());
builder.Services.AddSingleton<AiGateway>();
builder.Services.AddSingleton<SuggestionService>();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("web", p =>
{
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("ai", opt => { opt.PermitLimit = 30; opt.Window = TimeSpan.FromMinutes(1); opt.QueueLimit = 0; });
});

var app = builder.Build();

app.UseExceptionHandler(a => a.Run(async ctx =>
{
    var err = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (err is BadHttpRequestException bad)
    {
        ctx.Response.StatusCode = bad.StatusCode;
        await ctx.Response.WriteAsJsonAsync(new { error = "Invalid request body." });
        return;
    }
    ctx.Response.StatusCode = 500;   // never echo exception details or payloads
    await ctx.Response.WriteAsJsonAsync(new { error = "Unexpected server error." });
}));
app.UseCors("web");
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment()) app.MapOpenApi();

var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

api.MapGet("/config", (AiGateway ai) => Results.Ok(new
{
    ai = new { enabled = ai.Enabled, provider = ai.Provider, model = ai.Model, sendsDataToThirdParty = ai.SendsDataToThirdParty },
    limits = new { maxBodyBytes = Limits.MaxBodyBytes, maxFields = Limits.MaxFields, maxMappings = Limits.MaxMappings },
    transforms = Transforms.All
}));

api.MapPost("/compare", (CompareRequest r) =>
    Guards.Payloads(r.Legacy, r.Modern) ?? Results.Ok(CompareService.Compare(r.Legacy!, r.Modern!)));

api.MapPost("/suggest", async (SuggestRequest r, SuggestionService svc, CancellationToken ct) =>
{
    var mode = (r.Mode ?? "hybrid").ToLowerInvariant();
    if (mode is not ("heuristic" or "hybrid" or "qwen3"))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = new[] { "mode must be heuristic, hybrid or qwen3." } });
    var bad = Guards.Payloads(r.Legacy, r.Modern);
    if (bad is not null) return bad;
    return Results.Ok(await svc.SuggestAsync(r.Legacy!, r.Modern!, mode, ct));
}).RequireRateLimiting("ai");

api.MapPost("/validate", (ValidateRequest r) =>
    Guards.Payloads(r.Legacy, r.Modern) ?? Guards.Mappings(r.Mappings)
    ?? Results.Ok(ValidationService.Run(r.Mappings!, r.Legacy!, r.Modern!)));

IResult Generate(GenerateRequest r, bool asZip)
{
    var bad = Guards.Payloads(r.Legacy, r.Modern) ?? Guards.Mappings(r.Mappings);
    if (bad is not null) return bad;
    var ns = r.Namespace ?? "Legacy.Compat";
    if (!Guards.ValidNamespace(ns))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["namespace"] = new[] { "Invalid C# namespace." } });

    var v = ValidationService.Run(r.Mappings!, r.Legacy!, r.Modern!);
    if (!v.Safe)
        return Results.Json(new { error = "Code was not generated because verification found problems. Fix or reject the failing mappings.", validation = v },
                            statusCode: StatusCodes.Status422UnprocessableEntity);

    var files = Artifacts.Build(r.Mappings!, (System.Text.Json.Nodes.JsonObject)v.Adapted, ns);
    return asZip ? Results.File(Artifacts.Zip(files), "application/zip", "schemaghost-output.zip") : Results.Ok(files);
}
api.MapPost("/generate", (GenerateRequest r) => Generate(r, false));
api.MapPost("/download", (GenerateRequest r) => Generate(r, true));

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
