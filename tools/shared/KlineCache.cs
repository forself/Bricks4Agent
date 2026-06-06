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
using System.IO.Compression;
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

        var allBars = new List<BarData>();
        if (limit <= BinanceLimitPerCall)
        {
            // limit ≤ 1000:單次抓
            try
            {
                var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
                allBars = Parse(await Http.GetStringAsync(url));
            }
            catch { allBars = new(); }
        }
        else
        {
            // limit > 1000:分頁抓、用 endTime 倒推(第一次取最近 1000、之後往更舊倒推)
            long? endTime = null;
            int remaining = limit;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, BinanceLimitPerCall);
                var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={chunk}"
                        + (endTime.HasValue ? $"&endTime={endTime.Value}" : "");
                List<BarData> chunkBars;
                try { chunkBars = Parse(await Http.GetStringAsync(url)); } catch { break; }
                if (chunkBars.Count == 0) break;   // 已抓到歷史最早
                allBars.InsertRange(0, chunkBars);   // 新抓的是更舊的、加前面
                remaining -= chunkBars.Count;
                var oldestMs = new DateTimeOffset(chunkBars[0].OpenTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
                endTime = oldestMs - 1;
                if (chunkBars.Count < chunk) break;   // API 回少於要求 = 已到歷史最早
            }
        }

        // 下市/退市幣:REST API 回空(Binance 把下市標的從 live API 拿掉)→ 退回 data.binance.vision
        // 月度 archive(archive 保留下市前的歷史)。修倖存者偏誤的核心:讓宇宙能含「已死的幣」。
        if (allBars.Count == 0)
        {
            var to = DateTime.UtcNow;
            var from = to.AddDays(-(limit + 45));   // 多抓緩衝;不存在的月份自然 404 skip
            allBars = await FetchFromVisionArchive(symbol, interval, from, to);
            if (allBars.Count > limit) allBars = allBars.Skip(allBars.Count - limit).ToList();
        }

        // 序列化存檔(用 Binance kline JSON array format 重組)
        var serialized = ReSerialize(allBars);
        await File.WriteAllTextAsync(path, serialized);
        return allBars;
    }

    /// <summary>
    /// 從 data.binance.vision 月度 archive 抓 klines(REST API 抓不到的下市/退市幣才用)。
    /// 逐月下載 zip → 解 CSV → BarData;不存在的月份(下市後/上市前)404 自動 skip。
    /// CSV 時間戳相容毫秒(舊)與微秒(2025+);相容有無 header 行。
    /// </summary>
    public static async Task<List<BarData>> FetchFromVisionArchive(
        string symbol, string interval, DateTime fromMonth, DateTime toMonth)
    {
        var bars = new List<BarData>();
        for (var m = new DateTime(fromMonth.Year, fromMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
             m <= toMonth; m = m.AddMonths(1))
        {
            var url = $"https://data.binance.vision/data/spot/monthly/klines/{symbol}/{interval}/{symbol}-{interval}-{m:yyyy-MM}.zip";
            try
            {
                var zipBytes = await Http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(zipBytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv"));
                if (entry == null) continue;
                using var sr = new StreamReader(entry.Open());
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    var p = line.Split(',');
                    if (p.Length < 6 || !long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
                        continue;   // header 行或壞行 → skip
                    var dt = t > 1_000_000_000_000_000L                       // >1e15 = 微秒(2025+)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(t / 1000).UtcDateTime
                        : DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime;
                    if (decimal.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var o)
                        && decimal.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                        && decimal.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo)
                        && decimal.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var c)
                        && decimal.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        bars.Add(new BarData { OpenTime = dt, Open = o, High = h, Low = lo, Close = c, Volume = v });
                }
            }
            catch { /* 404(月份不存在)/ 網路 → skip */ }
        }
        return bars.OrderBy(b => b.OpenTime).ToList();
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
