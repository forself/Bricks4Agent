// Binance klines 唯讀本地快取(2026-05-26)。
// 多個 research 工具共用,避免每跑重抓 20+ 檔幣、節省 ~60s+ 並避 rate limit。
//
// 用法:var bars = await KlineCache.FetchOrLoad("BTCUSDT", "1d");
//      var bars = await KlineCache.FetchOrLoad("BTCUSDT", "1d", limit: 2000); // 多頁拼接
//
// 預設 TTL 24h、limit 1000(Binance 單次最大)。limit > 1000 自動分頁、用 endTime 倒推。
// 強制刷新:設環境變數 FORCE_REFRESH_KLINES=1。
// 快取位置:~/.cache/brick4agent/klines/(可用 KLINE_CACHE_DIR 覆寫)。
using StrategyWorker.Engine;
using System.Globalization;
using System.Text.Json;

namespace ToolsShared;

public static class KlineCache
{
    private static readonly string CacheDir =
        Environment.GetEnvironmentVariable("KLINE_CACHE_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "brick4agent", "klines");

    private static readonly bool ForceRefresh =
        Environment.GetEnvironmentVariable("FORCE_REFRESH_KLINES") == "1";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const int BinanceLimitPerCall = 1000;

    public static async Task<List<BarData>> FetchOrLoad(
        string symbol, string interval = "1d", int limit = 1000, TimeSpan? ttl = null)
    {
        ttl ??= TimeSpan.FromHours(24);
        Directory.CreateDirectory(CacheDir);
        var path = Path.Combine(CacheDir, $"{symbol}-{interval}-{limit}.json");

        if (!ForceRefresh && File.Exists(path))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age < ttl)
                return Parse(await File.ReadAllTextAsync(path));
        }

        // limit ≤ 1000:單次抓
        if (limit <= BinanceLimitPerCall)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
            var json = await Http.GetStringAsync(url);
            await File.WriteAllTextAsync(path, json);
            return Parse(json);
        }

        // limit > 1000:分頁抓、用 endTime 倒推
        // 第一次:取最近 1000(無 endTime)
        // 之後:取 endTime < earliest.OpenTime 的下一個 1000
        var allBars = new List<BarData>();
        long? endTime = null;
        int remaining = limit;
        while (remaining > 0)
        {
            int chunk = Math.Min(remaining, BinanceLimitPerCall);
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={chunk}"
                    + (endTime.HasValue ? $"&endTime={endTime.Value}" : "");
            var json = await Http.GetStringAsync(url);
            var chunkBars = Parse(json);
            if (chunkBars.Count == 0) break;   // 已抓到歷史最早

            // 加在前面(因為新抓的是更舊的、Binance 回應已是時間升序)
            allBars.InsertRange(0, chunkBars);
            remaining -= chunkBars.Count;

            // 下一次抓 endTime = 這次最舊那根的 OpenTime - 1ms(避重複)
            var oldestMs = new DateTimeOffset(chunkBars[0].OpenTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            endTime = oldestMs - 1;

            if (chunkBars.Count < chunk) break;   // 已抓到歷史最早(API 回少於要求數量)
        }

        // 序列化存檔(用 Binance kline JSON array format 重組)
        var serialized = ReSerialize(allBars);
        await File.WriteAllTextAsync(path, serialized);
        return allBars;
    }

    /// <summary>把 BarData list 轉回 Binance kline JSON array(供 cache 存檔)。</summary>
    private static string ReSerialize(List<BarData> bars)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < bars.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var b = bars[i];
            var t = new DateTimeOffset(b.OpenTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
            // Binance kline 12 欄,我們只用前 6,其他填 0/空
            sb.Append($"[{t},\"{b.Open.ToString(CultureInfo.InvariantCulture)}\",\"{b.High.ToString(CultureInfo.InvariantCulture)}\","
                    + $"\"{b.Low.ToString(CultureInfo.InvariantCulture)}\",\"{b.Close.ToString(CultureInfo.InvariantCulture)}\","
                    + $"\"{b.Volume.ToString(CultureInfo.InvariantCulture)}\",0,\"0\",0,\"0\",\"0\",\"0\"]");
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>列出當前快取(debug 用)。</summary>
    public static (int files, long bytes) Stats()
    {
        if (!Directory.Exists(CacheDir)) return (0, 0);
        var files = Directory.GetFiles(CacheDir, "*.json");
        long total = 0;
        foreach (var f in files) total += new FileInfo(f).Length;
        return (files.Length, total);
    }

    private static List<BarData> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var bars = new List<BarData>();
        foreach (var k in doc.RootElement.EnumerateArray())
            bars.Add(new BarData
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()).UtcDateTime,
                Open  = decimal.Parse(k[1].GetString()!, CultureInfo.InvariantCulture),
                High  = decimal.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
                Low   = decimal.Parse(k[3].GetString()!, CultureInfo.InvariantCulture),
                Close = decimal.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
                Volume = decimal.Parse(k[5].GetString()!, CultureInfo.InvariantCulture),
            });
        return bars;
    }
}
