// Binance klines 唯讀本地快取(2026-05-26)。
// 多個 research 工具共用,避免每跑重抓 20+ 檔幣、節省 ~60s+ 並避 rate limit。
//
// 用法:var bars = await KlineCache.FetchOrLoad("BTCUSDT", "1d");
//
// 預設 TTL 24h:多數研究在 24h 內可重現;次日自動重抓避免資料過老。
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

        var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
        var json = await Http.GetStringAsync(url);
        await File.WriteAllTextAsync(path, json);
        return Parse(json);
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
