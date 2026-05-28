// Binance perp funding rate 本地快取 + 對齊到 K 線。
//
// 用法:
//   var bars = await KlineCache.FetchOrLoad("BTCUSDT", "1d");
//   await FundingCache.InjectInto(bars, "BTCUSDT", "1d");   // bars[i].FundingRate 變成該根 K 線的累計 funding
//
// 對齊規則:
//   - Binance funding 每 8h 一次(00:00 / 08:00 / 16:00 UTC)
//   - daily bar (1d):取該日 3 次 funding 之和(代表「持有這根 bar 期間的總 funding 成本/收益」)
//   - weekly bar (1w):取該週 21 次 funding 之和
//   - hourly bar (1h / 4h):取該 bar 區間內所有 funding 之和(通常 0 或 1 次)
//
// 正號 funding = 多單付給空單(多單成本、空單收益)
// 負號 funding = 空單付給多單(空單成本、多單收益)
//
// 快取:~/.cache/brick4agent/funding/{SYMBOL}.json,TTL 24h(同 klines)
//
using StrategyWorker.Engine;
using System.Globalization;
using System.Text.Json;

namespace ToolsShared;

public static class FundingCache
{
    private static readonly string CacheDir =
        Environment.GetEnvironmentVariable("FUNDING_CACHE_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "brick4agent", "funding");

    private static readonly bool ForceRefresh =
        Environment.GetEnvironmentVariable("FORCE_REFRESH_FUNDING") == "1";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const int BinanceLimitPerCall = 1000;

    /// <summary>抓 symbol 的 funding 歷史(從 startMs、最多 limit 筆)。limit > 1000 自動分頁。</summary>
    public static async Task<List<(DateTime ts, decimal rate)>> FetchOrLoad(
        string symbol, int limit = 2000, TimeSpan? ttl = null)
    {
        ttl ??= TimeSpan.FromHours(24);
        Directory.CreateDirectory(CacheDir);
        var path = Path.Combine(CacheDir, $"{symbol}-{limit}.json");

        if (!ForceRefresh && File.Exists(path))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age < ttl) return ParseCached(await File.ReadAllTextAsync(path));
        }

        // 分頁抓:Binance fapi/v1/fundingRate quirks(實測):
        //   - `endTime=X` 回時間 ≤ X 的【最舊 1000 個】(從歷史起點開始,反直覺)
        //   - `startTime=X` 回時間 ≥ X 的【最舊 1000 個】(從 X 開始往後 1000)
        //   - 無 startTime/endTime → 只回 200(限制不一致)
        //
        // 策略:使用 startTime forward 分頁,從足夠早的日期開始(覆蓋 limit 事件),每次 startTime = 上次 newest + 1
        // 預估:limit=2000 events / 3 per day = 667 天,所以從 800 天前開始
        int daysBack = Math.Max(800, limit / 3 + 100);
        long startTime = DateTimeOffset.UtcNow.AddDays(-daysBack).ToUnixTimeMilliseconds();
        var all = new List<(DateTime ts, decimal rate)>();
        int callCount = 0;
        while (callCount < 10)   // 10 calls × 1000 = 10000 events 上限(防爆 API)
        {
            var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}&limit={BinanceLimitPerCall}&startTime={startTime}";
            string json;
            try { json = await Http.GetStringAsync(url); callCount++; }
            catch (Exception ex) { Console.WriteLine($"[FundingCache] {symbol}: fetch failed ({ex.Message})、跳過"); break; }
            using var doc = JsonDocument.Parse(json);
            var batch = new List<(DateTime, decimal)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                long ts = el.GetProperty("fundingTime").GetInt64();
                if (!decimal.TryParse(el.GetProperty("fundingRate").GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var r)) continue;
                batch.Add((DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime, r));
            }
            if (batch.Count == 0) break;

            all.AddRange(batch);

            // 下次 startTime = 本批 newest + 1 ms(forward 推進)
            var newestMs = new DateTimeOffset(batch.Max(x => x.Item1), TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (newestMs <= startTime) break;   // 沒往後推進 → 到底
            startTime = newestMs + 1;

            // 已超過現在時間 → 拿到最新、收工
            if (startTime > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) break;
        }

        // de-dup + sort ascending by ts
        all = all.GroupBy(x => x.ts).Select(g => g.First()).OrderBy(x => x.ts).ToList();

        // 序列化存
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(all.Select(x => new {
            fundingTime = new DateTimeOffset(x.ts, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            fundingRate = x.rate.ToString(CultureInfo.InvariantCulture),
        })));
        return all;
    }

    /// <summary>對 bars 注入 funding rate(就地修改 bars[i].FundingRate)。
    /// 規則:每根 bar 累計區間 [bar.OpenTime, nextBar.OpenTime) 內所有 funding 之和。
    /// 最後一根用 bar.OpenTime + interval 估區間。</summary>
    public static async Task InjectInto(List<BarData> bars, string symbol, string interval = "1d")
    {
        if (bars.Count == 0) return;
        // 2026-05-29:funding 抓取深度隨 bar 數自動放大(每日 ~3 events、+buffer),
        // 否則深歷史回測(--bars 2000+)早期 bar 拿不到 funding。FetchOrLoad 的 daysBack 跟著 limit 放大。
        int fundingLimit = Math.Max(2000, bars.Count * 3 + 300);
        var funding = await FetchOrLoad(symbol, limit: fundingLimit);
        if (funding.Count == 0)
        {
            Console.WriteLine($"[FundingCache] {symbol}: no funding data、bars FundingRate 保持 null");
            return;
        }

        // 估每根 bar 的時間長度
        TimeSpan barLen = interval switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "2h" => TimeSpan.FromHours(2),
            "4h" => TimeSpan.FromHours(4),
            "6h" => TimeSpan.FromHours(6),
            "8h" => TimeSpan.FromHours(8),
            "12h" => TimeSpan.FromHours(12),
            "1d" => TimeSpan.FromDays(1),
            "3d" => TimeSpan.FromDays(3),
            "1w" => TimeSpan.FromDays(7),
            "1M" => TimeSpan.FromDays(30),
            _   => TimeSpan.FromDays(1),
        };

        // 預排序確保 funding 是 ascending
        if (funding.Count > 1 && funding[0].ts > funding[1].ts)
            funding.Sort((a, b) => a.ts.CompareTo(b.ts));

        int fIdx = 0;   // funding 指標、跟著 bar 往前走
        for (int i = 0; i < bars.Count; i++)
        {
            var startTs = bars[i].OpenTime;
            var endTs = (i + 1 < bars.Count) ? bars[i + 1].OpenTime : startTs + barLen;

            decimal sum = 0m;
            // 推進 fIdx 到 >= startTs
            while (fIdx < funding.Count && funding[fIdx].ts < startTs) fIdx++;
            // 收集所有 funding ts 落在 [startTs, endTs) 的
            int j = fIdx;
            while (j < funding.Count && funding[j].ts < endTs)
            {
                sum += funding[j].rate;
                j++;
            }

            bars[i].FundingRate = sum;   // 該根 bar 持有期間的累計 funding rate
        }
    }

    private static List<(DateTime ts, decimal rate)> ParseCached(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<(DateTime, decimal)>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            long ts = el.GetProperty("fundingTime").GetInt64();
            var r = decimal.Parse(el.GetProperty("fundingRate").GetString()!, NumberStyles.Any, CultureInfo.InvariantCulture);
            list.Add((DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime, r));
        }
        return list;
    }
}
