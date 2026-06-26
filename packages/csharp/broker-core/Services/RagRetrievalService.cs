using System.Text.Json;
using System.Text.Json.Serialization;
using BaseOrm;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

public sealed class RagRetrievalService
{
    private readonly BrokerDb _db;
    private readonly EmbeddingService? _embeddingService;
    private readonly RagPipelineService? _ragPipeline;
    private readonly Action<string>? _log;

    public RagRetrievalService(
        BrokerDb db,
        EmbeddingService? embeddingService = null,
        RagPipelineService? ragPipeline = null,
        Action<string>? log = null)
    {
        _db = db;
        _embeddingService = embeddingService;
        _ragPipeline = ragPipeline;
        _log = log;
    }

    public async Task<RagRetrieveResponse> RetrieveAsync(
        RagRetrieveRequest request,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("query is required.", nameof(request));

        taskId = string.IsNullOrWhiteSpace(taskId) ? "global" : taskId;

        var limit = Math.Clamp(request.Limit <= 0 ? 5 : request.Limit, 1, 20);
        var mode = NormalizeMode(request.Mode);
        var threshold = request.Threshold ?? 0.2f;
        const int rrfK = 60;

        var searchQuery = request.Query;
        string[]? expandedTerms = null;
        if (request.Rewrite && _ragPipeline is { QueryRewriteEnabled: true })
        {
            var rewriteResult = await _ragPipeline.RewriteQueryAsync(request.Query);
            searchQuery = string.IsNullOrWhiteSpace(rewriteResult.RewrittenQuery)
                ? request.Query
                : rewriteResult.RewrittenQuery;
            expandedTerms = rewriteResult.ExpandedTerms;
        }

        var allowedKeys = ResolveAllowedKeys(taskId, request.Tags);

        var bm25Scores = new Dictionary<string, float>();
        var bm25Contents = new Dictionary<string, string>();
        if (mode is "fulltext" or "hybrid")
            CollectFulltextCandidates(taskId, searchQuery, expandedTerms, request.Query, limit, request.IncludeConvlog, allowedKeys, bm25Scores, bm25Contents, rrfK);

        var vectorScores = new Dictionary<string, float>();
        if (mode is "semantic" or "hybrid")
            await CollectVectorCandidatesAsync(taskId, request.Query, threshold, limit, allowedKeys, vectorScores, rrfK, cancellationToken);

        var fused = bm25Scores.Keys
            .Union(vectorScores.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(key =>
            {
                var bm25 = bm25Scores.GetValueOrDefault(key, 0f);
                var vector = vectorScores.GetValueOrDefault(key, 0f);
                return new { key, score = bm25 + vector, bm25, vector };
            })
            .OrderByDescending(item => item.score)
            .Take(limit * 2)
            .ToList();

        var candidates = fused.Select(item =>
        {
            var isConvlog = item.key.StartsWith("__convlog__", StringComparison.Ordinal);
            var content = isConvlog
                ? bm25Contents.GetValueOrDefault(item.key, "")
                : GetLatestEntryContent(taskId, item.key) ?? bm25Contents.GetValueOrDefault(item.key, "");

            return new RerankItem
            {
                Key = item.key,
                Content = content,
                Source = isConvlog ? "convlog" : "memory",
                OriginalScore = item.score,
                Bm25Score = item.bm25,
                VectorScore = item.vector
            };
        }).ToList();

        List<RerankItem> finalResults;
        var reranked = false;
        if (request.Rerank && _ragPipeline is { RerankEnabled: true } && candidates.Count > 1)
        {
            finalResults = await _ragPipeline.RerankAsync(request.Query, candidates, limit);
            reranked = true;
        }
        else
        {
            finalResults = candidates.Take(limit).ToList();
        }

        var results = finalResults.Select(item => new RagRetrieveResult
        {
            Key = item.Key,
            Content = item.Content.Length > 1000 ? item.Content[..1000] + "..." : item.Content,
            Score = MathF.Round(reranked ? item.RerankScore : item.OriginalScore, 4),
            Bm25Score = MathF.Round(item.Bm25Score, 4),
            VectorScore = MathF.Round(item.VectorScore, 4),
            RerankScore = reranked ? MathF.Round(item.RerankScore, 4) : null,
            Source = item.Source
        }).ToList();

        return new RagRetrieveResponse
        {
            Query = request.Query,
            Mode = mode,
            Results = results,
            Total = results.Count,
            Bm25Candidates = bm25Scores.Count,
            VectorCandidates = vectorScores.Count,
            Pipeline = new RagRetrievePipelineMetadata
            {
                QueryRewrite = expandedTerms != null,
                ExpandedTerms = expandedTerms,
                Reranked = reranked,
                TagFilter = request.Tags
            }
        };
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "hybrid";

        return mode.Trim().ToLowerInvariant() switch
        {
            "keyword" or "keywords" or "fts" => "fulltext",
            var value => value
        };
    }

    private HashSet<string>? ResolveAllowedKeys(string taskId, IReadOnlyList<string>? filterTags)
    {
        if (filterTags is not { Count: > 0 })
            return null;

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var entries = _db.GetAll<SharedContextEntry>().Where(entry => entry.TaskId == taskId);
        foreach (var entry in entries)
        {
            try
            {
                var entryTags = JsonSerializer.Deserialize<string[]>(entry.Tags ?? "[]");
                if (entryTags != null && entryTags.Any(entryTag =>
                    filterTags.Any(filterTag => entryTag.Contains(filterTag, StringComparison.OrdinalIgnoreCase))))
                {
                    allowed.Add(entry.Key);
                }
            }
            catch
            {
                if (filterTags.Any(filterTag => entry.Key.Contains(filterTag, StringComparison.OrdinalIgnoreCase)))
                    allowed.Add(entry.Key);
            }
        }

        return allowed;
    }

    private void CollectFulltextCandidates(
        string taskId,
        string searchQuery,
        string[]? expandedTerms,
        string originalQuery,
        int limit,
        bool includeConvlog,
        HashSet<string>? allowedKeys,
        Dictionary<string, float> bm25Scores,
        Dictionary<string, string> bm25Contents,
        int rrfK)
    {
        try
        {
            var searchTerms = expandedTerms is { Length: > 0 } ? expandedTerms : new[] { searchQuery };
            foreach (var term in searchTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Take(5))
            {
                var rows = _db.Query<RagFtsRow>(
                    "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                    new { q = Fts5TextNormalizer.PrepareQuery(term), taskId, fetchLimit = limit * 3 });

                var rank = 1;
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.SourceKey))
                        continue;
                    if (allowedKeys != null && !allowedKeys.Contains(row.SourceKey))
                        continue;

                    bm25Scores[row.SourceKey] = bm25Scores.GetValueOrDefault(row.SourceKey, 0f) + 1.0f / (rrfK + rank);
                    if (!bm25Contents.ContainsKey(row.SourceKey))
                        bm25Contents[row.SourceKey] = row.Content ?? "";
                    rank++;
                }
            }

            if (!includeConvlog)
                return;

            var convRows = _db.Query<RagConvlogFtsRow>(
                "SELECT user_id, role, content, rank FROM convlog_fts WHERE convlog_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                new { q = Fts5TextNormalizer.PrepareQuery(originalQuery), taskId, fetchLimit = limit * 2 });

            var convRank = 1;
            foreach (var row in convRows)
            {
                var convKey = $"__convlog__{row.UserId}_{convRank}";
                bm25Scores[convKey] = 1.0f / (rrfK + convRank) * 0.5f;
                bm25Contents[convKey] = $"[{row.Role}] {row.Content}";
                convRank++;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[RAG] Fulltext branch failed: {ex.Message}");
        }
    }

    private async Task CollectVectorCandidatesAsync(
        string taskId,
        string query,
        float threshold,
        int limit,
        HashSet<string>? allowedKeys,
        Dictionary<string, float> vectorScores,
        int rrfK,
        CancellationToken cancellationToken)
    {
        if (_embeddingService is not { IsEnabled: true })
            return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queryVector = _ragPipeline is { CacheEnabled: true }
                ? await _ragPipeline.GetCachedEmbeddingAsync(query, _embeddingService)
                : await _embeddingService.EmbedAsync(query);

            if (queryVector == null)
                return;

            var currentModel = _embeddingService.ModelName;
            var vectors = _db.GetAll<VectorEntry>()
                .Where(vector =>
                    vector.TaskId == taskId &&
                    vector.Embedding.Length > 0 &&
                    vector.Dimension == queryVector.Length &&
                    string.Equals(vector.EmbeddingModel, currentModel, StringComparison.Ordinal))
                .ToList();

            if (allowedKeys != null)
                vectors = vectors.Where(vector => allowedKeys.Contains(vector.SourceKey)).ToList();

            var scored = vectors
                .Select(vector =>
                {
                    var entryVector = EmbeddingService.BytesToVector(vector.Embedding);
                    var similarity = EmbeddingService.CosineSimilarity(queryVector, entryVector);
                    return new { vector.SourceKey, similarity };
                })
                .Where(item => item.similarity >= threshold)
                .OrderByDescending(item => item.similarity)
                .Take(limit * 3)
                .ToList();

            var rank = 1;
            foreach (var item in scored)
            {
                vectorScores[item.SourceKey] = 1.0f / (rrfK + rank);
                rank++;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[RAG] Vector branch failed: {ex.Message}");
        }
    }

    private string? GetLatestEntryContent(string taskId, string key)
        => _db.GetAll<SharedContextEntry>()
            .Where(entry => entry.TaskId == taskId && entry.Key == key)
            .OrderByDescending(entry => entry.Version)
            .FirstOrDefault()
            ?.ContentRef;
}

public sealed class RagRetrieveRequest
{
    public string Query { get; set; } = "";
    public string Mode { get; set; } = "hybrid";
    public int Limit { get; set; } = 5;
    public float? Threshold { get; set; }
    public bool IncludeConvlog { get; set; }
    public bool Rewrite { get; set; } = true;
    public bool Rerank { get; set; } = true;
    public List<string>? Tags { get; set; }
    public string? TaskId { get; set; }

