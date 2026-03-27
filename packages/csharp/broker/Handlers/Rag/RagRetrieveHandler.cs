using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Handlers.Rag;

public sealed class RagRetrieveHandler : BrokerCore.Services.IRouteHandler
{
    private readonly ILogger<RagRetrieveHandler> _logger;
    private readonly BrokerDb _db;
    private readonly EmbeddingService? _embeddingService;
    private readonly RagPipelineService? _ragPipeline;

    public string Route => "rag_retrieve";

    public RagRetrieveHandler(
        ILogger<RagRetrieveHandler> logger,
        BrokerDb db,
        EmbeddingService? embeddingService = null,
        RagPipelineService? ragPipeline = null)
    {
        _logger = logger;
        _db = db;
        _embeddingService = embeddingService;
        _ragPipeline = ragPipeline;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var mode = PayloadHelper.TryGetString(args, "mode") ?? "hybrid";
        var limitStr = PayloadHelper.TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 20) : 5;
        var thresholdStr = PayloadHelper.TryGetString(args, "threshold");
        var threshold = float.TryParse(thresholdStr, out var t) ? t : 0.2f;
        var includeConvlog = args.TryGetProperty("include_convlog", out var icl) &&
                             icl.ValueKind == JsonValueKind.True;
        var rewrite = !args.TryGetProperty("rewrite", out var rw) || rw.ValueKind != JsonValueKind.False; // 預設開啟
        var rerank = !args.TryGetProperty("rerank", out var rr) || rr.ValueKind != JsonValueKind.False; // 預設開啟

        // 新增：標籤過濾
        List<string>? filterTags = null;
        if (args.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            filterTags = new List<string>();
            foreach (var tagItem in tagsEl.EnumerateArray())
            {
                var tagStr = tagItem.GetString();
                if (!string.IsNullOrWhiteSpace(tagStr)) filterTags.Add(tagStr);
            }
            if (filterTags.Count == 0) filterTags = null;
        }

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        var taskId = request.TaskId ?? "global";
        var k = 60; // RRF constant

        // ── Step 1: Query Rewriting ──
        string searchQuery = query;
        string[]? expandedTerms = null;
        if (rewrite && _ragPipeline is { QueryRewriteEnabled: true })
        {
            var rewriteResult = await _ragPipeline.RewriteQueryAsync(query);
            searchQuery = rewriteResult.RewrittenQuery;
            expandedTerms = rewriteResult.ExpandedTerms;
        }

        // ── Step 2: Tag Pre-filter（取得符合標籤的 source_key 集合） ──
        HashSet<string>? allowedKeys = null;
        if (filterTags != null)
        {
            allowedKeys = new HashSet<string>();
            var allEntries = _db.GetAll<SharedContextEntry>()
                .Where(e => e.TaskId == taskId);

            foreach (var entry in allEntries)
            {
                try
                {
                    var entryTags = JsonSerializer.Deserialize<string[]>(entry.Tags ?? "[]");
                    if (entryTags != null && entryTags.Any(et => filterTags.Any(ft =>
                        et.Contains(ft, StringComparison.OrdinalIgnoreCase))))
                    {
                        allowedKeys.Add(entry.Key);
                    }
                }
                catch { }
            }
        }

        // ── Step 3: BM25 分支 ──
        var bm25Scores = new Dictionary<string, float>();
        var bm25Contents = new Dictionary<string, string>();

