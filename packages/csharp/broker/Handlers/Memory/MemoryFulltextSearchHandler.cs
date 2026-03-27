using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using Broker.Helpers;

namespace Broker.Handlers.Memory;

public sealed class MemoryFulltextSearchHandler : BrokerCore.Services.IRouteHandler
{
    private readonly ILogger<MemoryFulltextSearchHandler> _logger;
    private readonly BrokerDb _db;

    public string Route => "memory_fulltext_search";

    public MemoryFulltextSearchHandler(ILogger<MemoryFulltextSearchHandler> logger, BrokerDb db)
    {
        _logger = logger;
        _db = db;
    }

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var scope = PayloadHelper.TryGetString(args, "scope") ?? "memory";
        var limitStr = PayloadHelper.TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 50) : 10;

        if (string.IsNullOrEmpty(query))
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, "query is required."));

        var taskId = request.TaskId ?? "global";
        var results = new List<object>();

        // 搜尋智慧記憶
        if (scope is "memory" or "all")
        {
            try
            {
                var ftsResults = _db.Query<FtsResult>(
                    "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @limit",
                    new { q = Fts5Utility.PrepareFts5Query(query), taskId, limit });

                foreach (var r in ftsResults)
                {
                    results.Add(new
                    {
                        source = "memory",
                        key = r.SourceKey,
                        content = r.Content?.Length > 300 ? r.Content[..300] + "..." : r.Content,
                        bm25_score = r.Rank
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FTS5 memory search failed");
            }
        }

        // 搜尋對話日誌
        if (scope is "convlog" or "all")
        {
            try
            {
                var ftsResults = _db.Query<ConvlogFtsResult>(
                    "SELECT user_id, role, content, rank FROM convlog_fts WHERE convlog_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @limit",
                    new { q = Fts5Utility.PrepareFts5Query(query), taskId, limit });

                foreach (var r in ftsResults)
                {
                    results.Add(new
                    {
                        source = "convlog",
                        user_id = r.UserId,
                        role = r.Role,
                        content = r.Content?.Length > 300 ? r.Content[..300] + "..." : r.Content,
                        bm25_score = r.Rank
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FTS5 convlog search failed");
            }
        }

        return Task.FromResult(ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, scope, results, total = results.Count })));
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
