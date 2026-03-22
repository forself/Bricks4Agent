using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// High-level conversation gateway for LINE.
///
/// This service owns:
/// - conversation history persistence
/// - optional RAG augmentation
/// - calls to the high-level LLM binding
///
/// It intentionally does not use the execution/runtime LLM binding.
/// </summary>
public class LineChatGateway
{
    private readonly LineChatGatewayOptions _options;
    private readonly HighLevelLlmOptions _highLevelLlmOptions;
    private readonly HttpClient _httpClient;
    private readonly BrokerDb _db;
    private readonly EmbeddingService _embeddingService;
    private readonly RagPipelineService _ragPipeline;
    private readonly ILogger<LineChatGateway> _logger;

    public LineChatGateway(
        LineChatGatewayOptions options,
        HighLevelLlmOptions highLevelLlmOptions,
        IHttpClientFactory httpClientFactory,
        BrokerDb db,
        EmbeddingService embeddingService,
        RagPipelineService ragPipeline,
        ILogger<LineChatGateway> logger)
    {
        _options = options;
        _highLevelLlmOptions = highLevelLlmOptions;
        _httpClient = httpClientFactory.CreateClient("high-level-llm");
        _db = db;
        _embeddingService = embeddingService;
        _ragPipeline = ragPipeline;
        _logger = logger;
    }

    public async Task<ChatResult> ChatAsync(string userId, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(message))
        {
            return new ChatResult
            {
                Reply = "使用者識別與訊息內容不可為空。",
                Error = "empty_input"
            };
        }

