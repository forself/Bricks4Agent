using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Memory;

public sealed class MemoryStoreHandler : IRouteHandler
{
    private readonly ILogger<MemoryStoreHandler> _logger;
    private readonly BrokerDb _db;
    private readonly EmbeddingService? _embeddingService;

    public string Route => "memory_store";

    public MemoryStoreHandler(
        ILogger<MemoryStoreHandler> logger,
        BrokerDb db,
        EmbeddingService? embeddingService = null)
    {
        _logger = logger;
        _db = db;
        _embeddingService = embeddingService;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var key = PayloadHelper.TryGetString(args, "key") ?? "";
        var value = PayloadHelper.TryGetString(args, "value") ?? "";
        var contentType = PayloadHelper.TryGetString(args, "content_type") ?? "text/plain";

        if (string.IsNullOrEmpty(key))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "key is required."));

        var taskId = request.TaskId ?? "global";
        var principalId = request.PrincipalId ?? "system";
        var documentId = $"mem_{taskId}_{key}";

        // 查詢同 key 的最新版本
        var existing = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == key)
            .OrderByDescending(e => e.Version)
            .FirstOrDefault();

        var version = (existing?.Version ?? 0) + 1;

        var entry = new SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("sce"),
            DocumentId = documentId,
            Version = version,
            ParentVersion = existing?.Version,
            Key = key,
            ContentRef = value,
            ContentType = contentType,
            AuthorPrincipalId = principalId,
            TaskId = taskId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Insert(entry);

        // FTS5 索引：先刪除舊的再插入新的
        try
        {
            _db.Execute("DELETE FROM memory_fts WHERE source_key = @key AND task_id = @taskId",
                new { key, taskId });
            _db.Execute("INSERT INTO memory_fts(source_key, content, task_id) VALUES(@key, @value, @taskId)",
                new { key, value = Fts5Utility.PrepareFts5Query(value), taskId });
        }
        catch { /* FTS5 表可能不存在，忽略 */ }

        // 非同步嵌入（fire-and-forget，不阻塞回覆）
        if (_embeddingService is { IsEnabled: true })
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var hash = EmbeddingService.ComputeHash(value);

                    // 檢查是否已有相同 hash 的嵌入
                    var existingVec = _db.GetAll<VectorEntry>()
                        .FirstOrDefault(v => v.ContentHash == hash && v.TaskId == taskId);
                    if (existingVec != null)
                    {
                        // 更新 source_key 即可
                        existingVec.SourceKey = key;
                        _db.Update(existingVec);
                        return;
                    }

                    var vector = await _embeddingService.EmbedAsync($"{key}: {value}");
                    if (vector == null) return;

                    // 刪除該 key 的舊向量
                    _db.Execute("DELETE FROM vector_entries WHERE source_key = @key AND task_id = @taskId",
                        new { key, taskId });

                    _db.Insert(new VectorEntry
                    {
                        EntryId = BrokerCore.IdGen.New("vec"),
                        SourceKey = key,
                        TaskId = taskId,
                        TextPreview = value.Length > 500 ? value[..500] : value,
                        ContentHash = hash,
                        Embedding = EmbeddingService.VectorToBytes(vector),
                        EmbeddingModel = _embeddingService.ModelName,
                        Dimension = vector.Length,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Async embedding failed for key {Key}", key);
                }
            });
        }

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { key, version, stored = true,
                embedding_queued = _embeddingService is { IsEnabled: true } })));
    }
}
