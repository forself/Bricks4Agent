using Microsoft.Extensions.Logging;
using QuoteWorker.Storage;

namespace QuoteWorker.History;

/// <summary>
/// 啟動時自動抓取所有已設定 symbol 的歷史 K 線。
/// 只抓增量（DB 中已有的不會重複抓）。
/// 同時支援批次手動觸發。
/// </summary>
public class StartupHistoryFetcher
{
    private readonly HistoricalDataFetcher _fetcher;
    private readonly QuoteDbStorage _db;
    private readonly ILogger<StartupHistoryFetcher> _logger;
    private readonly string[] _stockSymbols;
    private readonly string[] _cryptoIds;

    public bool IsFetching { get; private set; }
    public string LastStatus { get; private set; } = "idle";

    public StartupHistoryFetcher(
        HistoricalDataFetcher fetcher,
        QuoteDbStorage db,
        ILogger<StartupHistoryFetcher> logger,
        string stockSymbols,
        string cryptoIds)
    {
        _fetcher      = fetcher;
        _db           = db;
        _logger       = logger;
        _stockSymbols = stockSymbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _cryptoIds    = cryptoIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>啟動後延遲執行一次完整歷史抓取。</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        // 等 10 秒讓 worker 先連上 broker
        await Task.Delay(TimeSpan.FromSeconds(10), ct).ContinueWith(_ => { });

        _logger.LogInformation("StartupHistoryFetcher: begin initial fetch for {Stocks} stocks, {Crypto} crypto",
            _stockSymbols.Length, _cryptoIds.Length);

        await FetchAllAsync(ct);
    }

    /// <summary>批次抓取所有 symbol 的所有時間框架。</summary>
    public async Task<(int totalBars, List<string> errors)> FetchAllAsync(CancellationToken ct = default)
    {
        if (IsFetching) return (0, new List<string> { "Already fetching" });

        IsFetching = true;
        LastStatus = "fetching";
        int totalBars = 0;
        var errors = new List<string>();

        var intervals = new[] { "1d", "1h", "4h" };

        // 美股
        foreach (var symbol in _stockSymbols)
        {
            if (ct.IsCancellationRequested) break;
            foreach (var interval in new[] { "1d" }) // Yahoo 只支援日線
            {
                try
                {
                    var count = await _fetcher.FetchStockHistoryAsync(symbol, "2y", interval, ct);
                    totalBars += count;
                    _logger.LogInformation("History: {Symbol} {Interval} → {Count} bars", symbol, interval, count);
                    await Task.Delay(500, ct).ContinueWith(_ => { }); // rate limit
                }
                catch (Exception ex)
                {
                    errors.Add($"{symbol}/{interval}: {ex.Message}");
                    _logger.LogWarning(ex, "History fetch failed: {Symbol} {Interval}", symbol, interval);
                }
            }
        }

        // 加密貨幣（Binance 支援多時間框架）
        foreach (var coinId in _cryptoIds)
        {
            if (ct.IsCancellationRequested) break;
            var binanceSymbol = HistoricalDataFetcher.CoinGeckoToBinance(coinId);
            foreach (var interval in intervals)
            {
                try
                {
                    var limit = interval == "1d" ? 365 : interval == "4h" ? 500 : 500;
                    var count = await _fetcher.FetchCryptoHistoryAsync(binanceSymbol, interval, limit, ct);
                    totalBars += count;
                    _logger.LogInformation("History: {Symbol} {Interval} → {Count} bars", binanceSymbol, interval, count);
                    await Task.Delay(300, ct).ContinueWith(_ => { });
                }
                catch (Exception ex)
                {
                    errors.Add($"{binanceSymbol}/{interval}: {ex.Message}");
                    _logger.LogWarning(ex, "History fetch failed: {Symbol} {Interval}", binanceSymbol, interval);
                }
            }
        }

        IsFetching = false;
        LastStatus = $"done: {totalBars} bars, {errors.Count} errors";
        _logger.LogInformation("StartupHistoryFetcher complete: {Total} bars, {Errors} errors", totalBars, errors.Count);
        return (totalBars, errors);
    }
}
