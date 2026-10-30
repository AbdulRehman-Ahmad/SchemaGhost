using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SchemaGhost.Api;

public class AiGateway(AiSettings s, IHttpClientFactory http)
{
    public bool Enabled => s.Provider is "ollama" or "openai";
    public string Provider => s.Provider;
    public string Model => s.Model;
    public bool SendsDataToThirdParty => s.Provider == "openai";

    public virtual async Task<List<FieldMapping>> ProposeAsync(JsonNode legacy, JsonNode modern, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(s.TimeoutSeconds));
        return s.Provider == "ollama"
            ? await new OllamaMapper(string.IsNullOrWhiteSpace(s.BaseUrl) ? "http://localhost:11434" : s.BaseUrl, s.Model)
                .ProposeAsync(legacy, modern, cts.Token)
            : await OpenAiCompatibleAsync(legacy, modern, cts.Token);
    }

    private async Task<List<FieldMapping>> OpenAiCompatibleAsync(JsonNode legacy, JsonNode modern, CancellationToken ct)
    {
        var (system, user) = MappingPrompts.Build(legacy, modern);
        user += "\n\nRespond with JSON only, exactly in this shape: " +
                "{\"mappings\":[{\"legacyPath\":\"...\",\"modernPath\":\"... or null\",\"transform\":\"none|toString|toInt|toDouble|toBool\",\"confidence\":0.0,\"reason\":\"...\"}]}";
        var client = http.CreateClient("ai");

        foreach (var useFormat in new[] { true, false })
        {
            var body = new JsonObject
            {
                ["model"] = s.Model,
                ["temperature"] = 0,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = user })
            };
            if (useFormat) body["response_format"] = new JsonObject { ["type"] = "json_object" };

            using var req = new HttpRequestMessage(HttpMethod.Post, s.BaseUrl.TrimEnd('/') + "/chat/completions")
            { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            if (!string.IsNullOrEmpty(s.ApiKey)) req.Headers.Authorization = new("Bearer", s.ApiKey);

            using var resp = await client.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.BadRequest && useFormat) continue;   // provider may not support response_format
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"provider returned HTTP {(int)resp.StatusCode}");

            var doc = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            var content = doc?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                          ?? throw new InvalidModelResponseException("empty response");
            return ModelOutput.Parse(content);
        }
        throw new InvalidModelResponseException("provider rejected the request");
    }
}

public static class ModelOutput
{
    public static List<FieldMapping> Parse(string content)
    {
        content = Regex.Replace(content, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidModelResponseException("no JSON object in response");
        try
        {
            var node = JsonNode.Parse(content[start..(end + 1)]);
            return node?["mappings"]?.Deserialize<List<FieldMapping>>(Json.Options)
                   ?? throw new InvalidModelResponseException("missing 'mappings' array");
        }
        catch (JsonException)
        {
            throw new InvalidModelResponseException("invalid JSON");
        }
    }
}
