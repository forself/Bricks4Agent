using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Broker.Services;

public sealed class HighLevelDocumentArtifactResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Format { get; set; } = "txt";
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool UploadedToGoogleDrive { get; set; }
    public LineArtifactDeliveryResult? Delivery { get; set; }
}

public sealed class HighLevelDocumentArtifactService
{
    private readonly HighLevelLlmOptions _highLevelLlmOptions;
    private readonly GoogleDriveShareService _googleDriveShareService;
    private readonly LineArtifactDeliveryService _artifactDeliveryService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HighLevelDocumentArtifactService> _logger;

    public HighLevelDocumentArtifactService(
        HighLevelLlmOptions highLevelLlmOptions,
        GoogleDriveShareService googleDriveShareService,
        GoogleDriveOAuthService googleDriveOAuthService,
        LineArtifactDeliveryService artifactDeliveryService,
        IHttpClientFactory httpClientFactory,
        ILogger<HighLevelDocumentArtifactService> logger)
    {
        _highLevelLlmOptions = highLevelLlmOptions;
        _googleDriveShareService = googleDriveShareService;
        _artifactDeliveryService = artifactDeliveryService;
        _httpClient = httpClientFactory.CreateClient("high-level-llm");
        _logger = logger;
    }

    public async Task<HighLevelDocumentArtifactResult> GenerateAndDeliverAsync(
        HighLevelTaskDraft draft,
        HighLevelUserProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(draft.TaskType, "doc_gen", StringComparison.OrdinalIgnoreCase))
            return Fail("draft is not a doc_gen task.");

        var format = InferFormat(draft.OriginalMessage);
        var fileName = InferFileName(draft, format);
        var content = await GenerateDocumentContentAsync(draft, format, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            content = BuildFallbackContent(draft, format);

        var identityMode = _googleDriveShareService.ResolveIdentityMode(null);
        var uploadToGoogleDrive = _googleDriveShareService.CanUpload(identityMode, draft.Channel, draft.UserId);

        var delivery = await _artifactDeliveryService.GenerateAndDeliverAsync(new LineArtifactDeliveryRequest
        {
            UserId = profile.UserId,
            FileName = fileName,
            Format = format,
            Content = content,
            UploadToGoogleDrive = uploadToGoogleDrive,
            IdentityMode = identityMode,
            FolderId = string.Empty,
            ShareMode = string.Empty,
            SendLineNotification = true,
            NotificationTitle = "檔案已完成",
            Source = "high_level_doc_gen",
            RelatedTaskType = draft.TaskType,
            RelatedDraftId = draft.DraftId
        }, cancellationToken);

        return new HighLevelDocumentArtifactResult
        {
            Success = delivery.Success,
            Message = delivery.Success
                ? (uploadToGoogleDrive ? "artifact_created_and_uploaded" : "artifact_created_locally_only")
                : delivery.Message,
            Format = format,
            FileName = fileName,
            Content = content,
            UploadedToGoogleDrive = uploadToGoogleDrive && delivery.Success,
            Delivery = delivery
        };
    }

