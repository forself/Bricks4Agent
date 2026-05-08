using System.Text.Json;
using WorkerSdk;
using QuoteWorker.History;
using QuoteWorker.Storage;

namespace QuoteWorker.Handlers;

/// <summary>
/// quote.ohlcv — 查詢或抓取 OHLCV 歷史 K 線。
///
/// Routes:
///   get_bars     — 查詢 DB 中的 K 線（參數：symbol, interval, limit）
///   fetch_stock  — 從 Yahoo Finance 抓取美股歷史（參數：symbol, range, interval）
///   fetch_crypto — 從 Binance 抓取加密貨幣歷史（參數：symbol, interval, limit）
/// </summary>
public class QuoteOhlcvHandler : ICapabilityHandler
{
    private readonly QuoteDbStorage _db;
    private readonly HistoricalDataFetcher _fetcher;
    public string CapabilityId => "quote.ohlcv";

    public QuoteOhlcvHandler(QuoteDbStorage db, HistoricalDataFetcher fetcher)
    {
        _db      = db;
        _fetcher = fetcher;
    }

    /// <summary>
    /// 把 BingX / Binance 等交易所慣用的 quote pair 格式（"BTC-USDT" / "BTCUSDT"）
    /// 轉成 DB 內存的基底符號（"BTC"）。FetchCryptoHistoryAsync 寫入時就 strip 掉 USDT、
    /// 所以這裡只是把外部呼叫的多種寫法統一回去。對美股或非標準 pair 直接 pass-through。
    /// </summary>
    internal static string NormalizeCryptoSymbol(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return symbol;
        // BingX 風格："BTC-USDT" → "BTC"
        if (symbol.Contains('-'))
        {
            var parts = symbol.Split('-');
            if (parts.Length == 2 && IsCommonQuote(parts[1]))
                return parts[0];
        }
        // Binance 風格（無分隔）："BTCUSDT" → "BTC"。只 strip 已知 quote、避免誤殺 stocks。
        foreach (var quote in CommonQuotes)
        {
            if (symbol.Length > quote.Length && symbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
                return symbol[..^quote.Length];
        }
        return symbol;
    }

    private static readonly string[] CommonQuotes = { "USDT", "USDC", "BUSD", "USD" };
    private static bool IsCommonQuote(string s) => CommonQuotes.Any(q => string.Equals(q, s, StringComparison.OrdinalIgnoreCase));

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var opts = string.IsNullOrWhiteSpace(payload)
            ? new JsonElement()
            : JsonDocument.Parse(payload).RootElement;

        return route switch
        {
            "get_bars"     => GetBars(opts),
            "fetch_stock"  => await FetchStock(opts, ct),
            "fetch_crypto" => await FetchCrypto(opts, ct),
            _ => (false, null, $"Unknown route: {route}")
        };
    }

    private (bool, string?, string?) GetBars(JsonElement opts)
    {
        var symbol   = opts.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
        var interval = opts.TryGetProperty("interval", out var i) ? i.GetString() ?? "1d" : "1d";
        var limit    = opts.TryGetProperty("limit",    out var l) ? l.GetInt32() : 365;

        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        var bars = _db.GetBars(NormalizeCryptoSymbol(symbol), interval, limit);
        var json = JsonSerializer.Serialize(new
        {
            symbol,
            interval,
            count = bars.Count,
            bars = bars.Select(b => new
            {
                open_time  = b.OpenTime,
                close_time = b.CloseTime,
                open       = b.Open,
                high       = b.High,
                low        = b.Low,
                close      = b.Close,
                volume     = b.Volume,
            })
        });
        return (true, json, null);
    }

    private async Task<(bool, string?, string?)> FetchStock(JsonElement opts, CancellationToken ct)
    {
        var symbol   = opts.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
        var range    = opts.TryGetProperty("range",    out var r) ? r.GetString() ?? "2y" : "2y";
        var interval = opts.TryGetProperty("interval", out var i) ? i.GetString() ?? "1d" : "1d";

        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        var count = await _fetcher.FetchStockHistoryAsync(symbol, range, interval, ct);
        var json = JsonSerializer.Serialize(new { symbol, range, interval, bars_saved = count });
        return (true, json, null);
    }

    private async Task<(bool, string?, string?)> FetchCrypto(JsonElement opts, CancellationToken ct)
    {
        var symbol   = opts.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
        var interval = opts.TryGetProperty("interval", out var i) ? i.GetString() ?? "1d" : "1d";
        var limit    = opts.TryGetProperty("limit",    out var l) ? l.GetInt32() : 365;

        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        var binanceSymbol = HistoricalDataFetcher.CoinGeckoToBinance(symbol);
        var count = await _fetcher.FetchCryptoHistoryAsync(binanceSymbol, interval, limit, ct);
        var json = JsonSerializer.Serialize(new { symbol, binance_symbol = binanceSymbol, interval, bars_saved = count });
        return (true, json, null);
    }
}
