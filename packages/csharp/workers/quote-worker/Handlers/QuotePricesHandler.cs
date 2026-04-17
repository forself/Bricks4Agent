using System.Text.Json;
using WorkerSdk;
using QuoteWorker.Queue;

namespace QuoteWorker.Handlers;

/// <summary>
/// quote.prices — 回傳所有 symbol 的最新報價
/// </summary>
public class QuotePricesHandler : ICapabilityHandler
{
    private readonly QuoteJobQueue _queue;
    public string CapabilityId => "quote.prices";

    public QuotePricesHandler(QuoteJobQueue queue) => _queue = queue;

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var prices = _queue.GetLatestPrices();

        var json = JsonSerializer.Serialize(new
        {
            count  = prices.Count,
            quotes = prices.Select(q => new
            {
                symbol         = q.Symbol,
                name           = q.Name,
                type           = q.Type,
                price          = q.Price,
                change         = q.Change,
                change_percent = q.ChangePercent,
                currency       = q.Currency,
                market_cap     = q.MarketCap,
                volume_24h     = q.Volume24h,
                fetched_at     = q.FetchedAt
            })
        });

        return Task.FromResult<(bool, string?, string?)>((true, json, null));
    }
}
