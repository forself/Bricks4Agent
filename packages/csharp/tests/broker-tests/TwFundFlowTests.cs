using BrokerCore.Models;
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

    // STOCK_DAY_ALL:2330(收盤 2,385)+ 停牌(收盤 --)+ 00403A(濾掉)
    private const string StockDayAllJson = """
    {"stat":"OK","date":"20260603","fields":["證券代號","證券名稱","成交股數","成交金額","開盤價","最高價","最低價","收盤價","漲跌價差","成交筆數"],
    "data":[
    ["2330","台積電","32,542,948","77,931,338,144","2,385.00","2,415.00","2,385.00","2,385.00","-40.00","196,886"],
    ["9999","停牌股","0","0","--","--","--","--","--","0"],
    ["00403A","主動式統一","1,000","1,000","10.00","10.00","10.00","10.50","0.50","5"]
    ]}
    """;

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

    // MI_INDEX(指定日全市場 OHLC):頂層無 stat、多表(指數表 + 個股表)。個股表用欄位名「證券代號」+「收盤價」定位。
    // 含:指數表(該跳過、有「收盤指數」非「收盤價」)、2330(收 2,425、漲跌欄含 HTML)、停牌(收 --)、00403A(濾掉)
    private const string MiIndexJson = """
    {"date":"20260603","tables":[
    {"title":"價格指數","fields":["指數","收盤指數","漲跌(+/-)","漲跌點數","漲跌百分比(%)"],"data":[["寶島股價指數","12,345.67","<p style='color:red'>+</p>","1.00","0.5"]]},
    {"title":"每日收盤行情","fields":["證券代號","證券名稱","成交股數","成交筆數","成交金額","開盤價","最高價","最低價","收盤價","漲跌(+/-)","漲跌價差"],"data":[
    ["2330","台積電","29,219,904","89,415","70,861,766,012","2,425.00","2,440.00","2,410.00","2,425.00","<p style='color:red'>+</p>","45.00"],
    ["9999","停牌股","0","0","0","--","--","--","--","","0.00"],
    ["00403A","主動式統一","1,000","5","10,000","10.00","10.50","10.00","10.50","<p style='color:green'>-</p>","0.50"]
    ]}
    ]}
    """;

    // 上市公司基本資料(t187ap03_L 形狀):公司代號 + 產業別代碼
    private const string IndustryJson = """
    [
    {"公司代號":"2330","公司名稱":"台積電","產業別":"24"},
    {"公司代號":"2317","公司名稱":"鴻海","產業別":"28"},
    {"公司代號":"2603","公司名稱":"長榮","產業別":"15"},
    {"公司代號":"01001","公司名稱":"權證類","產業別":"24"}
    ]
    """;

    // 上櫃 TPEx 三大法人(欄名仿真;外資 = total−trust−dealer 反推)
    private const string TpexInstJson = """
    [
    {"Date":"1150605","SecuritiesCompanyCode":"5483","CompanyName":"中美晶","SecuritiesInvestmentTrustCompanies-Difference":"150000","Dealers-Difference":"50000","TotalDifference":"1000000"},
    {"Date":"1150605","SecuritiesCompanyCode":"00679B","CompanyName":"元大美債","SecuritiesInvestmentTrustCompanies-Difference":"0","Dealers-Difference":"0","TotalDifference":"500000"}
    ]
    """;
    private const string TpexCloseJson = """[{"SecuritiesCompanyCode":"5483","Close":"120.5"},{"SecuritiesCompanyCode":"00679B","Close":"30.0"}]""";
    private const string TpexIndustryJson = """[{"SecuritiesCompanyCode":"5483","SecuritiesIndustryCode":"24"},{"SecuritiesCompanyCode":"01001","SecuritiesIndustryCode":"24"}]""";

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

        // ── ParseStockDayAll(收盤價)──
        var (cdate, closes) = TwseFundFlowClient.ParseStockDayAll(StockDayAllJson);
        Check("close-date=20260603", cdate == "20260603");
        Check("close-2330=2385", closes.TryGetValue("2330", out var c2330) && c2330 == 2385m);
        Check("close-skips-halt(--)", !closes.ContainsKey("9999"));        // 收盤 -- 不收
        Check("close-excludes-00403A", !closes.ContainsKey("00403A"));

        // ── ParseMiIndex(指定日收盤、根治張↔億元跳動)──
        var (midate, mcloses) = TwseFundFlowClient.ParseMiIndex(MiIndexJson);
        Check("miindex-date=20260603", midate == "20260603");
        Check("miindex-2330-close=2425", mcloses.TryGetValue("2330", out var mc) && mc == 2425m);
        Check("miindex-skips-index-table", !mcloses.ContainsKey("寶島股價指數"));   // 指數表(收盤指數≠收盤價)跳過
        Check("miindex-skips-halt(--)", !mcloses.ContainsKey("9999"));              // 停牌收盤 -- 不收
        Check("miindex-excludes-00403A", !mcloses.ContainsKey("00403A"));           // 6位含字母濾掉
        Check("miindex-only-2330", mcloses.Count == 1);                            // 唯一有效個股

        // ── AmountYi(買賣超金額億)──
        Check("amount-771034x2385=18.4", TwFundFlowReport.AmountYi(771_034, 2385m) == 18.4m);
        Check("amount-negative", TwFundFlowReport.AmountYi(-771_034, 2385m) == -18.4m);
        Check("amount-noclose→0", TwFundFlowReport.AmountYi(771_034, 0m) == 0m);

        // ── ConsecutiveDays ──
        Check("consec-+++-→3", TwFundFlowReport.ConsecutiveDays(new long[] { 5, 3, 1, -2 }) == 3);
        Check("consec---→2", TwFundFlowReport.ConsecutiveDays(new long[] { -5, -3 }) == 2);
        Check("consec-single→1", TwFundFlowReport.ConsecutiveDays(new long[] { 7 }) == 1);
        Check("consec-zero→0", TwFundFlowReport.ConsecutiveDays(new long[] { 0, 5, 5 }) == 0);
        Check("consec-empty→0", TwFundFlowReport.ConsecutiveDays(Array.Empty<long>()) == 0);
        Check("consec-flip→1", TwFundFlowReport.ConsecutiveDays(new long[] { -1, 2, -3 }) == 1);

        // ── Build + Render 煙霧測試 ──
        var reportRows = TwseFundFlowClient.ParseT86(T86Json, "2026-06-03");   // 2330(法人+771張買)+ 0050(ETF)
        var closeMap = new Dictionary<string, decimal> { ["2330"] = 2385m, ["0050"] = 180m };
        var hist = new Dictionary<string, List<long>> { ["2330"] = new() { 771_034, 500_000, 300_000 } };  // 連 3 日買
        var rd = TwFundFlowReport.Build("2026-06-03", reportRows, closeMap, hist, new[] { "2330", "9999" });

        Check("build-useAmount", rd.UseAmount);
        Check("build-totalBuy-2330", rd.TotalBuy.Count == 1 && rd.TotalBuy[0].Code == "2330");
        Check("build-totalBuy-amount=18.4", rd.TotalBuy.Count == 1 && rd.TotalBuy[0].AmountYi == 18.4m);
        Check("build-excludes-etf-from-rank", rd.TotalBuy.All(x => x.Code != "0050"));   // ETF 不進榜
        Check("build-consecBuy-2330-3d", rd.ConsecBuy.Any(c => c.Code == "2330" && c.Days == 3));
        Check("build-watch-2330-hasdata", rd.Watch.Any(w => w.Code == "2330" && w.HasData));
        Check("build-watch-9999-nodata", rd.Watch.Any(w => w.Code == "9999" && !w.HasData));
        Check("build-highlights-nonempty", rd.Highlights.Count > 0);

        var html = TwFundFlowReport.RenderHtml(rd);
        Check("html-has-title", html.Contains("台股資金流日報"));
        Check("html-has-2330", html.Contains("2330"));
        Check("html-has-table", html.Contains("<table"));
        Check("html-valid-doc", html.StartsWith("<!doctype html") && html.TrimEnd().EndsWith("</html>"));
        Check("html-full-has-watchlist", html.Contains("我的 watchlist"));               // 完整版含 watchlist
        var famHtml = TwFundFlowReport.RenderHtml(rd, includeWatchlist: false);
        Check("html-family-no-watchlist", !famHtml.Contains("我的 watchlist"));            // family 公開頁去掉 watchlist
        Check("html-family-valid-doc", famHtml.StartsWith("<!doctype html") && famHtml.TrimEnd().EndsWith("</html>"));

        // ── #2 漲跌% 進個股榜 ──
        var chgMap = new Dictionary<string, decimal> { ["2330"] = 1.5m };
        var rdChg = TwFundFlowReport.Build("2026-06-03", reportRows, closeMap, hist, new[] { "2330" }, null, chgMap);
        Check("changepct-2330=1.5", rdChg.TotalBuy.Any(x => x.Code == "2330" && x.ChangePct == 1.5m));
        Check("discord-shows-changepct", TwFundFlowReport.RenderDiscord(rdChg, null).Contains("1.5%"));
        Check("html-shows-changepct", TwFundFlowReport.RenderHtml(rdChg).Contains("+1.5%"));

        // ── #3a 上櫃 TPEx 解析 ──
        Check("tpex-roc→iso", TpexFundFlowClient.RocToIso("1150605") == "2026-06-05");
        var (tdate, trows) = TpexFundFlowClient.ParseInstitutional(TpexInstJson);
        Check("tpex-date=2026-06-05", tdate == "2026-06-05");
        Check("tpex-only-5483(濾ETF)", trows.Count == 1 && trows[0].StockCode == "5483");
        if (trows.Count == 1)
        {
            Check("tpex-total=1000000", trows[0].TotalNet == 1_000_000);
            Check("tpex-foreign=800000(反推)", trows[0].ForeignNet == 800_000);   // 100萬−15萬−5萬
            Check("tpex-identity", trows[0].ForeignNet + trows[0].TrustNet + trows[0].DealerNet == trows[0].TotalNet);
        }
        var tcloses = TpexFundFlowClient.ParseCloses(TpexCloseJson);
        Check("tpex-close-5483=120.5", tcloses.GetValueOrDefault("5483") == 120.5m);
        Check("tpex-close-excludes-etf", !tcloses.ContainsKey("00679B"));
        var tind = TpexFundFlowClient.ParseIndustryMap(TpexIndustryJson);
        Check("tpex-industry-5483=半導體", tind.GetValueOrDefault("5483") == "半導體");
        Check("tpex-industry-excludes-5digit", !tind.ContainsKey("01001"));

        var disc = TwFundFlowReport.RenderDiscord(rd, "https://x/tw-fundflow.html");
        Check("discord-has-summary", disc.Contains("重點摘要"));
        Check("discord-has-watchlist", disc.Contains("watchlist"));
        Check("discord-has-url", disc.Contains("https://x/tw-fundflow.html"));

        // 推給家人的版本(includeWatchlist:false)必須省略「我的 watchlist」、但保留榜單摘要。
        var fam = TwFundFlowReport.RenderDiscord(rd, "https://x/tw-fundflow.html", includeWatchlist: false);
        Check("family-no-watchlist", !fam.Contains("watchlist"));
        Check("family-has-summary", fam.Contains("重點摘要"));

        // ── 產業對照解析 + IndustryName ──
        var imap = TwseFundFlowClient.ParseIndustryMap(IndustryJson);
        Check("industry-2330=半導體", imap.GetValueOrDefault("2330") == "半導體");
        Check("industry-2317=電子零組件", imap.GetValueOrDefault("2317") == "電子零組件");
        Check("industry-2603=航運", imap.GetValueOrDefault("2603") == "航運");
        Check("industry-excludes-5digit-warrant", !imap.ContainsKey("01001"));   // 5位→IsCommonStock false
        Check("industryname-24=半導體", TwseFundFlowClient.IndustryName("24") == "半導體");
        Check("industryname-unknown→產業99", TwseFundFlowClient.IndustryName("99") == "產業99");

        // ── 按產業彙總(2330 半導體買 / 2317 電子零組件賣)──
        var secRows = new List<TwFundFlowDaily>
        {
            new() { StockCode = "2330", StockName = "台積電", TotalNet = 771_034, ForeignNet = -103_827, TrustNet = 647_975 },
            new() { StockCode = "2317", StockName = "鴻海", TotalNet = -500_000, ForeignNet = -400_000, TrustNet = -100_000 },
        };
        var secCloses = new Dictionary<string, decimal> { ["2330"] = 2385m, ["2317"] = 200m };
        var secMap = new Dictionary<string, string> { ["2330"] = "半導體", ["2317"] = "電子零組件" };
        var rdSec = TwFundFlowReport.Build("2026-06-03", secRows, secCloses,
            new Dictionary<string, List<long>>(), Array.Empty<string>(), secMap);
        Check("sector-inflow-半導體", rdSec.SectorInflow.Any(s => s.Sector == "半導體" && s.TotalYi == 18.4m));
        Check("sector-outflow-電子零組件", rdSec.SectorOutflow.Any(s => s.Sector == "電子零組件" && s.TotalYi == -1.0m));
        Check("sector-highlights-nonempty", rdSec.SectorHighlights.Count > 0);
        // 家人版(sectorFocus)= 產業為主、無個股 top、無 watchlist
        var famSec = TwFundFlowReport.RenderDiscord(rdSec, "https://x/f.html", includeWatchlist: false, sectorFocus: true);
        Check("sectorfocus-has-產業", famSec.Contains("產業淨流入") || famSec.Contains("產業淨流出"));
        Check("sectorfocus-no-watchlist", !famSec.Contains("watchlist"));

        Console.WriteLine($"--- TwFundFlow: {passed} passed, {failed} failed ---");
        return (passed, failed);
    }
}
