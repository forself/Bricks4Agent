using System.Net;
using System.Text;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 台股資金流日報的「純計算 + 雙渲染」(2026-06-04)。不碰網路/DB,全部吃傳入資料 → 可單元測試。
///
/// 計算:三大法人合計/外資/投信 買超賣超榜(有收盤價→排金額億、無→排張數)、融資融券增加榜、
///       外資連續買超/賣超天數(吃歷史序列)、watchlist 逐檔、重點摘要。
/// 渲染:RenderDiscord(精簡、手機一眼看、附報表連結)+ RenderHtml(完整、表格/顏色、行動裝置友善)。
/// </summary>
public static class TwFundFlowReport
{
    private const long Lot = 1000;   // 1 張 = 1000 股

    public record RankItem(string Code, string Name, decimal AmountYi, long Lots);
    /// <summary>Days 帶正負號:正=連續買超天數、負=連續賣超天數。</summary>
    public record ConsecItem(string Code, string Name, int Days, long LatestLots);
    public record WatchRow(string Code, string Name, long TotalLots, long ForeignLots, long TrustLots,
        long DealerLots, long MarginChg, long ShortChg, decimal TotalYi, bool HasData);
    /// <summary>產業層級資金流:三大法人合計金額(億)+ 外資/投信 分項(億)+ 該產業檔數。</summary>
    public record SectorItem(string Sector, decimal TotalYi, decimal ForeignYi, decimal TrustYi, int StockCount);

    public record ReportData(
        string Date, int TotalStocks, bool UseAmount,
        List<RankItem> TotalBuy, List<RankItem> TotalSell,
        List<RankItem> ForeignBuy, List<RankItem> ForeignSell,
        List<RankItem> TrustBuy, List<RankItem> TrustSell,
        List<RankItem> MarginUp, List<RankItem> ShortUp,
        List<ConsecItem> ConsecBuy, List<ConsecItem> ConsecSell,
        List<SectorItem> SectorInflow, List<SectorItem> SectorOutflow,
        List<WatchRow> Watch, List<string> Highlights, List<string> SectorHighlights);

    /// <summary>買賣超金額(億元)= 淨股數 × 收盤價 / 1e8。收盤未知(0)→ 0。</summary>
    public static decimal AmountYi(long shares, decimal close) =>
        close <= 0 ? 0m : Math.Round((decimal)shares * close / 100_000_000m, 1);

    /// <summary>連續同向天數:吃「最近在前」的淨額序列,回 [0] 同號的連續長度;[0]=0 回 0。</summary>
    public static int ConsecutiveDays(IReadOnlyList<long> netsRecentFirst)
    {
        if (netsRecentFirst.Count == 0) return 0;
        int sign = Math.Sign(netsRecentFirst[0]);
        if (sign == 0) return 0;
        int n = 0;
        foreach (var v in netsRecentFirst) { if (Math.Sign(v) == sign) n++; else break; }
        return n;
    }

