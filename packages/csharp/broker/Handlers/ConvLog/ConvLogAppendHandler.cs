using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using Broker.Helpers;

namespace Broker.Handlers.ConvLog;

public sealed class ConvLogAppendHandler : BrokerCore.Services.IRouteHandler
{
    private readonly BrokerDb _db;

    public string Route => "conv_log_append";

    public ConvLogAppendHandler(BrokerDb db)
    {
        _db = db;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var userId = PayloadHelper.TryGetString(args, "user_id") ?? "";
        var role = PayloadHelper.TryGetString(args, "role") ?? "user";
        var content = PayloadHelper.TryGetString(args, "content") ?? "";
        var metadata = PayloadHelper.TryGetString(args, "metadata") ?? "";

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(content))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "user_id and content are required."));

        var taskId = request.TaskId ?? "global";
        var principalId = request.PrincipalId ?? "system";
        var now = DateTime.UtcNow;

        // key 格式: convlog:{userId} — 每則訊息一筆，版本遞增
        var logKey = $"convlog:{userId}";
        var documentId = $"convlog_{taskId}_{userId}";

        // 查詢最新版本號
        var existing = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == logKey)
            .OrderByDescending(e => e.Version)
            .FirstOrDefault();

        var version = (existing?.Version ?? 0) + 1;

        // 內容包含角色 + 時間戳 + 原始訊息（結構化 JSON）
        var logEntry = JsonSerializer.Serialize(new
        {
            role,
            content,
            timestamp = now.ToString("o"),
            metadata
        });

        var entry = new SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("cvl"),
            DocumentId = documentId,
            Version = version,
            ParentVersion = existing?.Version,
            Key = logKey,
            ContentRef = logEntry,
            ContentType = "application/json",
            AuthorPrincipalId = principalId,
            TaskId = taskId,
            CreatedAt = now
        };

        _db.Insert(entry);

        // FTS5 索引對話內容
        try
        {
            _db.Execute(
                "INSERT INTO convlog_fts(user_id, role, content, task_id) VALUES(@userId, @role, @content, @taskId)",
                new { userId, role, content = Fts5Utility.PrepareFts5Query(content), taskId });
        }
        catch { /* FTS5 表可能不存在 */ }

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { user_id = userId, version, logged = true })));
    }
}
