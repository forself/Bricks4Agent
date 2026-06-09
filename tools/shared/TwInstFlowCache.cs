// 歷史 TWSE T86 三大法人買賣超 快取(Step 4 資金流×波動選股用)。
// 每日 ~2.5MB ~1000 檔;cache per-date(含非交易日的空回應、避免重抓)。回 code → (外資淨股, 三大法人淨股)。
// 欄位(2026-06-10 核對):[0]代號 [4]外陸資淨 [7]外資自營淨 [10]投信淨 [11]自營淨 [18]三大法人淨。外資=[4]+[7]。
// TWSE 免費無 key;禮貌延遲 400ms 避免被擋。歷史回溯到 ~2022(含熊市)。
using System.Globalization;
using System.Text.Json;

namespace ToolsShared;

public static class TwInstFlowCache
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "brick4agent", "twflow");
    private static readonly HttpClient Http = MakeHttp();
    private const string Url = "https://www.twse.com.tw/rwd/zh/fund/T86?date={0}&selectType=ALL&response=json";

    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        // TWSE 擋無 UA 的請求 → 帶瀏覽器 UA(curl/PowerShell 有帶才成功)
        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return h;
    }

    /// <summary>抓某日 T86 → code → (外資淨股, 三大法人淨股)。非交易日/抓失敗 → 空 dict。Cache per-date。</summary>
    public static async Task<Dictionary<string, (long fgn, long tot)>> FetchDay(DateTime d)
    {
        Directory.CreateDirectory(Dir);
        var ymd = d.ToString("yyyyMMdd");
        var path = Path.Combine(Dir, $"t86-{ymd}.json");
        string json;
        if (File.Exists(path))
            json = await File.ReadAllTextAsync(path);
        else
        {
            try { json = await Http.GetStringAsync(string.Format(Url, ymd)); }
            catch { return new(); }   // 抓失敗(網路/被擋)不快取、下次重試(避免把失敗 cache 成空)
            await File.WriteAllTextAsync(path, json);   // 只在成功抓到才快取(含非交易日的合法空回應)
            await Task.Delay(1000);   // 禮貌延遲(TWSE ~2s 不擋、1s 保險;失敗才慢)
        }
        return Parse(json);
    }

    /// <summary>讀所有已快取的 T86(可由 curl 預抓填入)→ 累計每股外資淨股 + 有資料的交易日數。
    /// 純讀快取、不抓網路(繞開 C# HttpClient 對 TWSE 不穩的問題;由外部 curl 可靠預抓)。</summary>
    public static (Dictionary<string, long> foreignSum, int tradingDays) SumAllCached()
    {
        var sum = new Dictionary<string, long>();
        int days = 0;
        if (!Directory.Exists(Dir)) return (sum, 0);
        foreach (var f in Directory.GetFiles(Dir, "t86-*.json"))
        {
            Dictionary<string, (long fgn, long tot)> day;
            try { day = Parse(File.ReadAllText(f)); } catch { continue; }
            if (day.Count == 0) continue;
            days++;
            foreach (var kv in day) { sum.TryGetValue(kv.Key, out var c); sum[kv.Key] = c + kv.Value.fgn; }
        }
        return (sum, days);
    }

    private static Dictionary<string, (long, long)> Parse(string json)
    {
        var outp = new Dictionary<string, (long, long)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return outp;
            foreach (var row in data.EnumerateArray())
            {
                if (row.GetArrayLength() < 19) continue;
                var code = (row[0].GetString() ?? "").Trim();
                if (code.Length != 4 || !code.All(char.IsDigit)) continue;
                outp[code] = (L(row[4]) + L(row[7]), L(row[18]));   // 外資淨 = [4]+[7];三大法人淨 = [18]
            }
        }
        catch { }
        return outp;
    }

    private static long L(JsonElement e)
    {
        var s = (e.GetString() ?? "0").Replace(",", "").Trim();
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
