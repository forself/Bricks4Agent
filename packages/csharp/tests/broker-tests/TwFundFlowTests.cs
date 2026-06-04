using BrokerCore.Services;

namespace Broker.Tests;

/// <summary>
/// 2026-06-04:台股資金流解析單元測試(TwseFundFlowClient 純函式)。
/// fixture 用核對過的真實 2330 台積電 2026-06-03 數值:
///   三大法人 外資(-103,827)+投信(647,975)+自營(226,886) == 三大法人(771,034)、
///   融資 前29,135→今28,310、融券 前91→今104。
/// </summary>
public static class TwFundFlowTests
{
    // T86:2330(普通股)+ 0050(ETF、4位數→納入)+ 00403A(權證類、6位含字母→濾掉)
    private const string T86Json = """
    {"stat":"OK","date":"20260603","fields":["證券代號","證券名稱","外陸資買進股數","外陸資賣出股數","外陸資買賣超股數","外資自營商買進股數","外資自營商賣出股數","外資自營商買賣超股數","投信買進股數","投信賣出股數","投信買賣超股數","自營商買賣超股數","自營商買進(自行)","自營商賣出(自行)","自營商買賣超(自行)","自營商買進(避險)","自營商賣出(避險)","自營商買賣超(避險)","三大法人買賣超股數"],
    "data":[
    ["2330","台積電","17,487,849","17,591,676","-103,827","0","0","0","885,898","237,923","647,975","226,886","407,000","241,000","166,000","213,837","152,951","60,886","771,034"],
    ["0050","元大台灣50","100,000","50,000","50,000","0","0","0","0","0","0","10,000","0","0","0","0","0","0","60,000"],
    ["00403A","主動式統一","189,608,716","37,097,076","152,511,640","0","0","0","0","0","0","203,657,948","0","0","0","219,368,439","15,710,491","203,657,948","356,169,588"]
    ]}
    """;

    private const string T86Holiday = """{"stat":"很抱歉，沒有符合條件的資料!","total":0}""";

    // MI_MARGN:table[0]=市場統計(欄少→跳過)、table[1]=個股彙總(16欄、含 2330)
    private const string MarginJson = """
    {"stat":"OK","date":"20260603","tables":[
    {"title":"信用交易統計","fields":["項目","買進","賣出","現金","前日餘額","限額"],"data":[["融資(交易單位)","768,515","737,619","5,061","9,373,138","9,398,973"]]},
    {"title":"融資融券彙總","fields":["代號","名稱","買進","賣出","現金償還","前日餘額","今日餘額","限額","買進","賣出","現券償還","前日餘額","今日餘額","限額","資券互抵","註記"],"data":[
    ["2330","台積電","1,094","1,690","229","29,135","28,310","6,483,131","6","19","0","91","104","6,483,131","0",""],
    ["00403A","主動式統一","185","1,242","104","14,284","13,000","999","0","0","0","0","0","999","0",""]
    ]}
    ]}
    """;

    public static (int passed, int failed) Run()
    {
        int passed = 0, failed = 0;
        void Check(string name, bool cond)
        {
            if (cond) { Console.WriteLine($"  [PASS] {name}"); passed++; }
            else { Console.Error.WriteLine($"  [FAIL] {name}"); failed++; }
        }

        Console.WriteLine("--- TwFundFlow (TWSE parser) Tests ---");

        // ── ParseT86 ──
        var rows = TwseFundFlowClient.ParseT86(T86Json, "2026-06-03");
        Check("t86-filters-warrant→2-rows", rows.Count == 2);            // 2330 + 0050,濾掉 00403A
        var tsmc = rows.FirstOrDefault(r => r.StockCode == "2330");
        Check("t86-2330-parsed", tsmc != null);
        if (tsmc != null)
        {
            Check("t86-foreign=-103827", tsmc.ForeignNet == -103_827);   // 外陸資 + 外資自營商(0)
            Check("t86-trust=647975", tsmc.TrustNet == 647_975);
            Check("t86-dealer=226886", tsmc.DealerNet == 226_886);
            Check("t86-total=771034", tsmc.TotalNet == 771_034);
            // 會計恆等式:外+投+自 == 三大法人合計(最強的 index 正確性保證)
            Check("t86-identity(外+投+自==合計)",
                tsmc.ForeignNet + tsmc.TrustNet + tsmc.DealerNet == tsmc.TotalNet);
            Check("t86-name=台積電", tsmc.StockName == "台積電");
            Check("t86-entrykey", tsmc.EntryKey == "2026-06-03:2330");
        }
        Check("t86-includes-etf-0050", rows.Any(r => r.StockCode == "0050"));   // 4位數 ETF 納入儲存
        Check("t86-excludes-00403A", rows.All(r => r.StockCode != "00403A"));   // 6位含字母濾掉

        // 非交易日 → 空
        Check("t86-holiday→empty", TwseFundFlowClient.ParseT86(T86Holiday, "2026-05-30").Count == 0);

        // ── ParseMargin ──
        var margin = TwseFundFlowClient.ParseMargin(MarginJson);
        Check("margin-has-2330", margin.ContainsKey("2330"));
        if (margin.TryGetValue("2330", out var m))
        {
            Check("margin-bal=28310", m.MarginBalance == 28_310);   // [6]今日餘額
            Check("margin-prev=29135", m.MarginPrev == 29_135);     // [5]前日餘額
            Check("short-bal=104", m.ShortBalance == 104);          // [12]今日餘額
            Check("short-prev=91", m.ShortPrev == 91);              // [11]前日餘額
        }
        Check("margin-skips-market-stat-table", !margin.ContainsKey("融資(交易單位)"));
        Check("margin-excludes-00403A", !margin.ContainsKey("00403A"));   // 6位含字母濾掉

        // ── 融資/融券變化(digest 用的衍生量)──
        if (margin.TryGetValue("2330", out var m2))
        {
            Check("margin-change=-825", m2.MarginBalance - m2.MarginPrev == -825);   // 融資減少
            Check("short-change=+13", m2.ShortBalance - m2.ShortPrev == 13);         // 融券增加
        }

        // ── Num 解析邊界 ──
        using (var doc = System.Text.Json.JsonDocument.Parse("""["1,234","-5,678","--","","0"]"""))
        {
            var a = doc.RootElement;
            Check("num-comma", TwseFundFlowClient.Num(a[0]) == 1234);
            Check("num-negative", TwseFundFlowClient.Num(a[1]) == -5678);
            Check("num-dashes→0", TwseFundFlowClient.Num(a[2]) == 0);
            Check("num-empty→0", TwseFundFlowClient.Num(a[3]) == 0);
            Check("num-zero", TwseFundFlowClient.Num(a[4]) == 0);
        }

        // ── IsCommonStock ──
        Check("common-2330", TwseFundFlowClient.IsCommonStock("2330"));
        Check("common-0050-etf", TwseFundFlowClient.IsCommonStock("0050"));
        Check("not-common-00403A", !TwseFundFlowClient.IsCommonStock("00403A"));
        Check("not-common-6digit", !TwseFundFlowClient.IsCommonStock("030001"));

        Console.WriteLine($"--- TwFundFlow: {passed} passed, {failed} failed ---");
        return (passed, failed);
    }
}
