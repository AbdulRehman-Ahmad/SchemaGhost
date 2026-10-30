using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SchemaGhost;
using SchemaGhost.Api;
using Xunit;

public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Ai:Provider", "none");
    }
}

public class FakeGateway(Func<List<FieldMapping>> result)
    : AiGateway(new AiSettings { Provider = "ollama", Model = "qwen3:0.6b" }, null!)
{
    public override Task<List<FieldMapping>> ProposeAsync(JsonNode legacy, JsonNode modern, CancellationToken ct) => Task.FromResult(result());
}

public class ApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _c;
    public ApiTests(ApiFactory f) => _c = f.CreateClient();

    private const string Old = """{ "user_id": 1024, "name": "Ali", "email": "ali@example.com", "is_active": true, "balance": 250.75 }""";
    private const string New = """{ "account": { "id": 1024, "status": "active" }, "full_name": "Ali", "contact": { "email_address": "ali@example.com" }, "isActive": "true", "wallet": { "balance": 250.75 } }""";

    private static JsonNode J(string s) => JsonNode.Parse(s)!;
    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    private static object Req(string legacy = Old, string modern = New, object? mappings = null, string? mode = null) =>
        new { legacy = J(legacy), modern = J(modern), mappings, mode };
    private static object M(string legacy, string? modern, string transform = "none") =>
        new { legacyPath = legacy, modernPath = modern, transform, confidence = 1.0, source = "heuristic", reason = "" };

    [Fact]
    public async Task Health_IsOk() => Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync("/api/health")).StatusCode);

    [Fact]
    public async Task Compare_FindsInferredRename()
    {
        var r = await (await _c.PostAsync("/api/compare", Body(Req()))).Content.ReadFromJsonAsync<JsonNode>();
        var changes = r!["changes"]!.AsArray();
        Assert.Contains(changes, c => c!["kind"]!.GetValue<string>() == "renamedGuess" && c["legacyPath"]!.GetValue<string>() == "user_id");
        Assert.All(changes.Where(c => c!["kind"]!.GetValue<string>() == "renamedGuess"), c => Assert.True(c!["inferred"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task Compare_RejectsNonObject()
    {
        var res = await _c.PostAsync("/api/compare", Body(new { legacy = J("[1,2]"), modern = J(New) }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task InvalidJsonBody_Returns400()
    {
        var res = await _c.PostAsync("/api/compare", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Suggest_Heuristic_MapsUserId()
    {
        var r = await (await _c.PostAsync("/api/suggest", Body(Req(mode: "heuristic")))).Content.ReadFromJsonAsync<JsonNode>();
        var s = r!["suggestions"]!.AsArray().First(x => x!["legacyPath"]!.GetValue<string>() == "user_id")!;
        Assert.Equal("account.id", s["modernPath"]!.GetValue<string>());
        Assert.False(r["ai"]!["used"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Suggest_Hybrid_WithoutAi_FallsBackWithMessage()
    {
        var r = await (await _c.PostAsync("/api/suggest", Body(Req(mode: "hybrid")))).Content.ReadFromJsonAsync<JsonNode>();
        Assert.False(r!["ai"]!["used"]!.GetValue<bool>());
        Assert.Contains("heuristic", r["ai"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Validate_RejectsUnsafeBooleanGuess()
    {
        var maps = new[] { M("is_active", "account.status", "toBool") };
        var r = await (await _c.PostAsync("/api/validate", Body(Req(mappings: maps)))).Content.ReadFromJsonAsync<JsonNode>();
        Assert.False(r!["safe"]!.GetValue<bool>());
        Assert.True(r["failed"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public async Task Validate_FlagsLossyNumericConversion()
    {
        var maps = new[] { M("balance", "wallet.balance", "toInt") };
        var r = await (await _c.PostAsync("/api/validate", Body(Req(mappings: maps)))).Content.ReadFromJsonAsync<JsonNode>();
        Assert.False(r!["safe"]!.GetValue<bool>());
        Assert.Contains(r["warnings"]!.AsArray(), w => w!["message"]!.GetValue<string>().Contains("fractional"));
    }

    [Fact]
    public async Task Validate_FailsOnHallucinatedPath()
    {
        var r = await (await _c.PostAsync("/api/validate", Body(Req(mappings: new[] { M("name", "does.not.exist") })))).Content.ReadFromJsonAsync<JsonNode>();
        Assert.Equal(1, r!["failed"]!.GetValue<int>());
    }

    [Fact]
    public async Task Validate_RejectsUnsupportedTransform()
    {
        var res = await _c.PostAsync("/api/validate", Body(Req(mappings: new[] { M("name", "full_name", "eval(\"x\")") })));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Generate_ReturnsAdapter_ForSafeMappings()
    {
        var maps = new[] { M("user_id", "account.id"), M("name", "full_name") };
        var res = await _c.PostAsync("/api/generate", Body(Req("""{ "user_id": 1024, "name": "Ali" }""", New, maps)));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var r = await res.Content.ReadFromJsonAsync<JsonNode>();
        Assert.Contains("class CompatibilityAdapter", r!["adapterCs"]!.GetValue<string>());
    }

    [Fact]
    public async Task Generate_Refuses_WhenVerificationFails()
    {
        var res = await _c.PostAsync("/api/generate", Body(Req(mappings: new[] { M("is_active", "account.status", "toBool") })));
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
    }

    [Fact]
    public async Task Generate_RejectsBadNamespace()
    {
        var res = await _c.PostAsync("/api/generate", Body(new { legacy = J(Old), modern = J(New), mappings = new[] { M("name", "full_name") }, @namespace = "X; System.Diagnostics.Process.Start(\"calc\")" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Download_ReturnsZip()
    {
        var res = await _c.PostAsync("/api/download", Body(Req("""{ "name": "Ali" }""", New, new[] { M("name", "full_name") })));
        Assert.Equal("application/zip", res.Content.Headers.ContentType!.MediaType);
    }
}

public class SuggestionServiceTests
{
    private static JsonNode J(string s) => JsonNode.Parse(s)!;
    private static readonly JsonNode Legacy = J("""{ "user_id": 1024, "phone": "555" }""");
    private static readonly JsonNode Modern = J("""{ "account": { "id": 1024 }, "contact": { "email": "a@x.com" } }""");

    [Fact]
    public async Task HallucinatedAiPath_IsDiscarded()
    {
        var gw = new FakeGateway(() => new() { new FieldMapping { LegacyPath = "phone", ModernPath = "ghost.phone", Confidence = 0.99 } });
        var r = await new SuggestionService(gw).SuggestAsync(Legacy, Modern, "hybrid", CancellationToken.None);
        Assert.Null(r.Suggestions.First(s => s.LegacyPath == "phone").ModernPath);
    }

    [Fact]
    public async Task Ai_CannotOverride_StrongHeuristic()
    {
        var gw = new FakeGateway(() => new() { new FieldMapping { LegacyPath = "user_id", ModernPath = "contact.email", Confidence = 0.9 } });
        var r = await new SuggestionService(gw).SuggestAsync(Legacy, Modern, "hybrid", CancellationToken.None);
        var s = r.Suggestions.First(x => x.LegacyPath == "user_id");
        Assert.Equal("account.id", s.ModernPath);
        Assert.Equal("contact.email", s.AiAlternativePath);
    }

    [Fact]
    public async Task InvalidAiOutput_FallsBackToHeuristic()
    {
        var gw = new FakeGateway(() => throw new InvalidModelResponseException("garbage"));
        var r = await new SuggestionService(gw).SuggestAsync(Legacy, Modern, "hybrid", CancellationToken.None);
        Assert.False(r.Ai.Used);
        Assert.NotNull(r.Ai.Message);
        Assert.Equal("account.id", r.Suggestions.First(s => s.LegacyPath == "user_id").ModernPath);
    }

    [Fact]
    public async Task AiConfidence_IsCapped_AndNeedsReview()
    {
        var gw = new FakeGateway(() => new() { new FieldMapping { LegacyPath = "phone", ModernPath = "contact.email", Confidence = 1.0 } });
        var r = await new SuggestionService(gw).SuggestAsync(Legacy, Modern, "qwen3", CancellationToken.None);
        var s = r.Suggestions.First(x => x.LegacyPath == "phone");
        Assert.Equal("qwen3", s.Source);
        Assert.True(s.Confidence <= 0.85);
        Assert.True(s.NeedsReview);
    }

    [Fact]
    public void ModelOutput_StripsThinkTags_AndRejectsGarbage()
    {
        var ok = ModelOutput.Parse("<think>hmm</think>{\"mappings\":[{\"legacyPath\":\"a\",\"modernPath\":\"b\",\"transform\":\"none\",\"confidence\":0.5,\"reason\":\"x\"}]}");
        Assert.Single(ok);
        Assert.Throws<InvalidModelResponseException>(() => ModelOutput.Parse("sorry, I can't help"));
    }
}
