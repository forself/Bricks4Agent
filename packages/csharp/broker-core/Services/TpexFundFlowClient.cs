using System.Globalization;
using System.Text.Json;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 上櫃(TPEx)資金流抓取 + 解析(2026-06-05)。三大法人 + 收盤 + 產業別,給「各產業資金流」併入上櫃用。
///
/// 端點(TPEx OpenAPI、只回最新交易日、無 date 參數 → 用回傳的 Date 跟報表日比對):
///   - 三大法人:/openapi/v1/tpex_3insti_daily_trading(欄名亂、外資用 total−trust−dealer 反推最穩)
///   - 收盤:    /openapi/v1/tpex_mainboard_daily_close_quotes(Close 欄)
///   - 產業別:  /openapi/v1/mopsfin_t187ap03_O(SecuritiesCompanyCode + SecuritiesIndustryCode)
/// 單位:股(同 TWSE);產業代碼沿用 TwseFundFlowClient.IndustryName。Date 是民國 yyyMMdd(1150605=2026-06-05)。
/// 上櫃僅在報表「當日」併入(in-memory、不存 DB)→ 失敗不致命、只少上櫃那段。
/// </summary>
public static class TpexFundFlowClient
{
    private const string InstUrl = "https://www.tpex.org.tw/openapi/v1/tpex_3insti_daily_trading";
    private const string CloseUrl = "https://www.tpex.org.tw/openapi/v1/tpex_mainboard_daily_close_quotes";
    private const string IndustryUrl = "https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap03_O";

    public record TpexResult(string Date, List<TwFundFlowDaily> Rows, Dictionary<string, decimal> Closes);

    /// <summary>抓上櫃三大法人 + 收盤,回 (isoDate, rows, closes)。產業另抓(FetchIndustryMapAsync)。失敗回空。</summary>
    public static async Task<TpexResult> FetchAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            var (date, rows) = ParseInstitutional(await http.GetStringAsync(InstUrl, ct));
            if (rows.Count == 0) return new("", new(), new());
            var closes = ParseCloses(await http.GetStringAsync(CloseUrl, ct));
            return new(date, rows, closes);
        }
        catch { return new("", new(), new()); }
    }

    /// <summary>抓上櫃個股→產業名對照(mopsfin_t187ap03_O)。失敗回空。</summary>
    public static async Task<Dictionary<string, string>> FetchIndustryMapAsync(HttpClient http, CancellationToken ct = default)
    {
        try { return ParseIndustryMap(await http.GetStringAsync(IndustryUrl, ct)); }
        catch { return new(); }
    }

    /// <summary>解析上櫃三大法人 → (isoDate, rows)。外資 = TotalDifference − 投信 − 自營(避開亂空格外資欄名)。純函式。</summary>
    public static (string Date, List<TwFundFlowDaily> Rows) ParseInstitutional(string json)
    {
        var result = new List<TwFundFlowDaily>();
        string iso = "";
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return ("", result);
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            string code = Str(r, "SecuritiesCompanyCode");
            if (!TwseFundFlowClient.IsCommonStock(code)) continue;
            if (iso == "") iso = RocToIso(Str(r, "Date"));
            long trust = Num(r, "SecuritiesInvestmentTrustCompanies-Difference");
            long dealer = Num(r, "Dealers-Difference");
            long total = Num(r, "TotalDifference");
            result.Add(new TwFundFlowDaily
            {
                EntryKey = $"{iso}:{code}",
                TradeDate = iso,
                StockCode = code,
                StockName = Str(r, "CompanyName"),
                ForeignNet = total - trust - dealer,   // 外資 = 合計 − 投信 − 自營
                TrustNet = trust,
                DealerNet = dealer,
                TotalNet = total,
            });
        }
        return (iso, result);
    }

    /// <summary>解析上櫃收盤 → code→close(Close 欄、只留普通個股)。純函式。</summary>
    public static Dictionary<string, decimal> ParseCloses(string json)
    {
        var map = new Dictionary<string, decimal>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            string code = Str(r, "SecuritiesCompanyCode");
            if (!TwseFundFlowClient.IsCommonStock(code)) continue;
            var close = Dec(r, "Close");
            if (close > 0) map[code] = close;
        }
        return map;
    }

    /// <summary>解析上櫃基本資料 → code→中文產業名(SecuritiesIndustryCode 經 IndustryName)。純函式。</summary>
    public static Dictionary<string, string> ParseIndustryMap(string json)
    {
        var map = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object) continue;
            string code = Str(c, "SecuritiesCompanyCode");
            string ind = Str(c, "SecuritiesIndustryCode");
            if (TwseFundFlowClient.IsCommonStock(code) && ind.Length > 0)
                map[code] = TwseFundFlowClient.IndustryName(ind);
        }
        return map;
    }

    /// <summary>民國 yyyMMdd(如 1150605)→ "2026-06-05"。格式不符回空。</summary>
    public static string RocToIso(string roc)
    {
        roc = (roc ?? "").Trim();
        if (roc.Length != 7 || !roc.All(char.IsDigit)) return "";
        int y = int.Parse(roc[..3]) + 1911;
        return $"{y:D4}-{roc.Substring(3, 2)}-{roc.Substring(5, 2)}";
    }

    private static string Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static long Num(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) ? TwseFundFlowClient.Num(v) : 0L;

    private static decimal Dec(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) ? TwseFundFlowClient.NumDec(v) : 0m;
}
