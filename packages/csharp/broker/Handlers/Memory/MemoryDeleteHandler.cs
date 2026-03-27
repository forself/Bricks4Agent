using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Memory;

public sealed class MemoryDeleteHandler : IRouteHandler
{
    private readonly BrokerDb _db;

    public string Route => "memory_delete";

    public MemoryDeleteHandler(BrokerDb db)
    {
        _db = db;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var key = PayloadHelper.TryGetString(args, "key") ?? "";

        if (string.IsNullOrEmpty(key))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "key is required."));

        var taskId = request.TaskId ?? "global";
        var deleted = _db.Execute(
            "DELETE FROM shared_context_entries WHERE task_id = @taskId AND key = @key",
            new { taskId, key });

        // 清除 FTS5 + 向量
        try { _db.Execute("DELETE FROM memory_fts WHERE source_key = @key AND task_id = @taskId", new { key, taskId }); } catch { }
        try { _db.Execute("DELETE FROM vector_entries WHERE source_key = @key AND task_id = @taskId", new { key, taskId }); } catch { }

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { key, deleted_count = deleted })));
    }
}
