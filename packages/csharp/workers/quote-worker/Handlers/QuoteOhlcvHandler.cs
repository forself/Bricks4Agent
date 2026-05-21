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
            "get_bars"          => GetBars(opts),
            "fetch_stock"       => await FetchStock(opts, ct),
            "fetch_crypto"      => await FetchCrypto(opts, ct),
            "fetch_crypto_deep" => await FetchCryptoDeep(opts, ct),
            "get_funding"       => GetFunding(opts),
            "fetch_funding_deep" => await FetchFundingDeep(opts, ct),
            "get_oi_now"        => await GetOpenInterestNow(opts, ct),
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

    /// <summary>
    /// fetch_crypto_deep — 深度回補：分頁抓過去 target_bars 根到現在。
    /// symbol 可直接給 binance 符號（"BTCUSDT"）或 coingecko id（"bitcoin"）。
    /// 參數：symbol, interval（預設 1d）, target_bars（預設 1500）。
    /// </summary>
    private async Task<(bool, string?, string?)> FetchCryptoDeep(JsonElement opts, CancellationToken ct)
    {
        var symbol     = opts.TryGetProperty("symbol",      out var s) ? s.GetString() ?? "" : "";
        var interval   = opts.TryGetProperty("interval",    out var i) ? i.GetString() ?? "1d" : "1d";
        var targetBars = opts.TryGetProperty("target_bars", out var t) ? t.GetInt32() : 1500;

        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        // 已是 binance 符號（含 USDT 等 quote）就直接用；否則當 coingecko id 轉換
        var binanceSymbol = CommonQuotes.Any(q => symbol.EndsWith(q, StringComparison.OrdinalIgnoreCase))
            ? symbol.ToUpperInvariant()
            : HistoricalDataFetcher.CoinGeckoToBinance(symbol);

        var count = await _fetcher.FetchCryptoDeepAsync(binanceSymbol, interval, targetBars, ct);
        var json = JsonSerializer.Serialize(new
        {
            symbol, binance_symbol = binanceSymbol, interval, target_bars = targetBars, bars_saved = count
        });
        return (true, json, null);
    }

    // ── 永續資金費率 + 未平倉量 ─────────────────────────────────────

    /// <summary>get_funding — 查 DB 的資金費率序列（參數：symbol, limit 預設 1000）。</summary>
    private (bool, string?, string?) GetFunding(JsonElement opts)
    {
        var symbol = opts.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        var limit  = opts.TryGetProperty("limit",  out var l) ? l.GetInt32() : 1000;
        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        var points = _db.GetFundingRates(NormalizeCryptoSymbol(symbol), limit);
        var json = JsonSerializer.Serialize(new
        {
            symbol,
            count = points.Count,
            funding = points.Select(p => new { funding_time = p.FundingTime, funding_rate = p.FundingRate })
        });
        return (true, json, null);
    }

    /// <summary>fetch_funding_deep — 深度回補資金費率（參數：symbol, target_points 預設 1000）。</summary>
    private async Task<(bool, string?, string?)> FetchFundingDeep(JsonElement opts, CancellationToken ct)
    {
        var symbol       = opts.TryGetProperty("symbol",        out var s) ? s.GetString() ?? "" : "";
        var targetPoints = opts.TryGetProperty("target_points", out var t) ? t.GetInt32() : 1000;
        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        var binanceSymbol = ToBinanceSymbol(symbol);
        var count = await _fetcher.FetchFundingRateDeepAsync(binanceSymbol, targetPoints, ct);
        var json = JsonSerializer.Serialize(new
        {
            symbol, binance_symbol = binanceSymbol, target_points = targetPoints, points_saved = count
        });
        return (true, json, null);
    }

    /// <summary>get_oi_now — 當前未平倉量快照（OI history 只 ~30 天、故只給即時值當 live 訊號）。</summary>
    private async Task<(bool, string?, string?)> GetOpenInterestNow(JsonElement opts, CancellationToken ct)
    {
        var symbol = opts.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(symbol))
            return (false, null, "Missing required parameter: symbol");

        var binanceSymbol = ToBinanceSymbol(symbol);
        var oi = await _fetcher.FetchOpenInterestNowAsync(binanceSymbol, ct);
        if (oi == null)
            return (false, null, $"open interest unavailable for {binanceSymbol}");

        var json = JsonSerializer.Serialize(new
        {
            symbol, binance_symbol = binanceSymbol,
            open_interest = oi.Value.OpenInterest, time = oi.Value.Time
        });
        return (true, json, null);
    }

    /// <summary>外部 symbol（"BTC-USDT" / "bitcoin" / "BTCUSDT"）→ Binance 符號（"BTCUSDT"）。</summary>
    private static string ToBinanceSymbol(string symbol)
    {
        if (symbol.Contains('-'))
        {
            var parts = symbol.Split('-');
            if (parts.Length == 2 && IsCommonQuote(parts[1]))
                return (parts[0] + parts[1]).ToUpperInvariant();
        }
        return CommonQuotes.Any(q => symbol.EndsWith(q, StringComparison.OrdinalIgnoreCase))
            ? symbol.ToUpperInvariant()
            : HistoricalDataFetcher.CoinGeckoToBinance(symbol);
    }
}