    public static RagRetrieveRequest FromArgs(JsonElement args)
    {
        var request = new RagRetrieveRequest
        {
            Query = TryGetString(args, "query") ?? "",
            Mode = TryGetString(args, "mode") ?? "hybrid",
            TaskId = TryGetString(args, "task_id")
        };

        var limitValue = TryGetString(args, "limit");
        if (int.TryParse(limitValue, out var parsedLimit))
            request.Limit = parsedLimit;
        else if (args.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind == JsonValueKind.Number)
            request.Limit = limitElement.GetInt32();

        var thresholdValue = TryGetString(args, "threshold");
        if (float.TryParse(thresholdValue, out var parsedThreshold))
            request.Threshold = parsedThreshold;
        else if (args.TryGetProperty("threshold", out var thresholdElement) && thresholdElement.ValueKind == JsonValueKind.Number)
            request.Threshold = thresholdElement.GetSingle();

        request.IncludeConvlog = args.TryGetProperty("include_convlog", out var includeConvlog) &&
                                 includeConvlog.ValueKind == JsonValueKind.True;
        request.Rewrite = !args.TryGetProperty("rewrite", out var rewrite) ||
                          rewrite.ValueKind != JsonValueKind.False;
        request.Rerank = !args.TryGetProperty("rerank", out var rerank) ||
                         rerank.ValueKind != JsonValueKind.False;

        if (args.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            request.Tags = tagsElement
                .EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToList();
            if (request.Tags.Count == 0)
                request.Tags = null;
        }

        return request;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}

public sealed class RagRetrieveResponse
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "hybrid";

