using System.Text.Json.Nodes;
using SchemaGhost;
using Xunit;

public class CoreTests
{
    private static JsonNode J(string s) => JsonNode.Parse(s)!;
    private const string Old = """{ "user_id": 1024, "name": "Ali" }""";
    private const string New = """{ "account": { "id": 1024 }, "full_name": "Ali" }""";

    [Fact]
    public void Flatten_ProducesDottedPaths()
    {
        var paths = JsonPaths.Flatten(J(New)).Select(l => l.Path).ToList();
        Assert.Equal(new[] { "account.id", "full_name" }, paths);
    }

    [Fact]
    public void Heuristic_MapsNestedRename()
    {
        var m = Heuristic.Propose(J(Old), J(New));
        Assert.Equal("account.id", m.First(x => x.LegacyPath == "user_id").ModernPath);
        Assert.Equal("full_name", m.First(x => x.LegacyPath == "name").ModernPath);
    }

    [Fact]
    public void Heuristic_AbstainsWhenValuesAreAmbiguous()
    {
        var m = Heuristic.Propose(J("""{ "low": 10, "high": 10 }"""), J("""{ "min": 10, "max": 10 }"""));
        Assert.All(m, x => Assert.True(x.Confidence < 0.7));
    }

    [Fact]
    public void Sanitize_RejectsHallucinatedPaths()
    {
        var raw = new List<FieldMapping> { new() { LegacyPath = "name", ModernPath = "does.not.exist", Confidence = 0.99 } };
        var result = Engine.Sanitize(raw, J(Old), J(New));
        Assert.Null(result.First(x => x.LegacyPath == "name").ModernPath);
    }

    [Fact]
    public void Engine_RebuildsLegacyShapeWithTransform()
    {
        var profile = new MappingProfile { Mappings = { new() { LegacyPath = "id", ModernPath = "id", Transform = "toInt", Confidence = 1 } } };
        var output = Engine.Apply(profile, J("""{ "id": "1024" }"""));
        Assert.Equal(1024, output["id"]!.GetValue<long>());
    }

    [Fact]
    public void Validator_FailsWhenSourceMissing()
    {
        var profile = new MappingProfile { Mappings = { new() { LegacyPath = "name", ModernPath = "nope", Confidence = 1 } } };
        var v = Validator.Validate(profile, J(Old), J(New));
        Assert.Equal(1, v.Failed);
    }

    [Fact]
    public void Validator_PassesGoodMapping()
    {
        var profile = new MappingProfile { Mappings = {
            new() { LegacyPath = "user_id", ModernPath = "account.id", Confidence = 1 },
            new() { LegacyPath = "name", ModernPath = "full_name", Confidence = 1 } } };
        var v = Validator.Validate(profile, J(Old), J(New));
        Assert.Equal(0, v.Failed);
        Assert.Equal(1.0, v.Coverage);
    }

    [Fact]
    public void AdapterGenerator_EmitsMappedPaths()
    {
        var profile = new MappingProfile { Mappings = { new() { LegacyPath = "user_id", ModernPath = "account.id", Confidence = 0.9 } } };
        var code = AdapterGenerator.Generate(profile);
        Assert.Contains("Get(modern, \"account.id\")", code);
        Assert.Contains("class CompatibilityAdapter", code);
    }
}
