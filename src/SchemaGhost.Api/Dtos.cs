using System.Text.Json.Nodes;

namespace SchemaGhost.Api;

public record CompareRequest(JsonNode? Legacy, JsonNode? Modern);
public record SuggestRequest(JsonNode? Legacy, JsonNode? Modern, string? Mode);
public record ValidateRequest(JsonNode? Legacy, JsonNode? Modern, List<FieldMapping>? Mappings);
public record GenerateRequest(JsonNode? Legacy, JsonNode? Modern, List<FieldMapping>? Mappings, string? Namespace);

public record ChangeDto(string Kind, string? LegacyPath, string? ModernPath, string? LegacyType, string? ModernType,
                        bool Breaking, bool Inferred, double? Confidence, string Note);
public record CompareResponse(List<ChangeDto> Changes, List<Leaf> LegacyFields, List<Leaf> ModernFields, int Unchanged);

public record SuggestionDto(string LegacyPath, string? ModernPath, string Transform, double Confidence, string Source,
                            string Reason, bool NeedsReview, string? AiAlternativePath, string? AiAlternativeReason);
public record AiStatus(bool Requested, bool Used, string Provider, string Model, string? Message);
public record SuggestResponse(List<SuggestionDto> Suggestions, AiStatus Ai);

public record WarningDto(string? LegacyPath, string Severity, string Message);
public record ValidateResponse(List<Check> Checks, List<WarningDto> Warnings, JsonNode Adapted,
                               int Passed, int Failed, int Unmapped, double Coverage, bool Safe);
public record GenerateResponse(string AdapterCs, string MappingJson, string AdaptedJson);

public class AiSettings
{
    public string Provider { get; set; } = "none";   // none | ollama | openai (any OpenAI-compatible endpoint)
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "qwen3:0.6b";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
}

public class InvalidModelResponseException(string message) : Exception(message);