    [JsonPropertyName("results")]
    public List<RagRetrieveResult> Results { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("bm25_candidates")]
    public int Bm25Candidates { get; set; }

    [JsonPropertyName("vector_candidates")]
    public int VectorCandidates { get; set; }

    [JsonPropertyName("pipeline")]
    public RagRetrievePipelineMetadata Pipeline { get; set; } = new();
}

public sealed class RagRetrieveResult
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("bm25_score")]
    public float Bm25Score { get; set; }

    [JsonPropertyName("vector_score")]
    public float VectorScore { get; set; }

    [JsonPropertyName("rerank_score")]
    public float? RerankScore { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "memory";
}

public sealed class RagRetrievePipelineMetadata
{
    [JsonPropertyName("query_rewrite")]
    public bool QueryRewrite { get; set; }

    [JsonPropertyName("expanded_terms")]
    public string[]? ExpandedTerms { get; set; }

    [JsonPropertyName("reranked")]
    public bool Reranked { get; set; }

    [JsonPropertyName("tag_filter")]
    public List<string>? TagFilter { get; set; }
}

file sealed class RagFtsRow
{
    [Column("source_key")]
    public string? SourceKey { get; set; }

    [Column("content")]
    public string? Content { get; set; }

    [Column("rank")]
    public double Rank { get; set; }
}

file sealed class RagConvlogFtsRow
{
    [Column("user_id")]
    public string? UserId { get; set; }

    [Column("role")]
    public string? Role { get; set; }

    [Column("content")]
    public string? Content { get; set; }

    [Column("rank")]
    public double Rank { get; set; }
}
