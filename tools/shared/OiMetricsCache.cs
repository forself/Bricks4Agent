// Binance perp open interest + L/S ratio + taker volume 本地快取(從 data.binance.vision daily ZIP CSV)
//
// 為什麼不用 /futures/data/openInterestHist API?
//   - 公開 API 只回 30 天歷史、t-stat 不夠樣本
//   - data.binance.vision 提供 5min 粒度、回溯 2-3 年(2022+)、足夠 backfill
//
// CSV 欄位(5min 粒度、每天一個 zip ~11KB):
//   create_time, symbol, sum_open_interest, sum_open_interest_value,
//   count_toptrader_long_short_ratio, sum_toptrader_long_short_ratio,
//   count_long_short_ratio, sum_taker_long_short_vol_ratio
//
// 用法:
//   var snaps = await OiMetricsCache.FetchOrLoad("BTCUSDT", DateTime.UtcNow.AddDays(-365), DateTime.UtcNow);
//   // snaps 是 5min 粒度 List<OiSnapshot>、之後可自行 aggregate 到 daily / hourly
//
// 快取:~/.cache/brick4agent/oi-metrics/{SYMBOL}/{YYYY-MM-DD}.json,單檔不 expire(歷史資料不變)

using StrategyWorker.Engine;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace ToolsShared;

public sealed record OiSnapshot(
    DateTime Ts,
    decimal SumOpenInterest,
    decimal SumOpenInterestValue,
    decimal CountToptraderLsRatio,
    decimal SumToptraderLsRatio,
    decimal CountLsRatio,
    decimal SumTakerLsVolRatio);

