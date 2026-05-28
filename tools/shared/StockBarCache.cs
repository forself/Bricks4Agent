// 美股日線本地快取(Yahoo Finance chart API、免費無 key)。
// 對齊 KlineCache 介面,讓 strat-validate 等工具能用同一套 walk-forward/pool-t-stat 機制驗股票。
//
// 用法:var bars = await StockBarCache.FetchOrLoad("AAPL", "1d", 1300);
// 快取:~/.cache/brick4agent/stock/{SYMBOL}-{interval}-{limit}.json,TTL 24h。
using StrategyWorker.Engine;
using System.Globalization;
using System.Text.Json;

namespace ToolsShared;

public static class StockBarCache
{
    private static readonly string CacheDir =
        Environment.GetEnvironmentVariable("STOCK_CACHE_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "brick4agent", "stock");

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; b4a-research/1.0)");
        return h;
    }

    /// <summary>抓 symbol 的日線(最多 limit 根、到現在)。interval 目前只支援 "1d"(Yahoo range 用 max)。</summary>
    public static async Task<List<BarData>> FetchOrLoad(string symbol, string interval = "1d", int limit = 1300, TimeSpan? ttl = null)
    {
        ttl ??= TimeSpan.FromHours(24);
        Directory.CreateDirectory(CacheDir);
        var path = Path.Combine(CacheDir, $"{symbol}-{interval}-{limit}.json");
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < ttl)
        {
            try { return ParseCached(await File.ReadAllTextAsync(path)); } catch { }
        }

        // Yahoo chart API:range 取夠長(limit 根日線 ≈ limit 日曆日 / 0.69 交易日比例;取 max 最簡單)
        string range = limit > 1000 ? "10y" : (limit > 500 ? "5y" : "2y");
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?interval={interval}&range={range}";
        string json;
        try { json = await Http.GetStringAsync(url); }
        catch (Exception ex) { Console.WriteLine($"[StockBarCache] {symbol}: fetch failed ({ex.Message})"); return new(); }

        var bars = ParseYahoo(json);
        if (bars.Count > limit) bars = bars.Skip(bars.Count - limit).ToList();   // 取最近 limit 根
        if (bars.Count > 0)
            await File.WriteAllTextAsync(path, SerializeCache(bars));
        return bars;
    }

    private static List<BarData> ParseYahoo(string json)
    {
        var result = new List<BarData>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("chart").GetProperty("result");
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return result;
        var r0 = root[0];
        if (!r0.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.Array) return result;
        var quote = r0.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open"); var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low"); var closes = quote.GetProperty("close");
        var vols = quote.GetProperty("volume");
        int n = ts.GetArrayLength();
        for (int i = 0; i < n; i++)
        {
            // 任一 OHLC 為 null(停牌/缺漏)→ skip 該根
            if (closes[i].ValueKind != JsonValueKind.Number || opens[i].ValueKind != JsonValueKind.Number) continue;
            result.Add(new BarData
            {
                OpenTime = DateTimeOffset.FromUnixTimeSeconds(ts[i].GetInt64()).UtcDateTime.Date,
                Open = opens[i].GetDecimal(),
                High = highs[i].ValueKind == JsonValueKind.Number ? highs[i].GetDecimal() : closes[i].GetDecimal(),
                Low = lows[i].ValueKind == JsonValueKind.Number ? lows[i].GetDecimal() : closes[i].GetDecimal(),
                Close = closes[i].GetDecimal(),
                Volume = vols[i].ValueKind == JsonValueKind.Number ? vols[i].GetDecimal() : 0m,
            });
        }
        return result;
    }

    private static string SerializeCache(List<BarData> bars) =>
        JsonSerializer.Serialize(bars.Select(b => new
        {
            t = new DateTimeOffset(b.OpenTime, TimeSpan.Zero).ToUnixTimeSeconds(),
            o = b.Open.ToString(CultureInfo.InvariantCulture), h = b.High.ToString(CultureInfo.InvariantCulture),
            l = b.Low.ToString(CultureInfo.InvariantCulture), c = b.Close.ToString(CultureInfo.InvariantCulture),
            v = b.Volume.ToString(CultureInfo.InvariantCulture),
        }));

    private static List<BarData> ParseCached(string json)
    {
        var result = new List<BarData>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            result.Add(new BarData
            {
                OpenTime = DateTimeOffset.FromUnixTimeSeconds(el.GetProperty("t").GetInt64()).UtcDateTime.Date,
                Open = decimal.Parse(el.GetProperty("o").GetString()!, CultureInfo.InvariantCulture),
                High = decimal.Parse(el.GetProperty("h").GetString()!, CultureInfo.InvariantCulture),
                Low = decimal.Parse(el.GetProperty("l").GetString()!, CultureInfo.InvariantCulture),
                Close = decimal.Parse(el.GetProperty("c").GetString()!, CultureInfo.InvariantCulture),
                Volume = decimal.Parse(el.GetProperty("v").GetString()!, CultureInfo.InvariantCulture),
            });
        }
        return result;
    }
}