        try
        {
            var history = LoadConversationHistory(userId);
            _logger.LogInformation(
                "[LineChatGW] User={User} history={Count} msg={Msg}",
                userId[..Math.Min(8, userId.Length)],
                history.Count,
                Truncate(message, 50));

            string? ragContext = null;
            List<RagSnippet>? ragSnippets = null;
            if (_options.RagEnabled && ShouldUseRag(message))
            {
                (ragContext, ragSnippets) = await RetrieveRagContext(message, ct);
            }

            var messages = BuildMessages(history, message, ragContext);
            var llmReply = await CallLlmAsync(messages, ct);
            if (llmReply == null)
            {
                return new ChatResult
                {
                    Reply = "抱歉，AI 服務暫時無法回應，請稍後再試。",
                    Error = "llm_unavailable"
                };
            }

            SaveMessage(userId, "user", message);
            SaveMessage(userId, "assistant", llmReply);

            _logger.LogInformation(
                "[LineChatGW] Reply to {User}: {Reply}",
                userId[..Math.Min(8, userId.Length)],
                Truncate(llmReply, 80));

            return new ChatResult
            {
                Reply = llmReply,
                RagSnippets = ragSnippets,
                HistoryCount = history.Count + 2
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LineChatGW] Error for user {User}", userId);
            return new ChatResult
            {
                Reply = "處理 LINE 對話時發生錯誤，請稍後再試。",
                Error = ex.Message
            };
        }
    }

    public List<ConversationSummary> ListConversations()
    {
        var entries = _db.Query<SharedContextEntry>(
            @"SELECT * FROM shared_context_entries
              WHERE key LIKE 'convlog:%' AND task_id = 'global'
              ORDER BY created_at DESC");

        var result = new List<ConversationSummary>();
        foreach (var entry in entries)
        {
            var uid = entry.Key.Length > 8 ? entry.Key[8..] : entry.Key;
            var messages = ParseMessages(entry.ContentRef);
            var lastMsg = messages.LastOrDefault();
            result.Add(new ConversationSummary
            {
                UserId = uid,
                MessageCount = messages.Count,
                LastMessage = lastMsg?.Content ?? "",
                LastRole = lastMsg?.Role ?? "",
                LastTimestamp = lastMsg?.Timestamp ?? entry.CreatedAt.ToString("O")
            });
        }

        return result;
    }

    public List<ConvMessage> GetConversation(string userId)
        => LoadConversationHistory(userId);

    public void ClearConversation(string userId)
    {
        var key = $"convlog:{userId}";
        _db.Execute(
            "DELETE FROM shared_context_entries WHERE key = @key AND task_id = 'global'",
            new { key });
        _logger.LogInformation("[LineChatGW] Cleared conversation for {User}", userId);
    }

    private List<ConvMessage> LoadConversationHistory(string userId)
    {
        var key = $"convlog:{userId}";
        var entry = _db.Query<SharedContextEntry>(
            @"SELECT content_ref FROM shared_context_entries
              WHERE key = @key AND task_id = 'global'
              ORDER BY version DESC LIMIT 1",
            new { key }).FirstOrDefault();

        if (entry == null || string.IsNullOrEmpty(entry.ContentRef))
            return new List<ConvMessage>();

        var messages = ParseMessages(entry.ContentRef);
        if (messages.Count > _options.MaxConversationHistory)
            messages = messages.Skip(messages.Count - _options.MaxConversationHistory).ToList();

        return messages;
    }

    private void SaveMessage(string userId, string role, string content)
    {
        var key = $"convlog:{userId}";
        var existing = LoadConversationHistory(userId);
        existing.Add(new ConvMessage
        {
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow.ToString("O")
        });

        if (existing.Count > _options.MaxConversationHistory * 2)
            existing = existing.Skip(existing.Count - _options.MaxConversationHistory).ToList();

        var json = JsonSerializer.Serialize(existing);
        var existingEntry = _db.Query<SharedContextEntry>(
            "SELECT entry_id FROM shared_context_entries WHERE key = @key AND task_id = 'global' LIMIT 1",
            new { key }).FirstOrDefault();

        if (existingEntry != null)
        {
            _db.Execute(
                @"UPDATE shared_context_entries
                  SET content_ref = @json, version = version + 1
                  WHERE key = @key AND task_id = 'global'",
                new { json, key });
        }
        else
        {
            var entry = new SharedContextEntry
            {
                EntryId = $"ctx_{Guid.NewGuid():N}"[..24],
                TaskId = "global",
                DocumentId = $"convlog:{userId}",
                Key = key,
                ContentRef = json,
                ContentType = "application/json",
                AuthorPrincipalId = "system:line-gateway",
                Acl = """{"read":["*"],"write":["system:line-gateway"]}""",
                Version = 1,
                CreatedAt = DateTime.UtcNow
            };
            _db.Insert(entry);
        }
    }

    private async Task<(string? context, List<RagSnippet>? snippets)> RetrieveRagContext(
        string query,
        CancellationToken ct)
    {
        try
        {
            const string taskId = "global";
            const int k = 60;
            var topK = _options.RagTopK;

            var rewriteResult = await _ragPipeline.RewriteQueryAsync(query);
            var searchTerms = rewriteResult.ExpandedTerms;

            var bm25Results = new Dictionary<string, (float score, string content)>();
            foreach (var term in searchTerms)
            {
                try
                {
                    var ftsQuery = Adapters.InProcessDispatcher.PrepareFts5Query(term);
                    var fts = _db.Query<RagFtsResult>(
                        "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @lim",
                        new { q = ftsQuery, taskId, lim = topK * 3 });
                    var rank = 1;
                    foreach (var row in fts)
                    {
                        var key = row.SourceKey ?? "";
                        if (!bm25Results.ContainsKey(key))
                        {
                            bm25Results[key] = (1f / (k + rank), row.Content ?? "");
                        }
                        else
                        {
                            var old = bm25Results[key];
                            bm25Results[key] = (old.score + 1f / (k + rank), old.content);
                        }

                        rank++;
                    }
                }
                catch
                {
                }
            }

            var vecResults = new Dictionary<string, float>();
            if (_embeddingService.IsEnabled)
            {
                var queryVec = await _ragPipeline.GetCachedEmbeddingAsync(query, _embeddingService);
                if (queryVec != null)
                {
                    var vectors = _db.GetAll<VectorEntry>()
                        .Where(v => v.TaskId == taskId && v.Embedding.Length > 0)
                        .ToList();
                    var rank = 1;
                    foreach (var v in vectors
                                 .Select(v => (v.SourceKey, Sim: EmbeddingService.CosineSimilarity(queryVec, EmbeddingService.BytesToVector(v.Embedding))))
                                 .Where(x => x.Sim >= 0.2f)
                                 .OrderByDescending(x => x.Sim)
                                 .Take(topK * 3))
                    {
                        vecResults[v.SourceKey] = 1f / (k + rank);
                        rank++;
                    }
                }
            }

            var allKeys = bm25Results.Keys.Union(vecResults.Keys).Distinct();
            var fused = allKeys
                .Select(key => new
                {
                    key,
                    content = bm25Results.TryGetValue(key, out var b) ? b.content : "",
                    score = (bm25Results.TryGetValue(key, out var bs) ? bs.score : 0f) + vecResults.GetValueOrDefault(key, 0f)
                })
                .OrderByDescending(x => x.score)
                .Take(topK)
                .ToList();

            if (fused.Count == 0)
                return (null, null);

            var snippets = new List<RagSnippet>();
            var sb = new StringBuilder();
            sb.AppendLine("以下是可供回答時參考的資料片段：");
            sb.AppendLine();

            foreach (var item in fused)
            {
                var content = item.content;
                if (string.IsNullOrEmpty(content))
                {
                    content = _db.GetAll<SharedContextEntry>()
                        .Where(e => e.TaskId == taskId && e.Key == item.key)
                        .OrderByDescending(e => e.Version)
                        .FirstOrDefault()?.ContentRef ?? "";
                }

                if (string.IsNullOrEmpty(content))
                    continue;

                var display = content.Length > 500 ? content[..500] + "..." : content;
                sb.AppendLine($"[{item.key}]");
                sb.AppendLine(display);
                sb.AppendLine();

                snippets.Add(new RagSnippet
                {
                    Key = item.key,
                    Content = display,
                    Score = item.score
                });
            }

            return (sb.ToString(), snippets);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LineChatGW] RAG retrieval failed, continuing without context");
            return (null, null);
        }
    }

    private JsonArray BuildMessages(List<ConvMessage> history, string userMessage, string? ragContext)
    {
        var messages = new JsonArray();
        var systemContent = _options.SystemPrompt;
        if (!string.IsNullOrEmpty(ragContext))
        {
            systemContent += "\n\n" + ragContext;
        }

        messages.Add(new JsonObject
        {
            ["role"] = "system",
            ["content"] = systemContent
        });

        foreach (var msg in history)
        {
            messages.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            });
        }

        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = userMessage
        });

        return messages;
    }

    private async Task<string?> CallLlmAsync(JsonArray messages, CancellationToken ct)
    {
        try
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
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[LineChatGW] LLM call timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LineChatGW] LLM call failed");
            return null;
        }
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
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[LineChatGW] High-level LLM returned {Status}: {Error}",
                response.StatusCode, Truncate(error, 200));
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var replyContent = doc.RootElement.GetProperty("message").GetProperty("content").GetString();
        return StripThinkTags(replyContent);
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
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[LineChatGW] High-level LLM returned {Status}: {Error}",
                response.StatusCode, Truncate(error, 200));
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return null;

        var replyContent = choices[0].GetProperty("message").GetProperty("content").GetString();
        return StripThinkTags(replyContent);
    }

    private async Task<string?> SendResponsesApiAsync(JsonArray messages, CancellationToken ct)
    {
        var input = new JsonArray();
        foreach (var msg in messages)
        {
            if (msg is not JsonObject obj)
                continue;

            var role = obj["role"]?.GetValue<string>() ?? "user";
            var text = obj["content"]?.GetValue<string>() ?? string.Empty;
            input.Add(new JsonObject
            {
                ["role"] = role,
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = text
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
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("[LineChatGW] High-level LLM returned {Status}: {Error}",
                response.StatusCode, Truncate(error, 200));
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return StripThinkTags(outputText.GetString());
        }

        if (doc.RootElement.TryGetProperty("output", out var output) &&
            output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var contentArray) || contentArray.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var contentItem in contentArray.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var textNode) &&
                        textNode.ValueKind == JsonValueKind.String)
                    {
                        return StripThinkTags(textNode.GetString());
                    }
                }
            }
        }

        return null;
    }

    private static List<ConvMessage> ParseMessages(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new List<ConvMessage>();

        try
        {
            return JsonSerializer.Deserialize<List<ConvMessage>>(json) ?? new List<ConvMessage>();
        }
        catch
        {
            return new List<ConvMessage>();
        }
    }

    private static string? StripThinkTags(string? replyContent)
    {
        if (string.IsNullOrWhiteSpace(replyContent))
            return replyContent?.Trim();

        if (replyContent.Contains("<think>", StringComparison.Ordinal))
        {
            var thinkEnd = replyContent.IndexOf("</think>", StringComparison.Ordinal);
            if (thinkEnd >= 0)
            {
                replyContent = replyContent[(thinkEnd + 8)..].TrimStart();
            }
        }

        return replyContent.Trim();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";

    private bool ShouldUseRag(string message)
    {
        if (_options.RagTriggerKeywords == null || _options.RagTriggerKeywords.Length == 0)
            return true;

        return _options.RagTriggerKeywords.Any(keyword =>
            !string.IsNullOrWhiteSpace(keyword) &&
            message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

public class LineChatGatewayOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxConversationHistory { get; set; } = 20;
    public bool RagEnabled { get; set; } = true;
    public int RagTopK { get; set; } = 3;
    public string[] RagTriggerKeywords { get; set; } =
    {
        "法律",
        "法規",
        "條文",
        "契約",
        "合約",
        "權益",
        "申訴",
        "罰則",
        "民法",
        "刑法",
        "勞基法",
        "消費者",
        "消保",
        "法務",
        "regulation",
        "compliance",
        "consumer protection",
        "contract",
        "law",
        "legal"
    };
    public string SystemPrompt { get; set; } =
        "你是一個透過 LINE 與使用者互動的高階智慧助理。請使用繁體中文，語氣簡潔清楚。"
        + " 你的角色是對話、澄清需求、回答一般問題，並在適當時引導使用者使用明確前綴。"
        + " 若問題需要受控網路搜尋，請建議使用 ?search 關鍵字。"
        + " 若使用者要建立或修改交付物，請引導使用 / 開頭的任務指令。";
}

public class ChatResult
{
    public string Reply { get; set; } = "";
    public string? Error { get; set; }
    public List<RagSnippet>? RagSnippets { get; set; }
    public int HistoryCount { get; set; }
}

public class ConvMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public class ConversationSummary
{
    public string UserId { get; set; } = "";
    public int MessageCount { get; set; }
    public string LastMessage { get; set; } = "";
    public string LastRole { get; set; } = "";
    public string LastTimestamp { get; set; } = "";
}

public class RagSnippet
{
    public string Key { get; set; } = "";
    public string Content { get; set; } = "";
    public float Score { get; set; }
}

public class RagFtsResult
{
    public string? SourceKey { get; set; }
    public string? Content { get; set; }
    public double Rank { get; set; }
}