public static class OiMetricsCache
{
    private static readonly string CacheDir =
        Environment.GetEnvironmentVariable("OI_CACHE_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "brick4agent", "oi-metrics");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// 抓 symbol 在 [startUtc, endUtc] 區間的所有 5min OI snapshots。
    /// 自動 day-by-day 抓 zip CSV、cache 到本地、之後直接讀 cache。
    /// </summary>
    public static async Task<List<OiSnapshot>> FetchOrLoad(string symbol, DateTime startUtc, DateTime endUtc)
    {
        var symDir = Path.Combine(CacheDir, symbol);
        Directory.CreateDirectory(symDir);

        var all = new List<OiSnapshot>();
        for (var day = startUtc.Date; day <= endUtc.Date; day = day.AddDays(1))
        {
            var daySnaps = await FetchOrLoadDay(symbol, day, symDir);
            all.AddRange(daySnaps);
        }
        return all.OrderBy(s => s.Ts).ToList();
    }

    private static async Task<List<OiSnapshot>> FetchOrLoadDay(string symbol, DateTime day, string symDir)
    {
        string dayStr = day.ToString("yyyy-MM-dd");
        string cachePath = Path.Combine(symDir, $"{dayStr}.json");

        if (File.Exists(cachePath))
        {
            try { return ParseJsonCache(await File.ReadAllTextAsync(cachePath)); }
            catch { /* corrupt cache、重抓 */ }
        }

        string url = $"https://data.binance.vision/data/futures/um/daily/metrics/{symbol}/{symbol}-metrics-{dayStr}.zip";
        byte[] zipBytes;
        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                // 寫空 cache 防一直重抓(該日資料可能不存在)
                await File.WriteAllTextAsync(cachePath, "[]");
                return new();
            }
            zipBytes = await resp.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OiMetricsCache] {symbol} {dayStr}: fetch failed ({ex.Message})、skip");
            return new();
        }

        var snaps = ParseZipCsv(zipBytes);
        await File.WriteAllTextAsync(cachePath, SerializeJsonCache(snaps));
        return snaps;
    }

    private static List<OiSnapshot> ParseZipCsv(byte[] zipBytes)
    {
        var result = new List<OiSnapshot>();
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv"));
        if (entry == null) return result;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        string? header = reader.ReadLine();
        if (header == null) return result;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length < 8) continue;
            try
            {
                var ts = DateTime.ParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                result.Add(new OiSnapshot(
                    Ts: ts,
                    SumOpenInterest: ParseDec(parts[2]),
                    SumOpenInterestValue: ParseDec(parts[3]),
                    CountToptraderLsRatio: ParseDec(parts[4]),
                    SumToptraderLsRatio: ParseDec(parts[5]),
                    CountLsRatio: ParseDec(parts[6]),
                    SumTakerLsVolRatio: ParseDec(parts[7])
                ));
            }
            catch { /* skip malformed row */ }
        }
        return result;
    }

    private static decimal ParseDec(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string SerializeJsonCache(List<OiSnapshot> snaps) =>
        JsonSerializer.Serialize(snaps.Select(s => new
        {
            t = new DateTimeOffset(s.Ts, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            oi = s.SumOpenInterest.ToString(CultureInfo.InvariantCulture),
            oiv = s.SumOpenInterestValue.ToString(CultureInfo.InvariantCulture),
            ctls = s.CountToptraderLsRatio.ToString(CultureInfo.InvariantCulture),
            stls = s.SumToptraderLsRatio.ToString(CultureInfo.InvariantCulture),
            cls = s.CountLsRatio.ToString(CultureInfo.InvariantCulture),
            tls = s.SumTakerLsVolRatio.ToString(CultureInfo.InvariantCulture),
        }));

    private static List<OiSnapshot> ParseJsonCache(string json)
    {
        var result = new List<OiSnapshot>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            long tMs = el.GetProperty("t").GetInt64();
            result.Add(new OiSnapshot(
                Ts: DateTimeOffset.FromUnixTimeMilliseconds(tMs).UtcDateTime,
                SumOpenInterest: ParseDec(el.GetProperty("oi").GetString() ?? "0"),
                SumOpenInterestValue: ParseDec(el.GetProperty("oiv").GetString() ?? "0"),
                CountToptraderLsRatio: ParseDec(el.GetProperty("ctls").GetString() ?? "0"),
                SumToptraderLsRatio: ParseDec(el.GetProperty("stls").GetString() ?? "0"),
                CountLsRatio: ParseDec(el.GetProperty("cls").GetString() ?? "0"),
                SumTakerLsVolRatio: ParseDec(el.GetProperty("tls").GetString() ?? "0")
            ));
        }
        return result;
    }

    /// <summary>
    /// 把 OI / Retail L/S 注入到 bars(就地修改 bars[i] 的 OpenInterest + RetailLongShortRatio)。
    /// 規則:bar 區間 [OpenTime, nextBar.OpenTime) 內所有 5min snapshot 的均值。
    /// 已有 OI 的不覆寫(由 quote-worker 寫的真實 OI 優先)。
    /// </summary>
    public static async Task InjectInto(List<BarData> bars, string symbol, string interval = "1d")
    {
        if (bars.Count == 0) return;
        var startUtc = bars[0].OpenTime.AddDays(-1);
        var endUtc = bars[^1].OpenTime.AddDays(1);
        var snaps = await FetchOrLoad(symbol, startUtc, endUtc);
        if (snaps.Count == 0) return;

        TimeSpan barLen = interval switch
        {
            "1m" => TimeSpan.FromMinutes(1), "5m" => TimeSpan.FromMinutes(5), "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30), "1h" => TimeSpan.FromHours(1), "4h" => TimeSpan.FromHours(4),
            "1d" => TimeSpan.FromDays(1), "1w" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(1),
        };

        int sIdx = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            var startTs = bars[i].OpenTime;
            var endTs = (i + 1 < bars.Count) ? bars[i + 1].OpenTime : startTs + barLen;

            while (sIdx < snaps.Count && snaps[sIdx].Ts < startTs) sIdx++;
            decimal oiSum = 0m, lsSum = 0m;
            int n = 0;
            int j = sIdx;
            while (j < snaps.Count && snaps[j].Ts < endTs)
            {
                oiSum += snaps[j].SumOpenInterestValue;
                lsSum += snaps[j].CountLsRatio;
                n++;
                j++;
            }
            if (n > 0)
            {
                bars[i].OpenInterest ??= oiSum / n;
                bars[i].RetailLongShortRatio = lsSum / n;
            }
        }
    }

    /// <summary>
    /// Aggregate 5min snapshots → daily(取每日最後一筆 OI 作 EOD,各 ratio 取均值)。
    /// </summary>
    public static List<(DateTime Day, decimal OiValueEod, decimal OiPctChange, decimal AvgTopLsRatio, decimal AvgRetailLsRatio, decimal AvgTakerLsRatio)>
        AggregateDaily(List<OiSnapshot> snaps)
    {
        var byDay = snaps.GroupBy(s => s.Ts.Date).OrderBy(g => g.Key).ToList();
        var result = new List<(DateTime, decimal, decimal, decimal, decimal, decimal)>();
        decimal? prevOi = null;
        foreach (var g in byDay)
        {
            var list = g.OrderBy(s => s.Ts).ToList();
            if (list.Count == 0) continue;
            var eod = list[^1];
            decimal oiPct = prevOi is decimal p && p > 0m
                ? (eod.SumOpenInterestValue - p) / p
                : 0m;
            result.Add((
                g.Key,
                eod.SumOpenInterestValue,
                oiPct,
                list.Average(s => s.SumToptraderLsRatio),
                list.Average(s => s.CountLsRatio),
                list.Average(s => s.SumTakerLsVolRatio)
            ));
            prevOi = eod.SumOpenInterestValue;
        }
        return result;
    }
}
