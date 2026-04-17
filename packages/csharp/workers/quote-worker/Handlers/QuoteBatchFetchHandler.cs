using System.Text.Json;
using WorkerSdk;
using QuoteWorker.History;

namespace QuoteWorker.Handlers;

/// <summary>
/// quote.batch_fetch — 一鍵批次抓取所有 symbol 的歷史 K 線。
///
/// Routes:
///   fetch_all — 抓取全部（增量，不重複）
///   status    — 查看抓取狀態
/// </summary>
public class QuoteBatchFetchHandler : ICapabilityHandler
{
    private readonly StartupHistoryFetcher _fetcher;
    public string CapabilityId => "quote.batch_fetch";

    public QuoteBatchFetchHandler(StartupHistoryFetcher fetcher) => _fetcher = fetcher;

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        return route switch
        {
            "fetch_all" => await FetchAll(ct),
            "status"    => GetStatus(),
            _ => (false, null, $"Unknown route: {route}")
        };
    }

    private async Task<(bool, string?, string?)> FetchAll(CancellationToken ct)
    {
        if (_fetcher.IsFetching)
            return (true, JsonSerializer.Serialize(new { status = "already_fetching" }), null);

        var (totalBars, errors) = await _fetcher.FetchAllAsync(ct);
        var json = JsonSerializer.Serialize(new
        {
            status = "done",
            total_bars = totalBars,
            error_count = errors.Count,
            errors,
        });
        return (true, json, null);
    }

    private (bool, string?, string?) GetStatus()
    {
        var json = JsonSerializer.Serialize(new
        {
            is_fetching = _fetcher.IsFetching,
            last_status = _fetcher.LastStatus,
        });
        return (true, json, null);
    }
}