    public static ReportData Build(
        string date,
        List<TwFundFlowDaily> rows,
        Dictionary<string, decimal> closes,
        Dictionary<string, List<long>> foreignHistRecentFirst,
        string[] watchlist,
        Dictionary<string, string>? sectorByCode = null,
        int topN = 8)
    {
        bool useAmount = closes.Count > 0;
        decimal Close(string c) => closes.TryGetValue(c, out var v) ? v : 0m;
        // 全市場榜:排除 ETF(代號 0 開頭)、避免 0050/0056 巨量股數蓋過個股
        var stocks = rows.Where(r => r.StockCode.Length == 4 && r.StockCode[0] != '0').ToList();

        RankItem Mk(TwFundFlowDaily r, long net) => new(r.StockCode, r.StockName, AmountYi(net, Close(r.StockCode)), net / Lot);
        decimal Metric(TwFundFlowDaily r, long net) => useAmount ? Math.Abs(AmountYi(net, Close(r.StockCode))) : Math.Abs((decimal)net);

        List<RankItem> TopBuy(Func<TwFundFlowDaily, long> sel) =>
            stocks.Where(r => sel(r) > 0).OrderByDescending(r => Metric(r, sel(r))).Take(topN).Select(r => Mk(r, sel(r))).ToList();
        List<RankItem> TopSell(Func<TwFundFlowDaily, long> sel) =>
            stocks.Where(r => sel(r) < 0).OrderByDescending(r => Metric(r, sel(r))).Take(topN).Select(r => Mk(r, sel(r))).ToList();

        var totalBuy = TopBuy(r => r.TotalNet);   var totalSell = TopSell(r => r.TotalNet);
        var foreignBuy = TopBuy(r => r.ForeignNet); var foreignSell = TopSell(r => r.ForeignNet);
        var trustBuy = TopBuy(r => r.TrustNet);     var trustSell = TopSell(r => r.TrustNet);

        // 融資/融券「增加」榜(張):融資增=散戶加槓桿、融券增=空方布局
        List<RankItem> TopInc(Func<TwFundFlowDaily, long> chg) =>
            stocks.Where(r => chg(r) > 0).OrderByDescending(chg).Take(topN)
                  .Select(r => new RankItem(r.StockCode, r.StockName, 0m, chg(r))).ToList();
        var marginUp = TopInc(r => r.MarginBalance - r.MarginPrev);
        var shortUp = TopInc(r => r.ShortBalance - r.ShortPrev);

        // 外資連續買超/賣超(≥2 日)
        var nameByCode = rows.ToDictionary(r => r.StockCode, r => r.StockName);
        var consec = new List<ConsecItem>();
        foreach (var (code, series) in foreignHistRecentFirst)
        {
            if (!(code.Length == 4 && code[0] != '0')) continue;
            if (!nameByCode.TryGetValue(code, out var nm)) continue;
            int days = ConsecutiveDays(series);
            if (days >= 2) consec.Add(new ConsecItem(code, nm, days * Math.Sign(series[0]), series[0] / Lot));
        }
        var consecBuy = consec.Where(c => c.Days > 0).OrderByDescending(c => c.Days).ThenByDescending(c => Math.Abs(c.LatestLots)).Take(topN).ToList();
        var consecSell = consec.Where(c => c.Days < 0).OrderBy(c => c.Days).ThenByDescending(c => Math.Abs(c.LatestLots)).Take(topN).ToList();

        // watchlist 逐檔
        var byCode = rows.ToDictionary(r => r.StockCode);
        var watch = watchlist.Select(code =>
        {
            if (!byCode.TryGetValue(code, out var r))
                return new WatchRow(code, code, 0, 0, 0, 0, 0, 0, 0m, false);
            return new WatchRow(code, r.StockName, r.TotalNet / Lot, r.ForeignNet / Lot, r.TrustNet / Lot,
                r.DealerNet / Lot, r.MarginBalance - r.MarginPrev, r.ShortBalance - r.ShortPrev,
                AmountYi(r.TotalNet, Close(code)), true);
        }).ToList();

        // 按產業彙總三大法人金額(億):每檔的法人/外資/投信金額累加到所屬產業 → 排各產業淨流入/流出
        var sectorInflow = new List<SectorItem>();
        var sectorOutflow = new List<SectorItem>();
        var sectorHl = new List<string>();
        if (sectorByCode is { Count: > 0 } && useAmount)
        {
            var agg = new Dictionary<string, (decimal tot, decimal fgn, decimal tru, int n)>();
            foreach (var r in stocks)
            {
                if (!sectorByCode.TryGetValue(r.StockCode, out var sec)) continue;
                var c = Close(r.StockCode);
                if (c <= 0) continue;
                agg.TryGetValue(sec, out var a);
                agg[sec] = (a.tot + AmountYi(r.TotalNet, c), a.fgn + AmountYi(r.ForeignNet, c),
                            a.tru + AmountYi(r.TrustNet, c), a.n + 1);
            }
            var sectors = agg.Select(kv => new SectorItem(kv.Key,
                Math.Round(kv.Value.tot, 1), Math.Round(kv.Value.fgn, 1), Math.Round(kv.Value.tru, 1), kv.Value.n)).ToList();
            sectorInflow = sectors.Where(s => s.TotalYi > 0).OrderByDescending(s => s.TotalYi).Take(topN).ToList();
            sectorOutflow = sectors.Where(s => s.TotalYi < 0).OrderBy(s => s.TotalYi).Take(topN).ToList();
            if (sectorInflow.Count > 0)
                sectorHl.Add($"資金最流入:{sectorInflow[0].Sector}({FmtYi(sectorInflow[0].TotalYi)})" +
                    (sectorInflow.Count > 1 ? $"、{sectorInflow[1].Sector}({FmtYi(sectorInflow[1].TotalYi)})" : ""));
            if (sectorOutflow.Count > 0)
                sectorHl.Add($"資金最流出:{sectorOutflow[0].Sector}({FmtYi(sectorOutflow[0].TotalYi)})" +
                    (sectorOutflow.Count > 1 ? $"、{sectorOutflow[1].Sector}({FmtYi(sectorOutflow[1].TotalYi)})" : ""));
        }

        // 重點摘要
        var hl = new List<string>();
        string Val(RankItem i) => useAmount ? FmtYi(i.AmountYi) : FmtLots(i.Lots);
        if (foreignBuy.Count > 0)
            hl.Add($"外資最買 {foreignBuy[0].Name}({Val(foreignBuy[0])})" +
                   (foreignSell.Count > 0 ? $"、最賣 {foreignSell[0].Name}({Val(foreignSell[0])})" : ""));
        if (trustBuy.Count > 0)
            hl.Add($"投信最買 {trustBuy[0].Name}({Val(trustBuy[0])})" +
                   (trustSell.Count > 0 ? $"、最賣 {trustSell[0].Name}({Val(trustSell[0])})" : ""));
        if (consecBuy.Count > 0)
            hl.Add($"外資連買最長:{consecBuy[0].Name} 連 {consecBuy[0].Days} 日");
        if (consecSell.Count > 0)
            hl.Add($"外資連賣最長:{consecSell[0].Name} 連 {Math.Abs(consecSell[0].Days)} 日");
        var wBig = watch.Where(x => x.HasData).OrderByDescending(x => Math.Abs(x.TotalLots)).FirstOrDefault();
        if (wBig != null)
            hl.Add($"watchlist 法人最大動向:{wBig.Name} {FmtLots(wBig.TotalLots)}");

        return new ReportData(date, stocks.Count, useAmount, totalBuy, totalSell, foreignBuy, foreignSell,
            trustBuy, trustSell, marginUp, shortUp, consecBuy, consecSell,
            sectorInflow, sectorOutflow, watch, hl, sectorHl);
    }

