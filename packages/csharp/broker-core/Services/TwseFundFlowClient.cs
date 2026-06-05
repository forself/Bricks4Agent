using System.Globalization;
using System.Text.Json;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// TWSE 台股資金流抓取 + 解析(2026-06-04)。三大法人(T86)+ 融資融券(MI_MARGN)。
///
/// 解析邏輯抽成 static 純函式(可單元測試、不碰網路);Fetch* 走傳入的 HttpClient。
/// 欄位 index 已用真實 2330(台積電)2026-06-03 資料核對(見 ParseT86 / ParseMargin 註解)。
/// 非交易日 TWSE 回 stat != "OK"(「很抱歉,沒有符合條件的資料!」)→ ParseT86 回空 → 上層判定無資料。
/// </summary>
public static class TwseFundFlowClient
{
    private const string T86Url =
        "https://www.twse.com.tw/rwd/zh/fund/T86?date={0}&selectType=ALL&response=json";
    private const string MarginUrl =
        "https://www.twse.com.tw/rwd/zh/marginTrading/MI_MARGN?date={0}&selectType=ALL&response=json";
    // STOCK_DAY_ALL = 全上市個股「最近一個交易日」OHLC(只回最新日、無歷史)。算買賣超金額(億)用。
    private const string StockDayAllUrl =
        "https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY_ALL?response=json";
    // MI_INDEX = 「指定日期」全市場個股 OHLC(type=ALLBUT0999)。根治「張↔億元跳動」:報表用報表日自己的收盤、
    // 不靠 STOCK_DAY_ALL 的「只回最新日」→ 任何報表日(含 backfill 歷史日)都拿得到當日收盤、穩定顯示億元。
    private const string MiIndexUrl =
        "https://www.twse.com.tw/rwd/zh/afterTrading/MI_INDEX?date={0}&type=ALLBUT0999&response=json";
    // 上市公司基本資料(含產業別代碼)。給「按產業彙總資金流」用 — 個股 → 產業別代碼 → 中文產業名。
    private const string IndustryUrl = "https://openapi.twse.com.tw/v1/opendata/t187ap03_L";

    // TWSE 標準產業別代碼 → 中文名(穩定、少變)。未知代碼 → IndustryName 回「產業{code}」。
    private static readonly Dictionary<string, string> IndustryNames = new()
    {
        ["01"] = "水泥", ["02"] = "食品", ["03"] = "塑膠", ["04"] = "紡織纖維", ["05"] = "電機機械",
        ["06"] = "電器電纜", ["08"] = "玻璃陶瓷", ["09"] = "造紙", ["10"] = "鋼鐵", ["11"] = "橡膠",
        ["12"] = "汽車", ["14"] = "建材營造", ["15"] = "航運", ["16"] = "觀光餐旅", ["17"] = "金融保險",
        ["18"] = "貿易百貨", ["20"] = "其他", ["21"] = "化學", ["22"] = "生技醫療", ["23"] = "油電燃氣",
        ["24"] = "半導體", ["25"] = "電腦及週邊", ["26"] = "光電", ["27"] = "通信網路", ["28"] = "電子零組件",
        ["29"] = "電子通路", ["30"] = "資訊服務", ["31"] = "其他電子", ["32"] = "文化創意", ["33"] = "農業科技",
        ["35"] = "綠能環保", ["36"] = "數位雲端", ["37"] = "運動休閒", ["38"] = "居家生活",
        ["80"] = "管理股票", ["91"] = "存託憑證",
    };

    /// <summary>
    /// 抓某 TST 日期的台股資金流。回 (hasData, rows)。
    /// 非交易日 / 資料未發布 → (false, [])。融資融券抓不到不致命(只少一個維度、三大法人照回)。
    /// </summary>
    public static async Task<(bool HasData, List<TwFundFlowDaily> Rows)> FetchDayAsync(
        HttpClient http, DateTime tstDate, CancellationToken ct = default)
    {
        string ymd = tstDate.ToString("yyyyMMdd");
        string isoDate = tstDate.ToString("yyyy-MM-dd");

        string t86Json;
        try { t86Json = await http.GetStringAsync(string.Format(T86Url, ymd), ct); }
        catch { return (false, new()); }

        var rows = ParseT86(t86Json, isoDate);
        if (rows.Count == 0) return (false, new());   // 非交易日 / 無資料

        // 融資融券(失敗不致命)
        try
        {
            var marginJson = await http.GetStringAsync(string.Format(MarginUrl, ymd), ct);
            var margin = ParseMargin(marginJson);
            foreach (var r in rows)
                if (margin.TryGetValue(r.StockCode, out var m))
                {
                    r.MarginPrev = m.MarginPrev; r.MarginBalance = m.MarginBalance;
                    r.ShortPrev = m.ShortPrev; r.ShortBalance = m.ShortBalance;
                }
        }
        catch { /* 融資融券抓不到、三大法人照存 */ }

        return (true, rows);
    }

