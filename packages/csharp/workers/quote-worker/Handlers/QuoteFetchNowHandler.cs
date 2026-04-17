using System.Text.Json;
using WorkerSdk;
using QuoteWorker.Queue;

namespace QuoteWorker.Handlers;

/// <summary>
/// quote.fetch_now — 立即觸發一次抓取（加入 queue）
/// </summary>
public class QuoteFetchNowHandler : ICapabilityHandler
{
    private readonly QuoteJobQueue _queue;
    public string CapabilityId => "quote.fetch_now";

    public QuoteFetchNowHandler(QuoteJobQueue queue) => _queue = queue;

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var queued = _queue.EnqueueNow();
        var json   = JsonSerializer.Serialize(new
        {
            queued  = queued,
            pending = _queue.PendingCount,
            message = queued ? "Fetch job enqueued" : "Queue full, try again later"
        });

        return Task.FromResult<(bool, string?, string?)>((true, json, null));
    }
}
