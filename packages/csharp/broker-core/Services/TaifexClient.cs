using System.Text.Json;

namespace BrokerCore.Services;

/// <summary>
/// 期貨(TAIFEX)三大法人未平倉抓取 + 解析(2026-06-05)。取「外資及陸資 × 臺股期貨」的未平倉淨額 = 大盤情緒。
///
/// 端點:/v1/MarketDataOfMajorInstitutionalTradersDetailsOfFuturesContractsBytheDate(只回最新已結算日、西元 yyyyMMdd)。
/// OpenInterest(Net) = 未平倉淨口數(正=淨多/負=淨空);ContractValueofOpenInterest(Net)(Thousands) = 淨契約金額(千元)。
/// 期貨結算 EOD 才出 → 可能比現貨報表日晚一天;故帶自己的資料日、不跟報表日強制比對。失敗回 null(略過大盤情緒段)。
/// </summary>
public static class TaifexClient
{
    private const string FutOiUrl =
        "https://openapi.taifex.com.tw/v1/MarketDataOfMajorInstitutionalTradersDetailsOfFuturesContractsBytheDate";

    /// <summary>外資臺股期貨未平倉:資料日 + 淨口數 + 淨契約金額(億)。</summary>
    public record FuturesSentiment(string Date, long ForeignTxNetOi, decimal ForeignTxNetYi);

    public static async Task<FuturesSentiment?> FetchForeignTxOiAsync(HttpClient http, CancellationToken ct = default)
    {
        try { return ParseForeignTxOi(await http.GetStringAsync(FutOiUrl, ct)); }
        catch { return null; }
    }

    /// <summary>解析 → 外資及陸資 在「臺股期貨」的未平倉淨口數 + 淨契約金額(億)。找不到回 null。純函式。</summary>
    public static FuturesSentiment? ParseForeignTxOi(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            if (Str(r, "ContractCode") != "臺股期貨") continue;
            if (!Str(r, "Item").StartsWith("外資")) continue;   // 「外資及陸資」
            long netOi = Num(r, "OpenInterest(Net)");
            long netValThousand = Num(r, "ContractValueofOpenInterest(Net)(Thousands)");  // 千元
            decimal netYi = Math.Round(netValThousand / 100_000m, 1);                      // 千元 → 億
            string date = Str(r, "Date");
            if (date.Length == 8) date = $"{date[..4]}-{date.Substring(4, 2)}-{date.Substring(6, 2)}";
            return new FuturesSentiment(date, netOi, netYi);
        }
        return null;
    }

    private static string Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static long Num(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) ? TwseFundFlowClient.Num(v) : 0L;
}
