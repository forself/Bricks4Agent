using System.Globalization;
using System.Text.Json;
using StrategyWorker.Engine;

namespace ToolsShared;

/// <summary>
/// CFTC Commitments of Traders（COT）legacy futures-only 持倉快取（Socrata JSON API）。
///
/// 結構性部位訊號:投機者(non-commercial)淨持倉 % OI = (noncomm_long - noncomm_short)/open_interest。
/// 極端淨多 = 投機者擁擠 → 反向(商業避險者/聰明錢站對面);學術:hedger 預測力 > 投機者(Bessembinder & Chan 1992)。
/// 跟價格 TA 天生去相關 → 結構性 alpha 候選(商品/FX/股指期)。
///
/// 資料:https://publicreporting.cftc.gov/resource/6dca-aqww.json(週報、回溯 1986)。
/// 無 lookahead 注入:COT 週二資料週五公布 → 用 reportDate+5天 ≤ barDate 的最新一筆。
/// </summary>
public static class COTCache
{
    private static readonly string CacheDir =
        Environment.GetEnvironmentVariable("COT_CACHE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "brick4agent", "cot");

    private static readonly HttpClient Http = MakeHttp();
    private static HttpClient MakeHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (b4a-research)");
        return h;
    }

    /// <summary>回傳 (reportDate, specNetPct) 週序列(升冪)。找不到 → 空。</summary>
    public static async Task<List<(DateTime date, decimal specNetPct)>> FetchOrLoad(string keyword)
    {
        Directory.CreateDirectory(CacheDir);
        var safe = string.Concat(keyword.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(CacheDir, $"{safe}.json");
        if (File.Exists(path))
        {
            try { return ParseCache(await File.ReadAllTextAsync(path)); } catch { }
        }

        string where = $"market_and_exchange_names like '%{keyword}%'";
        string url = $"https://publicreporting.cftc.gov/resource/6dca-aqww.json?$where={Uri.EscapeDataString(where)}&$limit=20000&$order=report_date_as_yyyy_mm_dd";
        string json;
        try { json = await Http.GetStringAsync(url); }
        catch { return new(); }

        var byMarket = new Dictionary<string, List<(DateTime d, decimal spec, decimal oi)>>();
        using (var doc = JsonDocument.Parse(json))
        {
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string mkt = Str(el, "market_and_exchange_names");
                if (!DateTime.TryParse(Str(el, "report_date_as_yyyy_mm_dd"), out var d)) continue;
                decimal oi = Dec(el, "open_interest_all");
                if (oi <= 0m) continue;
                decimal spec = (Dec(el, "noncomm_positions_long_all") - Dec(el, "noncomm_positions_short_all")) / oi;
                if (!byMarket.TryGetValue(mkt, out var l)) byMarket[mkt] = l = new();
                l.Add((d.Date, spec, oi));
            }
        }
        if (byMarket.Count == 0) return new();

        // 多市場匹配 → 選平均 OI 最大的主力合約(e.g. GOLD > MICRO GOLD)
        var main = byMarket.OrderByDescending(kv => kv.Value.Average(x => x.oi)).First().Value;
        var series = main.OrderBy(x => x.d).Select(x => (x.d, x.spec)).ToList();
        try { await File.WriteAllTextAsync(path, Serialize(series)); } catch { }
        return series;
    }

    /// <summary>注入 CotSpecNet 到 bars(無 lookahead:reportDate+5天 ≤ barDate 的最新一筆)。回傳命中根數。</summary>
    public static async Task<int> InjectInto(List<BarData> bars, string keyword)
    {
        var cot = await FetchOrLoad(keyword);
        if (cot.Count == 0 || bars.Count == 0) return 0;
        int ci = 0, hit = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            var bd = bars[i].OpenTime.Date;
            while (ci + 1 < cot.Count && cot[ci + 1].date.AddDays(5) <= bd) ci++;
            if (cot[ci].date.AddDays(5) <= bd) { bars[i].CotSpecNet = cot[ci].specNetPct; hit++; }
        }
        return hit;
    }

    private static string Str(JsonElement el, string f) => el.TryGetProperty(f, out var v) ? (v.GetString() ?? "") : "";
    private static decimal Dec(JsonElement el, string f) =>
        decimal.TryParse(Str(el, f), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string Serialize(List<(DateTime d, decimal spec)> s) =>
        JsonSerializer.Serialize(s.Select(x => new { d = x.d.ToString("yyyy-MM-dd"), s = x.spec.ToString(CultureInfo.InvariantCulture) }));

    private static List<(DateTime, decimal)> ParseCache(string json)
    {
        var r = new List<(DateTime, decimal)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
            if (DateTime.TryParse(el.GetProperty("d").GetString(), out var d))
                r.Add((d, decimal.TryParse(el.GetProperty("s").GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m));
        return r;
    }
}
