// Cross-sectional 價格因子探勘 — 找低 DD 去相關市場中性因子
//
// 每天把所有幣按「過去 lookback 日報酬」排名,long top third / short bot third。
//   momentum 假設:贏家續強(spread = topRet - botRet > 0)
//   reversion 假設:輸家反彈(spread = botRet - topRet > 0)
// 市場中性(多空等額)→ 結構上低 DD、跟單幣方向性 alpha 去相關。
//
// 輸出每個 lookback 的:日均 spread、年化、Sharpe、maxDD、t-stat(momentum 與 reversion 兩方向)。
// bars=1300 含 2022 熊市。
using BrokerCore.Trading;
using StrategyWorker.Engine;
using ToolsShared;

var symbols = new[]
{
    "BTCUSDT","ETHUSDT","SOLUSDT","BNBUSDT","XRPUSDT","ADAUSDT","DOGEUSDT","AVAXUSDT",
    "LINKUSDT","LTCUSDT","DOTUSDT","ATOMUSDT","TRXUSDT","UNIUSDT","NEARUSDT","APTUSDT",
    "ARBUSDT","OPUSDT","SUIUSDT","INJUSDT",
};
int barsLimit = args.Length > 0 && int.TryParse(args[0], out var b) ? b : 1300;

Console.WriteLine($"=== Cross-sectional 價格因子探勘({symbols.Length} 幣、bars={barsLimit}、含 2022)===");

// 1. 抓所有幣的日線,對齊到共同日期
var closesByDate = new SortedDictionary<DateTime, Dictionary<string, decimal>>();
foreach (var sym in symbols)
{
    var bars = await KlineCache.FetchOrLoad(sym, "1d", barsLimit);
    foreach (var bar in bars)
    {
        if (!closesByDate.TryGetValue(bar.OpenTime.Date, out var d)) { d = new(); closesByDate[bar.OpenTime.Date] = d; }
        d[sym] = bar.Close;
    }
}
var dates = closesByDate.Keys.ToList();
Console.WriteLine($"對齊 {dates.Count} 個交易日");

// 2. 毛利探勘:每個 lookback 的 momentum + reversion(daily, gross)
Console.WriteLine($"\n--- 毛利(daily rebalance、無成本)---");
foreach (int lookback in new[] { 5, 10, 20, 40, 60 })
{
    var (mom, rev, _) = RunXsec(lookback, holdDays: 1, costPerSide: 0);
    Report($"lookback {lookback,2}d momentum ", mom);
    Report($"lookback {lookback,2}d reversion", rev);
}

// 3. 20d momentum(最佳)× 換手頻率 × 成本(make-or-break)
Console.WriteLine($"\n--- 20d momentum × hold 天數 × 成本(realistic costPerSide=0.08%)---");
foreach (int hold in new[] { 1, 3, 5, 10 })
{
    var (mom, _, turn) = RunXsec(20, holdDays: hold, costPerSide: 0.0008);
    Report($"20d mom hold {hold,2}d (淨)", mom);
    Console.WriteLine($"        ↑ 平均每次調倉換手 {turn*100:F0}% (×{365.0/hold:F0} 次/年)");
}

// 4. Split-half OOS:20d mom hold-5d 淨報酬切前後半,兩半都顯著才算穩(非單一 regime)
Console.WriteLine($"\n--- Split-half OOS(20d mom hold-5d 淨、前後半各驗)---");
var (momBest, _, _) = RunXsec(20, holdDays: 5, costPerSide: 0.0008);
{
    int half = momBest.Count / 2;
    Report("前半(早期)", momBest.Take(half).ToList());
    Report("後半(近期)", momBest.Skip(half).ToList());
}

// 5. 去相關驗證:xsec mom vs 市場 beta(等權 long 全幣)vs BTC — 確認市場中性 + 邊際貢獻
Console.WriteLine($"\n--- 去相關驗證(20d mom hold-5d vs 方向性 beta)---");
{
    // 對齊:mom series 對應 date index [20, dates.Count-1),每點 = 該日 → 隔日報酬
    var ewMkt = new List<double>(); var btc = new List<double>();
    for (int i = 20; i < dates.Count - 1; i++)
    {
        var today = closesByDate[dates[i]]; var next = closesByDate[dates[i + 1]];
        var rs = new List<double>();
        foreach (var s in symbols) if (today.TryGetValue(s, out var tc) && tc > 0 && next.TryGetValue(s, out var nc)) rs.Add((double)(nc / tc - 1m));
        ewMkt.Add(rs.Count > 0 ? rs.Average() : 0);
        btc.Add(today.TryGetValue("BTCUSDT", out var bt) && bt > 0 && next.TryGetValue("BTCUSDT", out var bn) ? (double)(bn / bt - 1m) : 0);
    }
    int n = Math.Min(momBest.Count, ewMkt.Count);
    var m = momBest.Take(n).ToArray();
    double cMkt = Pearson(m, ewMkt.Take(n).ToArray()), cBtc = Pearson(m, btc.Take(n).ToArray());
    Console.WriteLine($"  corr(xsec mom, 等權市場) = {cMkt:+0.000;-0.000}   corr(xsec mom, BTC) = {cBtc:+0.000;-0.000}");
    Console.WriteLine($"  → 接近 0 = 市場中性、跟方向性 alpha 結構去相關");
    // 邊際貢獻示意:兩個等波動 sleeve、Sharpe 各 sA/sB、相關 ρ → 組合 Sharpe = (sA+sB)/sqrt(2(1+ρ))
    double sMom = 1.28, sBook = 1.29; // xsec mom 淨 / 現有 10 條分散組合(bear audit)
    foreach (double rho in new[] { 0.0, cMkt, 0.3 })
    {
        double comb = (sMom + sBook) / Math.Sqrt(2 * (1 + rho));
        Console.WriteLine($"  若 corr={rho:+0.00;-0.00}:xsec mom(Sh {sMom}) + 現有組合(Sh {sBook})等波動合併 → Sharpe ~{comb:F2}");
    }
}

