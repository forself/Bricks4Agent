using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using Broker.Helpers;

namespace Broker.Handlers.Memory;

public sealed class MemoryRetrieveHandler : BrokerCore.Services.IRouteHandler
{
    private readonly BrokerDb _db;

    public string Route => "memory_retrieve";

    public MemoryRetrieveHandler(BrokerDb db)
    {
        _db = db;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var key = PayloadHelper.TryGetString(args, "key");
        var search = PayloadHelper.TryGetString(args, "search");
        var limitStr = PayloadHelper.TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? l : 20;

        var taskId = request.TaskId ?? "global";
        var entries = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId);

        if (!string.IsNullOrEmpty(key))
        {
            // 精確 key 查詢 — 回傳最新版本
            var latest = entries.Where(e => e.Key == key)
                .OrderByDescending(e => e.Version)
                .FirstOrDefault();

            if (latest == null)
                return Task.FromResult(ExecutionResult.Ok(request.RequestId,
                    JsonSerializer.Serialize(new { key, found = false })));

            return Task.FromResult(ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new
                {
                    key = latest.Key,
                    value = latest.ContentRef,
                    version = latest.Version,
                    content_type = latest.ContentType,
                    created_at = latest.CreatedAt.ToString("o"),
                    found = true
                })));
        }

        if (!string.IsNullOrEmpty(search))
        {
            // 模糊搜尋 key 和 value
            var results = entries
                .Where(e => e.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            e.ContentRef.Contains(search, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Key)
                .Select(g => g.OrderByDescending(e => e.Version).First())
                .Take(limit)
                .Select(e => new
                {
                    key = e.Key,
                    value = e.ContentRef.Length > 200 ? e.ContentRef[..200] + "..." : e.ContentRef,
                    version = e.Version
                })
                .ToList();

            return Task.FromResult(ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { search, matches = results, total = results.Count })));
        }

        // 列出所有 keys
        var allKeys = entries
            .GroupBy(e => e.Key)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Version).First();
                return new { key = latest.Key, version = latest.Version, content_type = latest.ContentType };
            })
            .Take(limit)
            .ToList();

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { keys = allKeys, total = allKeys.Count })));
    }
}
