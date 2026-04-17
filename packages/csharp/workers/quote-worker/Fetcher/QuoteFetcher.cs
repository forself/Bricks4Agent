using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuoteWorker.Models;

namespace QuoteWorker.Fetcher;

/// <summary>
/// 從 CoinGecko（加密貨幣）和 Yahoo Finance chart API（股票）抓取報價。
/// 兩個 API 皆不需要 API key。
/// </summary>
public class QuoteFetcher
{
    private readonly HttpClient _http;
    private readonly ILogger<QuoteFetcher> _logger;
    private readonly string[] _cryptoIds;
    private readonly string[] _stockSymbols;

    public QuoteFetcher(
        HttpClient http,
        ILogger<QuoteFetcher> logger,
        string cryptoIds,
        string stockSymbols)
    {
        _http = http;
        _logger = logger;
        _cryptoIds = cryptoIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _stockSymbols = stockSymbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<(List<QuoteResult> Results, List<string> Errors)> FetchAllAsync(CancellationToken ct)
    {
        var results = new List<QuoteResult>();
        var errors  = new List<string>();

        // ── 加密貨幣：CoinGecko ──────────────────────────────────────
        if (_cryptoIds.Length > 0)
        {
            try
            {
                var crypto = await FetchCryptoAsync(ct);
                results.AddRange(crypto);
                _logger.LogInformation("CoinGecko: fetched {Count} crypto quotes", crypto.Count);
            }
            catch (Exception ex)
            {
                var msg = $"CoinGecko fetch failed: {ex.Message}";
                errors.Add(msg);
                _logger.LogWarning(ex, "CoinGecko fetch failed");
            }
        }

        // ── 股票：Yahoo Finance chart API（逐一抓取，避免 rate limit）──
        foreach (var symbol in _stockSymbols)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var stock = await FetchStockAsync(symbol, ct);
                if (stock != null)
                    results.Add(stock);
            }
            catch (Exception ex)
            {
                var msg = $"Yahoo [{symbol}] failed: {ex.Message}";
                errors.Add(msg);
                _logger.LogWarning(ex, "Yahoo Finance fetch failed for {Symbol}", symbol);
            }

            // 每個 symbol 間隔 300ms 避免被 rate limit
            await Task.Delay(300, ct).ContinueWith(_ => { });
        }

        return (results, errors);
    }

    // ── CoinGecko API ─────────────────────────────────────────────────
    private async Task<List<QuoteResult>> FetchCryptoAsync(CancellationToken ct)
    {
        var ids = string.Join(",", _cryptoIds);
        var url = $"https://api.coingecko.com/api/v3/coins/markets" +
                  $"?vs_currency=usd&ids={ids}&order=market_cap_desc" +
                  $"&per_page=20&price_change_percentage=24h";

        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<QuoteResult>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add(new QuoteResult
            {
                Symbol        = (item.TryGetProperty("symbol", out var sym) ? sym.GetString() : null)?.ToUpper() ?? "",
                Name          = item.TryGetProperty("name",    out var nm)  ? nm.GetString()  ?? "" : "",
                Price         = item.TryGetProperty("current_price",               out var pr)  && pr.ValueKind  != JsonValueKind.Null ? pr.GetDecimal()  : 0,
                Change        = item.TryGetProperty("price_change_24h",            out var ch)  && ch.ValueKind  != JsonValueKind.Null ? ch.GetDecimal()  : 0,
                ChangePercent = item.TryGetProperty("price_change_percentage_24h", out var cp)  && cp.ValueKind  != JsonValueKind.Null ? cp.GetDecimal()  : 0,
                MarketCap     = item.TryGetProperty("market_cap",                  out var mc)  && mc.ValueKind  != JsonValueKind.Null ? mc.GetDecimal()  : 0,
                Volume24h     = item.TryGetProperty("total_volume",                out var vol) && vol.ValueKind != JsonValueKind.Null ? vol.GetDecimal() : 0,
                Currency      = "USD",
                Type          = "crypto",
            });
        }
        return list;
    }

    // ── Yahoo Finance chart API ───────────────────────────────────────
    private async Task<QuoteResult?> FetchStockAsync(string symbol, CancellationToken ct)
    {
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval=1d&range=1d";
        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogDebug("Yahoo Finance returned {Code} for {Symbol}", resp.StatusCode, symbol);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var chart = doc.RootElement.GetProperty("chart");
        if (!chart.TryGetProperty("result", out var resultArr) || resultArr.GetArrayLength() == 0)
            return null;

        var meta      = resultArr[0].GetProperty("meta");
        var price     = meta.TryGetProperty("regularMarketPrice",  out var rmp) && rmp.ValueKind != JsonValueKind.Null ? rmp.GetDecimal() : 0;
        var prevClose = meta.TryGetProperty("chartPreviousClose",  out var pc)  && pc.ValueKind  != JsonValueKind.Null ? pc.GetDecimal()  :
                        meta.TryGetProperty("previousClose",       out var pc2) && pc2.ValueKind != JsonValueKind.Null ? pc2.GetDecimal() : 0;
        var currency  = meta.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "USD" : "USD";
        var longName  = meta.TryGetProperty("longName",  out var ln)  ? ln.GetString()  ?? symbol : symbol;

        var change        = prevClose > 0 ? price - prevClose : 0;
        var changePct     = prevClose > 0 ? (change / prevClose) * 100m : 0;

        return new QuoteResult
        {
            Symbol        = symbol,
            Name          = longName,
            Price         = price,
            Change        = change,
            ChangePercent = Math.Round(changePct, 2),
            Currency      = currency,
            Type          = "stock",
        };
    }
}