Console.WriteLine($"\n判讀:市場中性日 spread、t>2 = 橫斷面因子 edge;淨利(扣成本)後 t>2 + maxDD 低 + 跟方向性 alpha 去相關 = 救得了組合的新因子");

// 跑一個 lookback/hold/cost 組合,回 (momentum 日報酬序列, reversion 日報酬序列, 平均換手率)
// holdDays:持有 K 天才重排(降換手);costPerSide:每邊單次成本,在調倉日按換手扣
(List<double> mom, List<double> rev, double avgTurnover) RunXsec(int lookback, int holdDays, double costPerSide)
{
    var momR = new List<double>(); var revR = new List<double>();
    var turnovers = new List<double>();
    HashSet<string> prevLong = new(), prevShort = new();
    int sinceRebal = holdDays;   // 強制首日 rebalance
    List<string> curLong = new(), curShort = new();
    for (int i = lookback; i < dates.Count - 1; i++)
    {
        var today = closesByDate[dates[i]];
        var past = closesByDate[dates[i - lookback]];
        var next = closesByDate[dates[i + 1]];
        // rebalance 日:重排 top/bot third
        if (sinceRebal >= holdDays)
        {
            var rows = new List<(string sym, double trail)>();
            foreach (var sym in symbols)
                if (today.TryGetValue(sym, out var tc) && past.TryGetValue(sym, out var pc) && pc > 0 && tc > 0)
                    rows.Add((sym, (double)(tc / pc - 1m)));
            if (rows.Count < 6) continue;
            var ranked = rows.OrderBy(r => r.trail).ToList();
            int third = Math.Max(1, ranked.Count / 3);
            var newShort = ranked.Take(third).Select(r => r.sym).ToList();        // 輸家
            var newLong = ranked.TakeLast(third).Select(r => r.sym).ToList();     // 贏家
            // 換手 = 變動的部位比例(long+short 合計)
            int changed = newLong.Count(s => !prevLong.Contains(s)) + newShort.Count(s => !prevShort.Contains(s));
            int total = newLong.Count + newShort.Count;
            double turnover = total > 0 ? (double)changed / total : 0;
            turnovers.Add(turnover);
            curLong = newLong; curShort = newShort;
            prevLong = new(newLong); prevShort = new(newShort);
            sinceRebal = 0;
            // 調倉成本:換手 × costPerSide × 2(進出)、攤到當日報酬
            double cost = turnover * costPerSide * 2;
            ApplyDay(today, next, curLong, curShort, momR, revR, cost);
        }
        else
        {
            ApplyDay(today, next, curLong, curShort, momR, revR, 0);
        }
        sinceRebal++;
    }
    return (momR, revR, turnovers.Count > 0 ? turnovers.Average() : 0);
}

void ApplyDay(Dictionary<string, decimal> today, Dictionary<string, decimal> next,
    List<string> longs, List<string> shorts, List<double> momR, List<double> revR, double cost)
{
    double Avg(List<string> ss)
    {
        var rs = new List<double>();
        foreach (var s in ss)
            if (today.TryGetValue(s, out var tc) && tc > 0 && next.TryGetValue(s, out var nc))
                rs.Add((double)(nc / tc - 1m));
        return rs.Count > 0 ? rs.Average() : 0;
    }
    double topFwd = Avg(longs), botFwd = Avg(shorts);
    momR.Add((topFwd - botFwd) - cost);   // 動量淨報酬
    revR.Add((botFwd - topFwd) - cost);
}

static double Pearson(double[] x, double[] y)
{
    int n = Math.Min(x.Length, y.Length);
    if (n < 3) return 0;
    double mx = x.Take(n).Average(), my = y.Take(n).Average(), sxy = 0, sxx = 0, syy = 0;
    for (int i = 0; i < n; i++) { double dx = x[i] - mx, dy = y[i] - my; sxy += dx * dy; sxx += dx * dx; syy += dy * dy; }
    return sxx < 1e-12 || syy < 1e-12 ? 0 : sxy / Math.Sqrt(sxx * syy);
}

static void Report(string label, List<double> daily)
{
    if (daily.Count < 30) { Console.WriteLine($"  {label}: 樣本 {daily.Count} 太少"); return; }
    double mean = daily.Average();
    double sd = Math.Sqrt(daily.Select(x => (x - mean) * (x - mean)).Sum() / (daily.Count - 1));
    double t = sd > 1e-12 ? mean / (sd / Math.Sqrt(daily.Count)) : 0;
    double sharpe = sd > 1e-12 ? mean / sd * Math.Sqrt(365) : 0;   // 年化 Sharpe
    double annual = mean * 365 * 100;
    // maxDD of cumulative spread equity (每日 spread 當日報酬複利)
    double eq = 1, peak = 1, maxDd = 0;
    foreach (var r in daily) { eq *= (1 + r); if (eq > peak) peak = eq; double dd = (peak - eq) / peak; if (dd > maxDd) maxDd = dd; }
    string flag = Math.Abs(t) > 2 ? " ✅" : (Math.Abs(t) > 1.5 ? " ~" : "");
    Console.WriteLine($"  {label}: 日均 {mean*100:+0.000;-0.000}% / 年化~{annual:+0;-0}% / Sh {sharpe:F2} / maxDD {maxDd*100:F0}% / t={t:+0.00;-0.00} (n={daily.Count}){flag}");
}
