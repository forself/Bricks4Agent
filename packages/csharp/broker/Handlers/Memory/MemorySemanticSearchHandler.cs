using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Handlers.Memory;

public sealed class MemorySemanticSearchHandler : BrokerCore.Services.IRouteHandler
{
    private readonly BrokerDb _db;
    private readonly EmbeddingService _embeddingService;
    private readonly RagPipelineService? _ragPipeline;

    public string Route => "memory_semantic_search";

    public MemorySemanticSearchHandler(
        BrokerDb db,
        EmbeddingService embeddingService,
        RagPipelineService? ragPipeline = null)
    {
        _db = db;
        _embeddingService = embeddingService;
        _ragPipeline = ragPipeline;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        if (_embeddingService is not { IsEnabled: true })
            return ExecutionResult.Fail(request.RequestId, "Embedding service not enabled.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var locale = PayloadHelper.TryGetString(args, "locale") ?? "zh-TW";
        var safeMode = PayloadHelper.TryGetString(args, "safe_mode") ?? "moderate";
        var limitStr = PayloadHelper.TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 20) : 5;
        var thresholdStr = PayloadHelper.TryGetString(args, "threshold");
        var threshold = float.TryParse(thresholdStr, out var t) ? t : 0.3f;

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        var taskId = request.TaskId ?? "global";

        // 嵌入查詢文字（帶快取）
        float[]? queryVector;
        if (_ragPipeline is { CacheEnabled: true })
            queryVector = await _ragPipeline.GetCachedEmbeddingAsync(query, _embeddingService);
        else
            queryVector = await _embeddingService.EmbedAsync(query);

        if (queryVector == null)
            return ExecutionResult.Fail(request.RequestId, "Failed to embed query.");

        // 載入所有向量（小資料量，in-memory cosine）
        var vectors = _db.GetAll<VectorEntry>()
            .Where(v => v.TaskId == taskId && v.Embedding.Length > 0)
            .ToList();

        // 計算相似度
        var scored = vectors.Select(v =>
        {
            var vec = EmbeddingService.BytesToVector(v.Embedding);
            var similarity = EmbeddingService.CosineSimilarity(queryVector, vec);
            return new { v.SourceKey, v.TextPreview, similarity };
        })
        .Where(x => x.similarity >= threshold)
        .OrderByDescending(x => x.similarity)
        .Take(limit)
        .ToList();

        // 附帶原始記憶內容
        var results = scored.Select(s =>
        {
            var entry = _db.GetAll<SharedContextEntry>()
                .Where(e => e.TaskId == taskId && e.Key == s.SourceKey)
                .OrderByDescending(e => e.Version)
                .FirstOrDefault();

            return new
            {
                key = s.SourceKey,
                content = entry?.ContentRef ?? s.TextPreview,
                similarity = MathF.Round(s.similarity, 4),
                version = entry?.Version ?? 0
            };
        }).ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, results, total = results.Count, vector_count = vectors.Count }));
    }
}
