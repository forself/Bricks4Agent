using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Broker.Services;

public interface IHighLevelExecutionModelPlanner
{
    Task<HighLevelExecutionModelRequest?> RecommendAsync(
        HighLevelTaskDraft draft,
        HighLevelMemoryState memory,
        CancellationToken cancellationToken = default);
}

public sealed class HighLevelExecutionModelPlanner : IHighLevelExecutionModelPlanner
{
    private readonly HighLevelExecutionModelPolicyOptions _policyOptions;
    private readonly HighLevelLlmOptions _highLevelLlmOptions;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HighLevelExecutionModelPlanner> _logger;

    public HighLevelExecutionModelPlanner(
        HighLevelExecutionModelPolicyOptions policyOptions,
        HighLevelLlmOptions highLevelLlmOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<HighLevelExecutionModelPlanner> logger)
    {
        _policyOptions = policyOptions;
        _highLevelLlmOptions = highLevelLlmOptions;
        _httpClient = httpClientFactory.CreateClient("high-level-llm");
        _logger = logger;
    }

    public async Task<HighLevelExecutionModelRequest?> RecommendAsync(
        HighLevelTaskDraft draft,
        HighLevelMemoryState memory,
        CancellationToken cancellationToken = default)
    {
        if (!_policyOptions.Enabled)
            return null;

        var catalog = _policyOptions.Catalog
            .Where(entry => entry.Enabled &&
                            !string.IsNullOrWhiteSpace(entry.Alias) &&
                            !string.IsNullOrWhiteSpace(entry.Model))
            .ToArray();
        if (catalog.Length == 0)
            return null;

        try
        {
            var messages = BuildMessages(draft, memory, catalog);
            var rawReply = await CallLlmAsync(messages, cancellationToken);
            if (string.IsNullOrWhiteSpace(rawReply))
                return null;

            using var doc = JsonDocument.Parse(rawReply);
            if (!doc.RootElement.TryGetProperty("alias", out var aliasNode))
                return null;

            var alias = aliasNode.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(alias))
                return null;

            var matched = catalog.FirstOrDefault(entry =>
                string.Equals(entry.Alias, alias, StringComparison.OrdinalIgnoreCase));
            if (matched == null)
            {
                _logger.LogWarning("High-level execution model planner returned unknown alias: {Alias}", alias);
                return null;
            }

            var reason = doc.RootElement.TryGetProperty("reason", out var reasonNode)
                ? reasonNode.GetString()?.Trim()
                : null;

            return new HighLevelExecutionModelRequest
            {
                Alias = matched.Alias,
                Model = matched.Model,
                Tier = matched.Tier,
                Reason = string.IsNullOrWhiteSpace(reason)
                    ? $"requested by high-level entry model for task_type={draft.TaskType}"
                    : reason,
                RequestedBy = "high-level-entry-model",
                ValidationStatus = "validated"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "High-level execution model planner returned non-JSON output");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "High-level execution model planner failed");
            return null;
        }
    }

    private JsonArray BuildMessages(
        HighLevelTaskDraft draft,
        HighLevelMemoryState memory,
        IReadOnlyList<HighLevelExecutionModelCatalogEntry> catalog)
    {
        var catalogLines = string.Join('\n', catalog.Select(entry =>
            $"- alias={entry.Alias}; tier={entry.Tier}; model={entry.Model}; description={entry.Description}"));

        var userPrompt = string.Join('\n', new[]
        {
            "請根據下列 production draft，從允許的 execution model catalog 中選擇最適合的一個。",
            "你只能回傳 JSON，不要加上 markdown 或額外說明。",
            "JSON 格式：",
            "{\"alias\":\"<catalog alias>\",\"reason\":\"<簡短理由>\"}",
            "",
            $"task_type: {draft.TaskType}",
            $"title: {draft.Title}",
            $"summary: {draft.Summary}",
            $"goal: {memory.CurrentGoal ?? draft.OriginalMessage}",
            "",
            "允許的 catalog：",
            catalogLines
        });

        return new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] =
                    "你是入口高階模型的一部分，負責為 execution task 提出模型請求。" +
                    "你只能從 catalog 選擇 alias，不能自行創造新模型。"
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = userPrompt
            }
        };
    }

    private async Task<string?> CallLlmAsync(JsonArray messages, CancellationToken ct)
    {
        var provider = (_highLevelLlmOptions.Provider ?? "ollama").Trim().ToLowerInvariant();
        return provider switch
        {
            "ollama" => await SendOllamaChatAsync(messages, ct),
            _ => string.Equals(_highLevelLlmOptions.ApiFormat, "responses", StringComparison.OrdinalIgnoreCase)
                ? await SendResponsesApiAsync(messages, ct)
                : await SendChatCompletionsAsync(messages, ct)
        };
    }

    private async Task<string?> SendOllamaChatAsync(JsonArray messages, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = messages,
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/chat", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim();
    }

    private async Task<string?> SendChatCompletionsAsync(JsonArray messages, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = messages,
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/chat/completions", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return null;

        return choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
    }

    private async Task<string?> SendResponsesApiAsync(JsonArray messages, CancellationToken ct)
    {
        var input = new JsonArray();
        foreach (var msg in messages)
        {
            if (msg is not JsonObject obj)
                continue;

            input.Add(new JsonObject
            {
                ["role"] = obj["role"]?.GetValue<string>() ?? "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = obj["content"]?.GetValue<string>() ?? string.Empty
                    }
                }
            });
        }

        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["input"] = input,
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/responses", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString()?.Trim();
        }

        return null;
    }
}

public sealed class HighLevelExecutionModelPolicyOptions
{
    public bool Enabled { get; set; } = true;
    public List<HighLevelExecutionModelCatalogEntry> Catalog { get; set; } = [];
}

public sealed class HighLevelExecutionModelCatalogEntry
{
    public string Alias { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class HighLevelExecutionModelRequest
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;
    [JsonPropertyName("tier")]
    public string Tier { get; set; } = string.Empty;
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
    [JsonPropertyName("requested_by")]
    public string RequestedBy { get; set; } = string.Empty;
    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; set; } = "pending";
}
