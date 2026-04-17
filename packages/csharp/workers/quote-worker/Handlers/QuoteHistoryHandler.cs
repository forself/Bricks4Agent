using System.Text.Json;
using WorkerSdk;
using QuoteWorker.Queue;

namespace QuoteWorker.Handlers;

/// <summary>
/// quote.history — 回傳最近 N 筆 job 歷程（包含 error 清單）
/// </summary>
public class QuoteHistoryHandler : ICapabilityHandler
{
    private readonly QuoteJobQueue _queue;
    public string CapabilityId => "quote.history";

    public QuoteHistoryHandler(QuoteJobQueue queue) => _queue = queue;

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var take = 20;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("take", out var t))
                take = t.GetInt32();
        }
        catch { }

        var history = _queue.GetHistory(take);

        var json = JsonSerializer.Serialize(new
        {
            total    = history.Count,
            pending  = _queue.PendingCount,
            jobs     = history.Select(j => new
            {
                job_id        = j.JobId,
                status        = j.Status,
                created_at    = j.CreatedAt,
                started_at    = j.StartedAt,
                completed_at  = j.CompletedAt,
                duration_sec  = Math.Round(j.DurationSeconds, 1),
                total_symbols = j.TotalSymbols,
                fetched_count = j.FetchedCount,
                error_count   = j.ErrorCount,
                fatal_error   = j.FatalError,
                errors        = j.Errors
            })
        });

        return Task.FromResult<(bool, string?, string?)>((true, json, null));
    }
}