    private async Task<string?> GenerateDocumentContentAsync(
        HighLevelTaskDraft draft,
        string format,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildPrompt(draft, format);
            return await CallLlmAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "High-level document artifact generation failed for {UserId}", draft.UserId);
            return null;
        }
    }

    private string BuildPrompt(HighLevelTaskDraft draft, string format)
    {
        var outputRule = format switch
        {
            "md" => "請輸出完整的 Markdown 文件，不要加上 code fence。",
            "json" => "請輸出完整且有效的 JSON 文件，不要加上 code fence，也不要補充說明文字。",
            "html" => "請輸出完整的 HTML 文件，不要加上 code fence。",
            "csv" => "請輸出 UTF-8 CSV 內容，不要加上 code fence。",
            _ => "請輸出可直接儲存為文字檔的完整內容，不要加上 code fence。"
        };

        return string.Join('\n', new[]
        {
            "你是一個透過 LINE 接收需求、負責生成可交付文件的高階助理。",
            $"輸出格式：{format}",
            outputRule,
            "請根據使用者要求產生可直接交付的文件內容，避免多餘解釋，保持內容完整且可直接存檔。",
            "若需求不足，請仍先產出一份合理的初稿。",
            string.Empty,
            $"title: {draft.Title}",
            $"summary: {draft.Summary}",
            "user_request:",
            draft.OriginalMessage
        });
    }

    private async Task<string?> CallLlmAsync(string prompt, CancellationToken ct)
    {
        var provider = (_highLevelLlmOptions.Provider ?? "ollama").Trim().ToLowerInvariant();
        return provider switch
        {
            "ollama" => await SendOllamaChatAsync(prompt, ct),
            _ => string.Equals(_highLevelLlmOptions.ApiFormat, "responses", StringComparison.OrdinalIgnoreCase)
                ? await SendResponsesApiAsync(prompt, ct)
                : await SendChatCompletionsAsync(prompt, ct)
        };
    }

    private async Task<string?> SendOllamaChatAsync(string prompt, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            },
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

    private async Task<string?> SendChatCompletionsAsync(string prompt, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            },
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

    private async Task<string?> SendResponsesApiAsync(string prompt, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = prompt
                        }
                    }
                }
            }
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

        if (doc.RootElement.TryGetProperty("output", out var output) &&
            output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var contentNodes) || contentNodes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in contentNodes.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textNode) &&
                        textNode.ValueKind == JsonValueKind.String)
                    {
                        return textNode.GetString()?.Trim();
                    }
                }
            }
        }

        return null;
    }

    private static string InferFormat(string message)
    {
        var lower = (message ?? string.Empty).ToLowerInvariant();
        if (lower.Contains(".md") || lower.Contains("markdown"))
            return "md";
        if (lower.Contains(".json") || lower.Contains("json"))
            return "json";
        if (lower.Contains(".html") || lower.Contains("html"))
            return "html";
        if (lower.Contains(".csv") || lower.Contains("csv"))
            return "csv";
        return "txt";
    }

    private static string InferFileName(HighLevelTaskDraft draft, string format)
    {
        var match = Regex.Match(
            draft.OriginalMessage,
            @"(?<name>[\p{L}\p{N}_\-\s]{1,80})\.(?<ext>txt|md|json|html|csv)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success)
        {
            var explicitName = $"{match.Groups["name"].Value.Trim()}.{match.Groups["ext"].Value.ToLowerInvariant()}";
            return SanitizeFileName(explicitName);
        }

        var baseName = !string.IsNullOrWhiteSpace(draft.ProjectName)
            ? draft.ProjectName!
            : $"deliverable-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        return SanitizeFileName($"{baseName}.{format}");
    }

    private static string SanitizeFileName(string value)
    {
        var fileName = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        return fileName;
    }

    private static string BuildFallbackContent(HighLevelTaskDraft draft, string format)
        => format switch
        {
            "md" => $"# {draft.Title}\n\n{draft.OriginalMessage}\n",
            "json" => JsonSerializer.Serialize(new
            {
                title = draft.Title,
                summary = draft.Summary,
                user_request = draft.OriginalMessage
            }, new JsonSerializerOptions { WriteIndented = true }),
            "html" => $"<!DOCTYPE html><html lang=\"zh-TW\"><meta charset=\"UTF-8\"><title>{System.Net.WebUtility.HtmlEncode(draft.Title)}</title><body><h1>{System.Net.WebUtility.HtmlEncode(draft.Title)}</h1><p>{System.Net.WebUtility.HtmlEncode(draft.OriginalMessage)}</p></body></html>",
            "csv" => "field,value\n" +
                     $"title,\"{draft.Title.Replace("\"", "\"\"")}\"\n" +
                     $"summary,\"{draft.Summary.Replace("\"", "\"\"")}\"\n" +
                     $"user_request,\"{draft.OriginalMessage.Replace("\"", "\"\"")}\"\n",
            _ => $"{draft.Title}{Environment.NewLine}{Environment.NewLine}{draft.OriginalMessage}{Environment.NewLine}"
        };

    private static HighLevelDocumentArtifactResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}
