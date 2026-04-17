using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuoteWorker.Models;
using QuoteWorker.Storage;

namespace QuoteWorker.History;

/// <summary>
/// 歷史 K 線抓取器。
/// - 美股：Yahoo Finance chart API（最多 2 年日 K）
/// - 加密貨幣：Binance public klines API（最多 1000 根 bar）
/// 支援增量抓取（從 DB 最新一根 bar 之後開始）。
/// </summary>
public class HistoricalDataFetcher
{
    private readonly HttpClient _http;
    private readonly QuoteDbStorage _db;
    private readonly ILogger<HistoricalDataFetcher> _logger;

    public HistoricalDataFetcher(
        HttpClient http,
        QuoteDbStorage db,
        ILogger<HistoricalDataFetcher> logger)
    {
        _http   = http;
        _db     = db;
        _logger = logger;
    }

    // ── 美股：Yahoo Finance ──────────────────────────────────────────

    /// <summary>
    /// 抓取美股歷史日 K。
    /// range: "1mo","3mo","6mo","1y","2y","5y","max"
    /// </summary>
    public async Task<int> FetchStockHistoryAsync(
        string symbol, string range = "2y", string interval = "1d", CancellationToken ct = default)
    {
        var latestBar = _db.GetLatestBarTime(symbol, interval);
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}" +
                  $"?interval={interval}&range={range}";

        _logger.LogInformation("Fetching stock history: {Symbol} range={Range} interval={Interval}", symbol, range, interval);

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Yahoo Finance returned {Code} for {Symbol}", resp.StatusCode, symbol);
            return 0;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var chart = doc.RootElement.GetProperty("chart");
        if (!chart.TryGetProperty("result", out var resultArr) || resultArr.GetArrayLength() == 0)
            return 0;

        var result    = resultArr[0];
        var timestamps = result.GetProperty("timestamp");
        var ohlcv      = result.GetProperty("indicators").GetProperty("quote")[0];

        var opens   = ohlcv.GetProperty("open");
        var highs   = ohlcv.GetProperty("high");
        var lows    = ohlcv.GetProperty("low");
        var closes  = ohlcv.GetProperty("close");
        var volumes = ohlcv.GetProperty("volume");

        var bars = new List<OhlcvBar>();
        for (int i = 0; i < timestamps.GetArrayLength(); i++)
        {
            if (opens[i].ValueKind == JsonValueKind.Null ||
                closes[i].ValueKind == JsonValueKind.Null)
                continue;

            var openTime = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime;

            // 增量：跳過已有的 bar
            if (latestBar.HasValue && openTime <= latestBar.Value)
                continue;

            bars.Add(new OhlcvBar
            {
                Symbol    = symbol,
                Type      = "stock",
                Interval  = interval,
                OpenTime  = openTime,
                CloseTime = openTime.Date.AddDays(1).AddSeconds(-1),
                Open      = GetDecimal(opens[i]),
                High      = GetDecimal(highs[i]),
                Low       = GetDecimal(lows[i]),
                Close     = GetDecimal(closes[i]),
                Volume    = volumes[i].ValueKind != JsonValueKind.Null ? GetDecimal(volumes[i]) : 0,
            });
        }

        if (bars.Count > 0)
            _db.SaveBars(bars);

        _logger.LogInformation("Stock history {Symbol}: saved {Count} new bars (total range: {Range})",
            symbol, bars.Count, range);
        return bars.Count;
    }

    // ── 加密貨幣：Binance ────────────────────────────────────────────

    /// <summary>
    /// 抓取加密貨幣歷史 K 線。
    /// binanceSymbol: "BTCUSDT","ETHUSDT" 等。
    /// interval: "1m","5m","15m","1h","4h","1d","1w"
    /// </summary>
    public async Task<int> FetchCryptoHistoryAsync(
        string binanceSymbol, string interval = "1d", int limit = 365, CancellationToken ct = default)
    {
        var normalizedSymbol = binanceSymbol.Replace("USDT", "").ToUpper();
        var latestBar = _db.GetLatestBarTime(normalizedSymbol, interval);

        var url = $"https://api.binance.com/api/v3/klines" +
                  $"?symbol={binanceSymbol}&interval={interval}&limit={limit}";

        // 增量抓取：從最新 bar 之後開始
        if (latestBar.HasValue)
        {
            var startMs = new DateTimeOffset(latestBar.Value).ToUnixTimeMilliseconds() + 1;
            url += $"&startTime={startMs}";
        }

        _logger.LogInformation("Fetching crypto history: {Symbol} interval={Interval} limit={Limit}",
            binanceSymbol, interval, limit);

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Binance returned {Code} for {Symbol}", resp.StatusCode, binanceSymbol);
            return 0;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var bars = new List<OhlcvBar>();
        foreach (var kline in doc.RootElement.EnumerateArray())
        {
            // Binance kline format: [openTime, open, high, low, close, volume, closeTime, ...]
            var openTime  = DateTimeOffset.FromUnixTimeMilliseconds(kline[0].GetInt64()).UtcDateTime;
            var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(kline[6].GetInt64()).UtcDateTime;

            bars.Add(new OhlcvBar
            {
                Symbol    = normalizedSymbol,
                Type      = "crypto",
                Interval  = interval,
                OpenTime  = openTime,
                CloseTime = closeTime,
                Open      = decimal.Parse(kline[1].GetString()!),
                High      = decimal.Parse(kline[2].GetString()!),
                Low       = decimal.Parse(kline[3].GetString()!),
                Close     = decimal.Parse(kline[4].GetString()!),
                Volume    = decimal.Parse(kline[5].GetString()!),
            });
        }

        if (bars.Count > 0)
            _db.SaveBars(bars);

        _logger.LogInformation("Crypto history {Symbol}: saved {Count} new bars", binanceSymbol, bars.Count);
        return bars.Count;
    }

    /// <summary>
    /// CoinGecko id → Binance symbol 對應。
    /// </summary>
    public static string CoinGeckoToBinance(string coinGeckoId) => coinGeckoId.ToLower() switch
    {
        "bitcoin"  => "BTCUSDT",
        "ethereum" => "ETHUSDT",
        "solana"   => "SOLUSDT",
        "dogecoin" => "DOGEUSDT",
        "bnb"      => "BNBUSDT",
        "xrp"      => "XRPUSDT",
        "cardano"  => "ADAUSDT",
        "avalanche-2" => "AVAXUSDT",
        _ => $"{coinGeckoId.ToUpper()}USDT",
    };

    private static decimal GetDecimal(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number ? el.GetDecimal() :
        decimal.TryParse(el.GetString(), out var d) ? d : 0;
}