    /// <summary>
    /// 解析 T86 三大法人買賣超 JSON → 個股列(只留 4 位數字代號)。純函式、可測。
    /// T86 欄位(已核對 2330):[0]代號 [1]名稱 [4]外陸資買賣超 [7]外資自營商買賣超
    ///   [10]投信買賣超 [11]自營商買賣超 [18]三大法人買賣超。單位:股。
    /// 外資合計 = [4] + [7](TWSE 慣例);驗證 外資+投信+自營 == [18]。
    /// </summary>
    public static List<TwFundFlowDaily> ParseT86(string json, string isoDate)
    {
        var result = new List<TwFundFlowDaily>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK") return result;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;

        foreach (var r in data.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 19) continue;
            string code = (r[0].GetString() ?? "").Trim();
            if (!IsCommonStock(code)) continue;   // 濾權證/牛熊證/特殊;留 4 位數普通股 + 主要 ETF
            result.Add(new TwFundFlowDaily
            {
                EntryKey = $"{isoDate}:{code}",
                TradeDate = isoDate,
                StockCode = code,
                StockName = (r[1].GetString() ?? "").Trim(),
                ForeignNet = Num(r[4]) + Num(r[7]),   // 外陸資 + 外資自營商
                TrustNet = Num(r[10]),
                DealerNet = Num(r[11]),
                TotalNet = Num(r[18]),
            });
        }
        return result;
    }

    /// <summary>
    /// 解析 MI_MARGN(table[1] = 個股融資融券彙總)→ code → 餘額。純函式、可測。
    /// 已核對 2330:[5]融資前日餘額 [6]融資今日餘額 [11]融券前日餘額 [12]融券今日餘額。單位:張。
    /// MI_MARGN 是多表(tables[]):table[0]=市場信用統計、table[1]=個股彙總。靠「row[0]=4位數代號」鎖定個股表。
    /// </summary>
    public static Dictionary<string, (long MarginBalance, long MarginPrev, long ShortBalance, long ShortPrev)> ParseMargin(string json)
    {
        var map = new Dictionary<string, (long, long, long, long)>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK") return map;
        if (!root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Array) return map;

        foreach (var t in tables.EnumerateArray())
        {
            if (!t.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
            if (!t.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array
                || fields.GetArrayLength() < 14) continue;   // 個股彙總表 16 欄;市場統計表欄少、跳過
            foreach (var r in data.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 13) continue;
                string code = (r[0].GetString() ?? "").Trim();
                if (!IsCommonStock(code)) continue;
                map[code] = (Num(r[6]), Num(r[5]), Num(r[12]), Num(r[11]));
            }
        }
        return map;
    }

    /// <summary>
    /// 抓全個股「最近交易日」收盤價(STOCK_DAY_ALL)。回 (date, code→close)。失敗回 ("", 空)。
    /// 只回最新日、無歷史 → 只給「當日報表」算金額用;backfill 歷史日無收盤(金額退回張數)。
    /// </summary>
    public static async Task<(string Date, Dictionary<string, decimal> Closes)> FetchClosesAsync(
        HttpClient http, CancellationToken ct = default)
    {
        try
        {
            var json = await http.GetStringAsync(StockDayAllUrl, ct);
            return ParseStockDayAll(json);
        }
        catch { return ("", new()); }
    }

    /// <summary>
    /// 解析 STOCK_DAY_ALL → (date, code→收盤價)。純函式、可測。
    /// 欄位(已核對 2330):[0]代號 [1]名稱 [7]收盤價;停牌/無成交收盤為 "--" → skip。
    /// </summary>
    public static (string Date, Dictionary<string, decimal> Closes) ParseStockDayAll(string json)
    {
        var map = new Dictionary<string, decimal>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK") return ("", map);
        string date = root.TryGetProperty("date", out var dt) ? (dt.GetString() ?? "") : "";
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return (date, map);

        foreach (var r in data.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 8) continue;
            string code = (r[0].GetString() ?? "").Trim();
            if (!IsCommonStock(code)) continue;
            var close = NumDec(r[7]);
            if (close > 0) map[code] = close;
        }
        return (date, map);
    }

    /// <summary>
    /// 抓「指定 TST 日期」全市場個股收盤(MI_INDEX)。回 code→close。失敗/非交易日回空。
    /// 用報表日自己的收盤 → 根治「張↔億元跳動」(不靠 STOCK_DAY_ALL 的只回最新日)。
    /// </summary>
    public static async Task<Dictionary<string, decimal>> FetchClosesForDateAsync(
        HttpClient http, DateTime tstDate, CancellationToken ct = default)
    {
        try
        {
            var json = await http.GetStringAsync(string.Format(MiIndexUrl, tstDate.ToString("yyyyMMdd")), ct);
            return ParseMiIndex(json).Closes;
        }
        catch { return new(); }
    }

    /// <summary>
    /// 解析 MI_INDEX(type=ALLBUT0999)→ (date, code→收盤價)。純函式、可測。
    /// MI_INDEX 是多表(指數表 + 個股表),且頂層常「無 stat」直接給 tables。
    /// 用「欄位名」定位個股表(fields 同含「證券代號」+「收盤價」),避開指數表的「收盤指數」、且不寫死 index。
    /// 漲跌欄含 HTML(&lt;p style…&gt;)是字串內容、不影響;收盤價形如 "2,425.00" 由 NumDec 處理。
    /// </summary>
    public static (string Date, Dictionary<string, decimal> Closes) ParseMiIndex(string json)
    {
        var map = new Dictionary<string, decimal>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string date = root.TryGetProperty("date", out var dt) ? (dt.GetString() ?? "") : "";
        if (!root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Array)
            return (date, map);   // 非交易日 / 結構異常 → 空

        foreach (var t in tables.EnumerateArray())
        {
            if (!t.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array) continue;
            int codeIdx = -1, closeIdx = -1, i = 0;
            foreach (var f in fields.EnumerateArray())
            {
                var name = f.GetString() ?? "";
                if (name.Contains("證券代號")) codeIdx = i;
                else if (name == "收盤價") closeIdx = i;   // 精確比對:排除指數表的「收盤指數」
                i++;
            }
            if (codeIdx < 0 || closeIdx < 0) continue;   // 非個股表(如指數表)→ 跳過
            if (!t.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
            foreach (var r in data.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() <= Math.Max(codeIdx, closeIdx)) continue;
                string code = (r[codeIdx].GetString() ?? "").Trim();
                if (!IsCommonStock(code)) continue;
                var close = NumDec(r[closeIdx]);
                if (close > 0) map[code] = close;
            }
        }
        return (date, map);
    }

    /// <summary>產業別代碼 → 中文產業名(未知 → 「產業{code}」)。純函式。</summary>
    public static string IndustryName(string code)
    {
        code = (code ?? "").Trim();
        return IndustryNames.TryGetValue(code, out var n) ? n : $"產業{code}";
    }

    /// <summary>抓「個股 → 中文產業名」對照(上市公司基本資料 t187ap03_L)。失敗回空。給按產業彙總資金流用。</summary>
    public static async Task<Dictionary<string, string>> FetchIndustryMapAsync(HttpClient http, CancellationToken ct = default)
    {
        try { return ParseIndustryMap(await http.GetStringAsync(IndustryUrl, ct)); }
        catch { return new(); }
    }

    /// <summary>解析 t187ap03_L → code → 中文產業名(只留普通個股、產業代碼轉中文)。純函式、可測。</summary>
    public static Dictionary<string, string> ParseIndustryMap(string json)
    {
        var map = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object) continue;
            string code = c.TryGetProperty("公司代號", out var cd) ? (cd.GetString() ?? "").Trim() : "";
            string ind = c.TryGetProperty("產業別", out var id) ? (id.GetString() ?? "").Trim() : "";
            if (IsCommonStock(code) && ind.Length > 0) map[code] = IndustryName(ind);
        }
        return map;
    }

    /// <summary>普通個股 + 主要 ETF:4 位數字代號(濾掉 6 位權證、含字母的特殊商品如 00403A)。</summary>
    public static bool IsCommonStock(string code) =>
        code.Length == 4 && code.All(char.IsDigit);

    /// <summary>TWSE 數字字串 → long:去千分位逗號、處理 "--"/空白/負號。解析失敗回 0。</summary>
    public static long Num(JsonElement el)
    {
        string? s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Replace(",", "").Replace(" ", "").Trim();
        if (s is "" or "--" or "-" or "---") return 0;
        return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>TWSE 數字字串 → decimal(價格用):去逗號、"--"/空白 → 0。</summary>
    public static decimal NumDec(JsonElement el)
    {
        string? s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        if (string.IsNullOrWhiteSpace(s)) return 0m;
        s = s.Replace(",", "").Replace(" ", "").Trim();
        if (s is "" or "--" or "-" or "---") return 0m;
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
