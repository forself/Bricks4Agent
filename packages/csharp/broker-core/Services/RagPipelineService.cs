using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace BrokerCore.Services;

/// <summary>
/// RAG 管線服務 —— 提供查詢改寫、重排序、嵌入快取
///
/// 1. Query Rewriting：用 LLM 將使用者口語查詢展開為精準搜尋詞
/// 2. Re-ranking：用 LLM 對初步檢索結果重新評分排序
/// 3. Embedding Cache：快取最近查詢的嵌入向量（避免重複 API 呼叫）
///
/// 注意：此類位於 BrokerCore，使用 Action delegate 進行日誌輸出
/// </summary>
public class RagPipelineService
{
    private readonly RagPipelineOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Action<string>? _log;

    // ── Embedding Cache（LRU-like，固定大小） ──
    private readonly ConcurrentDictionary<string, CacheEntry<float[]>> _embeddingCache = new();
    private const int MaxEmbeddingCacheSize = 200;

    public RagPipelineService(RagPipelineOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.OllamaBaseUrl),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
    }

    public bool QueryRewriteEnabled => _options.QueryRewriteEnabled;
    public bool RerankEnabled => _options.RerankEnabled;
    public bool CacheEnabled => _options.CacheEnabled;

    // ════════════════════════════════════════════
    //  1. Query Rewriting（查詢改寫）
    // ════════════════════════════════════════════

    /// <summary>
    /// 將使用者口語查詢改寫為多個精準搜尋詞
    ///
    /// 例如：「退貨怎麼辦」→ ["退貨", "解除契約", "七日猶豫期", "消費者退貨權利"]
    /// </summary>
    public async Task<QueryRewriteResult> RewriteQueryAsync(string query)
    {
        if (!_options.QueryRewriteEnabled || string.IsNullOrWhiteSpace(query))
            return new QueryRewriteResult { OriginalQuery = query, ExpandedTerms = new[] { query } };

        try
        {
            var prompt = $@"你是搜尋查詢擴展助手。將以下使用者查詢擴展為 3-5 個精準搜尋詞（含原始查詢），用來提升全文檢索和語意搜尋的召回率。

使用者查詢：「{query}」

要求：
- 回傳 JSON 陣列格式：[""詞1"", ""詞2"", ""詞3""]
- 保持與原始查詢相同的語言
- 包含同義詞、相關法律用語、具體概念
- 只回傳 JSON 陣列，不要任何其他文字";

            var response = await CallOllamaAsync(prompt, _options.QueryRewriteModel);
            if (response == null)
                return new QueryRewriteResult { OriginalQuery = query, ExpandedTerms = new[] { query } };

            // 嘗試解析 JSON 陣列
            var terms = ParseJsonArray(response);
            if (terms.Length == 0)
                terms = new[] { query };

            // 確保原始查詢在第一個
            if (!terms.Contains(query))
                terms = new[] { query }.Concat(terms).ToArray();

            _log?.Invoke($"[QueryRewrite] '{query}' → [{string.Join(", ", terms)}]");

            return new QueryRewriteResult
            {
                OriginalQuery = query,
                ExpandedTerms = terms,
                RewrittenQuery = string.Join(" ", terms)
            };
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[QueryRewrite] Failed: {ex.Message}");
            return new QueryRewriteResult { OriginalQuery = query, ExpandedTerms = new[] { query } };
        }
    }

    // ════════════════════════════════════════════
    //  2. Re-ranking（重排序）
    // ════════════════════════════════════════════

    /// <summary>
    /// 用 LLM 對候選文件重新評分排序
    ///
    /// 輸入：查詢 + top-N 候選文件
    /// 輸出：按相關度重新排序的文件列表
    /// </summary>
    public async Task<List<RerankItem>> RerankAsync(string query, List<RerankItem> candidates, int topK = 5)
    {
        if (!_options.RerankEnabled || candidates.Count <= 1)
            return candidates.Take(topK).ToList();

        // 最多送 20 個候選給 LLM
        var toRerank = candidates.Take(Math.Min(candidates.Count, 20)).ToList();

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"查詢：「{query}」\n");
            sb.AppendLine("以下是候選文件，請為每個文件評估與查詢的相關度（0-10 分）：\n");

            for (int i = 0; i < toRerank.Count; i++)
            {
                var content = toRerank[i].Content;
                if (content.Length > 300) content = content[..300] + "...";
                sb.AppendLine($"[{i + 1}] {content}");
            }

            sb.AppendLine("\n回傳 JSON 陣列，格式：[{\"index\": 1, \"score\": 8}, ...]");
            sb.AppendLine("只回傳 JSON 陣列，不要任何其他文字。");

            var response = await CallOllamaAsync(sb.ToString(), _options.RerankModel);
            if (response == null)
                return candidates.Take(topK).ToList();

            // 解析 LLM 的評分結果
            var scores = ParseRerankScores(response);

            // 將 LLM 評分合併到候選項
            foreach (var (index, score) in scores)
            {
                if (index >= 1 && index <= toRerank.Count)
                    toRerank[index - 1].RerankScore = score;
            }

            // 沒被 LLM 評到的保持原始順序（給低分）
            for (int i = 0; i < toRerank.Count; i++)
            {
                if (toRerank[i].RerankScore < 0)
                    toRerank[i].RerankScore = 0;
            }

            var reranked = toRerank
                .OrderByDescending(x => x.RerankScore)
                .Take(topK)
                .ToList();

            _log?.Invoke($"[Rerank] Re-ranked {toRerank.Count} candidates → top {reranked.Count}");
            return reranked;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[Rerank] Failed: {ex.Message}");
            return candidates.Take(topK).ToList();
        }
    }

    // ════════════════════════════════════════════
    //  3. Embedding Cache（嵌入快取）
    // ════════════════════════════════════════════

    /// <summary>
    /// 帶快取的查詢嵌入：相同查詢在 TTL 內不重複呼叫 API
    /// </summary>
    public async Task<float[]?> GetCachedEmbeddingAsync(string text, EmbeddingService embeddingService)
    {
        if (!_options.CacheEnabled)
            return await embeddingService.EmbedAsync(text);

        var cacheKey = EmbeddingService.ComputeHash(text);

        if (_embeddingCache.TryGetValue(cacheKey, out var cached) &&
            cached.ExpiresAt > DateTime.UtcNow)
        {
            _log?.Invoke($"[EmbCache] Hit: {text[..Math.Min(text.Length, 30)]}...");
            return cached.Value;
        }

        var vector = await embeddingService.EmbedAsync(text);
        if (vector != null)
        {
            // 淘汰舊快取
            if (_embeddingCache.Count >= MaxEmbeddingCacheSize)
                EvictOldestCache();

            _embeddingCache[cacheKey] = new CacheEntry<float[]>
            {
                Value = vector,
                ExpiresAt = DateTime.UtcNow.AddSeconds(_options.CacheTtlSeconds)
            };
        }

        return vector;
    }

    /// <summary>清除所有快取</summary>
    public void ClearCache() => _embeddingCache.Clear();

    /// <summary>快取統計</summary>
    public (int count, int maxSize) GetCacheStats() => (_embeddingCache.Count, MaxEmbeddingCacheSize);

    // ════════════════════════════════════════════
    //  輔助方法
    // ════════════════════════════════════════════

    private async Task<string?> CallOllamaAsync(string prompt, string model)
    {
        // 嘗試主模型，失敗則嘗試備用模型
        var result = await CallOllamaWithModelAsync(prompt, model);
        if (result != null) return result;

        if (!string.IsNullOrEmpty(_options.FallbackModel) && _options.FallbackModel != model)
        {
            _log?.Invoke($"[RAGPipeline] Trying fallback model: {_options.FallbackModel}");
            return await CallOllamaWithModelAsync(prompt, _options.FallbackModel);
        }

        return null;
    }

    private async Task<string?> CallOllamaWithModelAsync(string prompt, string model)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            prompt,
            stream = false,
            options = new { temperature = 0.1, num_predict = 500 }
        });

        try
        {
            var response = await _httpClient.PostAsync(
                "/api/generate",
                new StringContent(body, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _log?.Invoke($"[RAGPipeline] Model '{model}' returned {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("response", out var resp))
                return resp.GetString();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[RAGPipeline] Model '{model}' call failed: {ex.Message}");
        }

        return null;
    }

    private static string[] ParseJsonArray(string text)
    {
        // 從回覆中提取 JSON 陣列
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return Array.Empty<string>();

        try
        {
            var jsonPart = text[start..(end + 1)];
            var arr = JsonSerializer.Deserialize<string[]>(jsonPart);
            return arr?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static List<(int index, float score)> ParseRerankScores(string text)
    {
        var results = new List<(int, float)>();
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return results;

        try
        {
            var jsonPart = text[start..(end + 1)];
            using var doc = JsonDocument.Parse(jsonPart);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var idx) ? idx.GetInt32() : -1;
                var score = item.TryGetProperty("score", out var s) ? s.GetSingle() : 0f;
                if (index > 0)
                    results.Add((index, score));
            }
        }
        catch { }

        return results;
    }

    private void EvictOldestCache()
    {
        // 簡單淘汰：移除最早過期的 1/4
        var toRemove = _embeddingCache
            .OrderBy(kv => kv.Value.ExpiresAt)
            .Take(MaxEmbeddingCacheSize / 4)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
            _embeddingCache.TryRemove(key, out _);
    }

    private class CacheEntry<T>
    {
        public T Value { get; set; } = default!;
        public DateTime ExpiresAt { get; set; }
    }
}

// ── 配置 ──

public class RagPipelineOptions
{
    /// <summary>Ollama base URL</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    // ── Query Rewrite ──
    public bool QueryRewriteEnabled { get; set; } = true;
    /// <summary>查詢改寫用的模型（輕量模型即可）</summary>
    public string QueryRewriteModel { get; set; } = "glm-4.7-flash";

    // ── Re-ranking ──
    public bool RerankEnabled { get; set; } = true;
    /// <summary>重排序用的模型</summary>
    public string RerankModel { get; set; } = "glm-4.7-flash";

    /// <summary>備用模型（主模型不可用時使用）</summary>
    public string FallbackModel { get; set; } = "glm-4.7-flash";

    // ── Cache ──
    public bool CacheEnabled { get; set; } = true;
    /// <summary>嵌入快取 TTL（秒）</summary>
    public int CacheTtlSeconds { get; set; } = 600;

    public int TimeoutSeconds { get; set; } = 60;
}

// ── DTO ──

public class QueryRewriteResult
{
    public string OriginalQuery { get; set; } = "";
    public string[] ExpandedTerms { get; set; } = Array.Empty<string>();
    public string RewrittenQuery { get; set; } = "";
}

public class RerankItem
{
    public string Key { get; set; } = "";
    public string Content { get; set; } = "";
    public string Source { get; set; } = "memory";
    public float OriginalScore { get; set; }
    public float Bm25Score { get; set; }
    public float VectorScore { get; set; }
    public float RerankScore { get; set; } = -1;
}
