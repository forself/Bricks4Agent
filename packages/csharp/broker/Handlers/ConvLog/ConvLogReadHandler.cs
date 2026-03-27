using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.ConvLog;

public sealed class ConvLogReadHandler : IRouteHandler
{
    private readonly BrokerDb _db;

    public string Route => "conv_log_read";

    public ConvLogReadHandler(BrokerDb db)
    {
        _db = db;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var userId = PayloadHelper.TryGetString(args, "user_id") ?? "";
        var limitStr = PayloadHelper.TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? l : 50;
        var before = PayloadHelper.TryGetString(args, "before"); // ISO timestamp

        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "user_id is required."));

        var taskId = request.TaskId ?? "global";
        var logKey = $"convlog:{userId}";

        var query = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == logKey);

        if (!string.IsNullOrEmpty(before) && DateTime.TryParse(before, out var beforeDt))
        {
            query = query.Where(e => e.CreatedAt < beforeDt);
        }

        // 取最近 N 則，按版本正序返回（舊→新）
        var entries = query
            .OrderByDescending(e => e.Version)
            .Take(limit)
            .ToList();

        entries.Reverse(); // 恢復時間正序

        var messages = entries.Select(e =>
        {
            try
            {
                var parsed = JsonDocument.Parse(e.ContentRef);
                return new
                {
                    version = e.Version,
                    role = parsed.RootElement.TryGetProperty("role", out var r) ? r.GetString() ?? "unknown" : "unknown",
                    content = parsed.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? e.ContentRef : e.ContentRef,
                    timestamp = parsed.RootElement.TryGetProperty("timestamp", out var t) ? t.GetString() ?? e.CreatedAt.ToString("o") : e.CreatedAt.ToString("o")
                };
            }
            catch
            {
                return new
                {
                    version = e.Version,
                    role = "unknown",
                    content = e.ContentRef,
                    timestamp = e.CreatedAt.ToString("o")
                };
            }
        }).ToList();

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { user_id = userId, messages, total = messages.Count })));
    }
}