    // ── 格式化 ──
    private static string FmtYi(decimal v) => (v >= 0 ? "+" : "") + v.ToString("0.0") + "億";
    private static string FmtLots(long v) => (v >= 0 ? "+" : "") + v.ToString("N0") + "張";
    private static string Val(RankItem i, bool useAmount) => useAmount ? FmtYi(i.AmountYi) : FmtLots(i.Lots);

    // ── Discord / LINE(精簡)──
    // includeWatchlist=false:省略「我的 watchlist」(個人關注清單不外送)。
    // sectorFocus=true:家人版 → 以「各產業資金流」為主、拿掉個股 top / 連續動能(家人要看的是產業)。
    public static string RenderDiscord(ReportData d, string? reportUrl, bool includeWatchlist = true, bool sectorFocus = false)
    {
        var sb = new StringBuilder();
        // 摘要:家人版用產業摘要;否則用個股摘要(非 watchlist 模式濾掉「watchlist 法人最大動向」那條)
        var highlights = sectorFocus
            ? d.SectorHighlights
            : (includeWatchlist ? d.Highlights : d.Highlights.Where(h => !h.StartsWith("watchlist")).ToList());
        if (highlights.Count > 0)
        {
            sb.AppendLine("**📊 重點摘要**");
            foreach (var h in highlights) sb.AppendLine($"・{h}");
            sb.AppendLine();
        }
        void SecRank(string title, List<RankItem> items, int take)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"**{title}**");
            foreach (var i in items.Take(take)) sb.AppendLine($"・{i.Name}({i.Code}) {Val(i, d.UseAmount)}");
            sb.AppendLine();
        }
        void SecSector(string title, List<SectorItem> items, int take)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"**{title}**");
            foreach (var s in items.Take(take))
                sb.AppendLine($"・{s.Sector} {FmtYi(s.TotalYi)}(外{FmtYi(s.ForeignYi)} 投{FmtYi(s.TrustYi)})");
            sb.AppendLine();
        }

        if (sectorFocus)
        {
            // 家人版:純產業
            SecSector("📈 產業淨流入 top", d.SectorInflow, 8);
            SecSector("📉 產業淨流出 top", d.SectorOutflow, 8);
        }
        else
        {
            if (d.ConsecBuy.Count > 0 || d.ConsecSell.Count > 0)
            {
                sb.AppendLine("**🔥 外資連續動能**");
                foreach (var c in d.ConsecBuy.Take(3)) sb.AppendLine($"・🟢 {c.Name}({c.Code}) 連 {c.Days} 日買超");
                foreach (var c in d.ConsecSell.Take(3)) sb.AppendLine($"・🔴 {c.Name}({c.Code}) 連 {Math.Abs(c.Days)} 日賣超");
                sb.AppendLine();
            }
            SecRank("🟢 三大法人買超 top", d.TotalBuy, 5);
            SecRank("🔴 三大法人賣超 top", d.TotalSell, 5);
            SecSector("📈 產業淨流入 top", d.SectorInflow, 6);
            SecSector("📉 產業淨流出 top", d.SectorOutflow, 6);

            if (includeWatchlist)
            {
                sb.AppendLine($"**📌 我的 watchlist({d.Watch.Count})**");
                foreach (var w in d.Watch)
                {
                    if (!w.HasData) { sb.AppendLine($"・{w.Code}: (無資料)"); continue; }
                    sb.AppendLine($"・{w.Name}({w.Code}): 法人{FmtLots(w.TotalLots)} (外{FmtLots(w.ForeignLots)} 投{FmtLots(w.TrustLots)}) · 融資{FmtLots(w.MarginChg)} 融券{FmtLots(w.ShortChg)}");
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(reportUrl))
        {
            sb.AppendLine();
            sb.AppendLine($"📄 完整報表:{reportUrl}");
        }
        return sb.ToString();
    }

    // ── HTML(完整報表、行動裝置友善、自含 CSS)──
    // includeWatchlist=false:family 公開頁 → 拿掉「我的 watchlist」段 + 濾掉 watchlist 重點摘要(個人關注不外送)。
    public static string RenderHtml(ReportData d, bool includeWatchlist = true)
    {
        string E(string s) => WebUtility.HtmlEncode(s);
        string Cls(decimal v) => v >= 0 ? "buy" : "sell";
        string ClsL(long v) => v >= 0 ? "buy" : "sell";

        var sb = new StringBuilder();
        sb.Append($@"<!doctype html><html lang=""zh-Hant""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>台股資金流日報 · {E(d.Date)}</title>
<style>
:root{{--buy:#d1383a;--sell:#0a8f3c;--bg:#f6f7f9;--card:#fff;--mut:#6b7280;--line:#e5e7eb}}
*{{box-sizing:border-box}}body{{margin:0;font-family:-apple-system,'Segoe UI','Microsoft JhengHei',sans-serif;background:var(--bg);color:#111;font-size:15px;line-height:1.5}}
.wrap{{max-width:880px;margin:0 auto;padding:14px}}
h1{{font-size:20px;margin:6px 0}}.date{{color:var(--mut);font-size:13px}}
.card{{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:12px;margin:12px 0;overflow:hidden}}
.card h2{{font-size:16px;margin:0 0 8px}}
.hl{{margin:6px 0;padding-left:18px}}.hl li{{margin:3px 0}}
.grid{{display:grid;grid-template-columns:1fr 1fr;gap:10px}}
.grid>div{{min-width:0;overflow-x:auto;-webkit-overflow-scrolling:touch}}
@media(max-width:700px){{.grid{{grid-template-columns:1fr}}}}
.scroll{{overflow-x:auto;-webkit-overflow-scrolling:touch}}
table{{width:100%;border-collapse:collapse;font-size:14px}}
th,td{{text-align:right;padding:5px 6px;border-bottom:1px solid var(--line);white-space:nowrap}}
th:first-child,td:first-child{{text-align:left}}
th{{color:var(--mut);font-weight:600;font-size:12px}}
.buy{{color:var(--buy);font-weight:600}}.sell{{color:var(--sell);font-weight:600}}
.tag{{font-size:11px;color:var(--mut)}}
.watch td:first-child{{font-weight:600}}
.foot{{color:var(--mut);font-size:12px;text-align:center;margin:18px 0 8px;padding:0 8px}}
.note{{color:var(--mut);font-size:12px;margin:2px 0 0}}
@media(max-width:480px){{body{{font-size:14px}}table{{font-size:13px}}th,td{{padding:4px}}.wrap{{padding:8px}}h1{{font-size:18px}}}}
</style></head><body><div class=""wrap"">");

        sb.Append($@"<h1>🇹🇼 台股資金流日報</h1><div class=""date"">{E(d.Date)} · 全市場 {d.TotalStocks} 檔個股 · 單位{(d.UseAmount ? "買賣超金額(億元)" : "張")}、正=買超/增</div>");

        // 重點摘要(family 版濾掉「watchlist 法人最大動向」那條)
        var hlList = includeWatchlist ? d.Highlights : d.Highlights.Where(h => !h.StartsWith("watchlist")).ToList();
        if (hlList.Count > 0)
        {
            sb.Append(@"<div class=""card""><h2>📊 重點摘要</h2><ul class=""hl"">");
            foreach (var h in hlList) sb.Append($"<li>{E(h)}</li>");
            sb.Append("</ul></div>");
        }

        // 各產業資金流(三大法人金額)— 放重點摘要後、最顯眼
        if (d.SectorInflow.Count > 0 || d.SectorOutflow.Count > 0)
        {
            sb.Append(@"<div class=""card""><h2>🏭 各產業資金流(三大法人金額)</h2><div class=""grid"">");
            sb.Append(SectorTable("淨流入 top", d.SectorInflow, E, Cls));
            sb.Append(SectorTable("淨流出 top", d.SectorOutflow, E, Cls));
            sb.Append("</div></div>");
        }

        // 連續動能
        if (d.ConsecBuy.Count > 0 || d.ConsecSell.Count > 0)
        {
            sb.Append(@"<div class=""card""><h2>🔥 外資連續動能(連 N 日同向)</h2><div class=""grid"">");
            sb.Append(ConsecTable("連續買超", d.ConsecBuy, true, E));
            sb.Append(ConsecTable("連續賣超", d.ConsecSell, false, E));
            sb.Append("</div></div>");
        }

        // 三大法人合計
        sb.Append(@"<div class=""card""><h2>三大法人合計</h2><div class=""grid"">");
        sb.Append(RankTable("買超 top", d.TotalBuy, d.UseAmount, E, Cls, ClsL));
        sb.Append(RankTable("賣超 top", d.TotalSell, d.UseAmount, E, Cls, ClsL));
        sb.Append("</div></div>");

        // 外資 / 投信
        sb.Append(@"<div class=""card""><h2>外資</h2><div class=""grid"">");
        sb.Append(RankTable("買超 top", d.ForeignBuy, d.UseAmount, E, Cls, ClsL));
        sb.Append(RankTable("賣超 top", d.ForeignSell, d.UseAmount, E, Cls, ClsL));
        sb.Append("</div></div>");
        sb.Append(@"<div class=""card""><h2>投信</h2><div class=""grid"">");
        sb.Append(RankTable("買超 top", d.TrustBuy, d.UseAmount, E, Cls, ClsL));
        sb.Append(RankTable("賣超 top", d.TrustSell, d.UseAmount, E, Cls, ClsL));
        sb.Append("</div></div>");

        // 融資融券
        sb.Append(@"<div class=""card""><h2>融資融券動向(張)</h2><div class=""grid"">");
        sb.Append(LotTable("融資增加 top(散戶加槓桿)", d.MarginUp, E));
        sb.Append(LotTable("融券增加 top(空方布局)", d.ShortUp, E));
        sb.Append("</div></div>");

        // watchlist(family 公開頁不含此段 — 個人關注清單不外送)
        if (includeWatchlist)
        {
            sb.Append(@"<div class=""card""><h2>📌 我的 watchlist</h2><div class=""scroll""><table class=""watch""><tr><th>個股</th><th>法人(張)</th><th>外資</th><th>投信</th><th>自營</th><th>融資</th><th>融券</th></tr>");
            foreach (var w in d.Watch)
            {
                if (!w.HasData) { sb.Append($@"<tr><td>{E(w.Code)}</td><td colspan=""6"" class=""tag"">無資料</td></tr>"); continue; }
                sb.Append($@"<tr><td>{E(w.Name)}<span class=""tag""> {E(w.Code)}</span></td>" +
                    $@"<td class=""{ClsL(w.TotalLots)}"">{Lots(w.TotalLots)}</td>" +
                    $@"<td class=""{ClsL(w.ForeignLots)}"">{Lots(w.ForeignLots)}</td>" +
                    $@"<td class=""{ClsL(w.TrustLots)}"">{Lots(w.TrustLots)}</td>" +
                    $@"<td class=""{ClsL(w.DealerLots)}"">{Lots(w.DealerLots)}</td>" +
                    $@"<td class=""{ClsL(w.MarginChg)}"">{Lots(w.MarginChg)}</td>" +
                    $@"<td class=""{ClsL(w.ShortChg)}"">{Lots(w.ShortChg)}</td></tr>");
            }
            sb.Append("</table></div></div>");
        }

        sb.Append($@"<div class=""foot"">資料來源:TWSE 三大法人買賣超(T86)+ 融資融券(MI_MARGN)+ 收盤(STOCK_DAY_ALL)。B4A 自動產生、僅供參考、非投資建議。</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string Lots(long v) => (v >= 0 ? "+" : "") + v.ToString("N0");

    private static string RankTable(string title, List<RankItem> items, bool useAmount,
        Func<string, string> E, Func<decimal, string> Cls, Func<long, string> ClsL)
    {
        var sb = new StringBuilder();
        sb.Append($@"<div><table><tr><th>{E(title)}</th><th>{(useAmount ? "億元" : "張")}</th></tr>");
        if (items.Count == 0) sb.Append(@"<tr><td class=""tag"">無</td><td></td></tr>");
        foreach (var i in items)
        {
            string cls = useAmount ? Cls(i.AmountYi) : ClsL(i.Lots);
            string val = useAmount ? (i.AmountYi >= 0 ? "+" : "") + i.AmountYi.ToString("0.0") : Lots(i.Lots);
            sb.Append($@"<tr><td>{E(i.Name)}<span class=""tag""> {E(i.Code)}</span></td><td class=""{cls}"">{val}</td></tr>");
        }
        sb.Append("</table></div>");
        return sb.ToString();
    }

    private static string SectorTable(string title, List<SectorItem> items, Func<string, string> E, Func<decimal, string> Cls)
    {
        static string Sgn(decimal v) => (v >= 0 ? "+" : "") + v.ToString("0.0");
        var sb = new StringBuilder();
        sb.Append($@"<div><table><tr><th>{E(title)}</th><th>億元</th><th>外資</th><th>投信</th></tr>");
        if (items.Count == 0) sb.Append(@"<tr><td class=""tag"">無</td><td></td><td></td><td></td></tr>");
        foreach (var s in items)
            sb.Append($@"<tr><td>{E(s.Sector)}<span class=""tag""> {s.StockCount}檔</span></td>" +
                $@"<td class=""{Cls(s.TotalYi)}"">{Sgn(s.TotalYi)}</td>" +
                $@"<td class=""{Cls(s.ForeignYi)}"">{Sgn(s.ForeignYi)}</td>" +
                $@"<td class=""{Cls(s.TrustYi)}"">{Sgn(s.TrustYi)}</td></tr>");
        sb.Append("</table></div>");
        return sb.ToString();
    }

    private static string LotTable(string title, List<RankItem> items, Func<string, string> E)
    {
        var sb = new StringBuilder();
        sb.Append($@"<div><table><tr><th>{E(title)}</th><th>張</th></tr>");
        if (items.Count == 0) sb.Append(@"<tr><td class=""tag"">無</td><td></td></tr>");
        foreach (var i in items)
            sb.Append($@"<tr><td>{E(i.Name)}<span class=""tag""> {E(i.Code)}</span></td><td class=""buy"">{Lots(i.Lots)}</td></tr>");
        sb.Append("</table></div>");
        return sb.ToString();
    }

    private static string ConsecTable(string title, List<ConsecItem> items, bool buy, Func<string, string> E)
    {
        var sb = new StringBuilder();
        sb.Append($@"<div><table><tr><th>{E(title)}</th><th>連續</th></tr>");
        if (items.Count == 0) sb.Append(@"<tr><td class=""tag"">無</td><td></td></tr>");
        foreach (var c in items)
            sb.Append($@"<tr><td>{E(c.Name)}<span class=""tag""> {E(c.Code)}</span></td><td class=""{(buy ? "buy" : "sell")}"">{Math.Abs(c.Days)} 日</td></tr>");
        sb.Append("</table></div>");
        return sb.ToString();
    }
}