        if (mode is "fulltext" or "hybrid")
        {
            try
            {
                // 用改寫後的多個搜尋詞分別查詢，合併結果
                var searchTerms = expandedTerms ?? new[] { query };
                foreach (var term in searchTerms.Take(5))
                {
                    var ftsResults = _db.Query<FtsResult>(
                        "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                        new { q = Fts5Utility.PrepareFts5Query(term), taskId, fetchLimit = limit * 3 });

                    int rank = 1;
                    foreach (var r in ftsResults)
                    {
                        if (r.SourceKey == null) continue;
                        if (allowedKeys != null && !allowedKeys.Contains(r.SourceKey)) continue;

                        var score = 1.0f / (k + rank);
                        // 合併分數（多個搜尋詞命中同一文件，分數累加）
                        bm25Scores[r.SourceKey] = bm25Scores.GetValueOrDefault(r.SourceKey, 0f) + score;
                        if (!bm25Contents.ContainsKey(r.SourceKey))
                            bm25Contents[r.SourceKey] = r.Content ?? "";
                        rank++;
                    }
                }

                // 也搜尋對話日誌
                if (includeConvlog)
                {
                    var convResults = _db.Query<ConvlogFtsResult>(
                        "SELECT user_id, role, content, rank FROM convlog_fts WHERE convlog_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                        new { q = Fts5Utility.PrepareFts5Query(query), taskId, fetchLimit = limit * 2 });

                    int convRank = 1;
                    foreach (var r in convResults)
                    {
                        var convKey = $"__convlog__{r.UserId}_{convRank}";
                        bm25Scores[convKey] = 1.0f / (k + convRank) * 0.5f;
                        bm25Contents[convKey] = $"[{r.Role}] {r.Content}";
                        convRank++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG BM25 branch failed");
            }
        }

        // ── Step 4: 向量分支（帶快取） ──
        var vecScores = new Dictionary<string, float>();

        if ((mode is "semantic" or "hybrid") && _embeddingService is { IsEnabled: true })
        {
            try
            {
                // 使用快取取得嵌入
                float[]? queryVector;
                if (_ragPipeline is { CacheEnabled: true })
                    queryVector = await _ragPipeline.GetCachedEmbeddingAsync(query, _embeddingService);
                else
                    queryVector = await _embeddingService.EmbedAsync(query);

                if (queryVector != null)
                {
                    var vectors = _db.GetAll<VectorEntry>()
                        .Where(v => v.TaskId == taskId && v.Embedding.Length > 0)
                        .ToList();

                    // 標籤過濾
                    if (allowedKeys != null)
                        vectors = vectors.Where(v => allowedKeys.Contains(v.SourceKey)).ToList();

                    var scored = vectors.Select(v =>
                    {
                        var vec = EmbeddingService.BytesToVector(v.Embedding);
                        var sim = EmbeddingService.CosineSimilarity(queryVector, vec);
                        return new { v.SourceKey, sim };
                    })
                    .Where(x => x.sim >= threshold)
                    .OrderByDescending(x => x.sim)
                    .Take(limit * 3)
                    .ToList();

                    int rank = 1;
                    foreach (var s in scored)
                    {
                        vecScores[s.SourceKey] = 1.0f / (k + rank);
                        rank++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG vector branch failed");
            }
        }

        // ── Step 5: Reciprocal Rank Fusion ──
        var allKeysSet = bm25Scores.Keys.Union(vecScores.Keys).Distinct();
        var fused = allKeysSet.Select(key =>
        {
            var bm25 = bm25Scores.GetValueOrDefault(key, 0f);
            var vec = vecScores.GetValueOrDefault(key, 0f);
            return new { key, score = bm25 + vec, bm25, vec };
        })
        .OrderByDescending(x => x.score)
        .Take(limit * 2) // 取多一些給 re-rank
        .ToList();

        // 組裝候選結果（附帶原始內容）
        var candidates = fused.Select(f =>
        {
            string content;
            string source;

            if (f.key.StartsWith("__convlog__"))
            {
                content = bm25Contents.GetValueOrDefault(f.key, "");
                source = "convlog";
            }
            else
            {
                if (bm25Contents.TryGetValue(f.key, out var cached))
                {
                    content = cached;
                }
                else
                {
                    var entry = _db.GetAll<SharedContextEntry>()
                        .Where(e => e.TaskId == taskId && e.Key == f.key)
                        .OrderByDescending(e => e.Version)
                        .FirstOrDefault();
                    content = entry?.ContentRef ?? "";
                }
                source = "memory";
            }

            return new RerankItem
            {
                Key = f.key,
                Content = content,
                Source = source,
                OriginalScore = f.score,
                Bm25Score = f.bm25,
                VectorScore = f.vec
            };
        }).ToList();

        // ── Step 6: Re-ranking ──
        List<RerankItem> finalResults;
        bool reranked = false;

        if (rerank && _ragPipeline is { RerankEnabled: true } && candidates.Count > 1)
        {
            finalResults = await _ragPipeline.RerankAsync(query, candidates, limit);
            reranked = true;
        }
        else
        {
            finalResults = candidates.Take(limit).ToList();
        }

        // 組裝最終結果
        var results = finalResults.Select(r => new
        {
            key = r.Key,
            content = r.Content.Length > 1000 ? r.Content[..1000] + "..." : r.Content,
            score = MathF.Round(reranked ? r.RerankScore : r.OriginalScore, 4),
            bm25_score = MathF.Round(r.Bm25Score, 4),
            vector_score = MathF.Round(r.VectorScore, 4),
            rerank_score = reranked ? MathF.Round(r.RerankScore, 4) : (float?)null,
            source = r.Source
        }).ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new
            {
                query,
                mode,
                results,
                total = results.Count,
                bm25_candidates = bm25Scores.Count,
                vector_candidates = vecScores.Count,
                pipeline = new
                {
                    query_rewrite = expandedTerms != null,
                    expanded_terms = expandedTerms,
                    reranked,
                    tag_filter = filterTags
                }
            }));
    }

    // FTS5 結果 DTO
    private class FtsResult
    {
        [BaseOrm.Column("source_key")]
        public string? SourceKey { get; set; }
        [BaseOrm.Column("content")]
        public string? Content { get; set; }
        [BaseOrm.Column("rank")]
        public double Rank { get; set; }
    }

    private class ConvlogFtsResult
    {
        [BaseOrm.Column("user_id")]
        public string? UserId { get; set; }
        [BaseOrm.Column("role")]
        public string? Role { get; set; }
        [BaseOrm.Column("content")]
        public string? Content { get; set; }
        [BaseOrm.Column("rank")]
        public double Rank { get; set; }
    }
}
