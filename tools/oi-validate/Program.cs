// OI / L-S ratio / taker volume vs funding & price 相關性 + t-stat 探勘
//
// Q2 第一個結構性 alpha 候選驗證:
//   問題:OI momentum 跟 funding momentum 是不是同一條 alpha?
//   方法:拉 BTC 1y daily,計算 5 個指標 → 算 corr matrix + per-signal Pearson 對 next-day return
//   目標:找出與 funding 相關 < 0.3、且對 next return Pearson > 0.05 的指標
//
// 用法: dotnet run --project tools/oi-validate -- BTCUSDT [ETHUSDT BNBUSDT...]
//
using StrategyWorker.Engine;
using ToolsShared;

// 支援 --days-back N(從 endDate 往前推 N 天作 endDate、用於 OOS 驗證):
//   dotnet run -- BTCUSDT ETHUSDT --days-back 365   等於跑「去年的去年」
int daysBack = 0;
var symList = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--days-back" && i + 1 < args.Length) { int.TryParse(args[++i], out daysBack); }
    else symList.Add(args[i]);
}
var symbols = symList.Count > 0 ? symList.ToArray() : new[] { "BTCUSDT" };
var endDate = DateTime.UtcNow.Date.AddDays(-daysBack);
var startDate = endDate.AddDays(-365);

Console.WriteLine($"=== OI/L-S/Taker vs Funding/Price correlation probe ===");
Console.WriteLine($"Window: {startDate:yyyy-MM-dd} → {endDate:yyyy-MM-dd}" + (daysBack > 0 ? $" (OOS: {daysBack} 天前)" : " (in-sample)"));

// Pool 累積:跨所有幣的 (signal, nextRet) pair,跑全 pool t-stat
var poolFund = new List<double>(); var poolOi = new List<double>();
var poolTls = new List<double>(); var poolRls = new List<double>(); var poolTaker = new List<double>();
var poolNext = new List<double>();

foreach (var sym in symbols)
{
    Console.WriteLine($"\n## {sym}");

    // 1. K 線
    var bars = await KlineCache.FetchOrLoad(sym, "1d");
    bars = bars.Where(b => b.OpenTime >= startDate && b.OpenTime <= endDate).ToList();
    if (bars.Count < 100) { Console.WriteLine($"  bars={bars.Count} 太少、skip"); continue; }

    // 2. Funding(注入到 bars)
    await FundingCache.InjectInto(bars, sym, "1d");

    // 3. OI metrics(拉 1 年 daily zip CSV)
    Console.Write($"  抓 OI metrics {sym} ");
    var snapsAll = await OiMetricsCache.FetchOrLoad(sym, startDate, endDate);
    Console.WriteLine($"→ {snapsAll.Count} 個 5min snapshot");
    if (snapsAll.Count < 1000) { Console.WriteLine($"  OI 樣本太少、skip"); continue; }

    var daily = OiMetricsCache.AggregateDaily(snapsAll);

    // 4. 對齊:用 bar.OpenTime.Date 作 key
    var byDay = daily.ToDictionary(d => d.Day, d => d);
    var rows = new List<(decimal funding, decimal oiPct, decimal topLs, decimal retailLs, decimal takerLs, decimal nextRet, decimal todayRet)>();
    for (int i = 0; i < bars.Count - 1; i++)
    {
        if (!byDay.TryGetValue(bars[i].OpenTime.Date, out var d)) continue;
        decimal nextRet = bars[i + 1].Close / bars[i].Close - 1m;
        decimal todayRet = i > 0 ? bars[i].Close / bars[i - 1].Close - 1m : 0m;
        rows.Add(((bars[i].FundingRate ?? 0m), d.OiPctChange, d.AvgTopLsRatio, d.AvgRetailLsRatio, d.AvgTakerLsRatio, nextRet, todayRet));
    }
    if (rows.Count < 60) { Console.WriteLine($"  對齊樣本 {rows.Count} 太少、skip"); continue; }

    Console.WriteLine($"  對齊樣本: {rows.Count} 天");

    // 5. Pearson corr matrix
    var fund = rows.Select(r => (double)r.funding).ToArray();
    var oi = rows.Select(r => (double)r.oiPct).ToArray();
    var tls = rows.Select(r => (double)r.topLs).ToArray();
    var rls = rows.Select(r => (double)r.retailLs).ToArray();
    var taker = rows.Select(r => (double)r.takerLs).ToArray();
    var nextR = rows.Select(r => (double)r.nextRet).ToArray();
    var todayR = rows.Select(r => (double)r.todayRet).ToArray();

    Console.WriteLine($"\n  指標 vs funding(corr 高=不是新 alpha):");
    Console.WriteLine($"    OI %change          vs funding: {Pearson(oi, fund):+0.000;-0.000}");
    Console.WriteLine($"    Top L/S ratio       vs funding: {Pearson(tls, fund):+0.000;-0.000}");
    Console.WriteLine($"    Retail L/S ratio    vs funding: {Pearson(rls, fund):+0.000;-0.000}");
    Console.WriteLine($"    Taker buy/sell vol  vs funding: {Pearson(taker, fund):+0.000;-0.000}");

    Console.WriteLine($"\n  指標 vs price(高=lookahead 風險):");
    Console.WriteLine($"    OI %change          vs todayRet: {Pearson(oi, todayR):+0.000;-0.000}");
    Console.WriteLine($"    Top L/S ratio       vs todayRet: {Pearson(tls, todayR):+0.000;-0.000}");
    Console.WriteLine($"    Retail L/S ratio    vs todayRet: {Pearson(rls, todayR):+0.000;-0.000}");
    Console.WriteLine($"    Taker buy/sell vol  vs todayRet: {Pearson(taker, todayR):+0.000;-0.000}");

    Console.WriteLine($"\n  指標 vs next-day return(>0.05 = 可能有 edge,t-stat 補):");
    PrintEdge("    OI %change         ", oi, nextR);
    PrintEdge("    Top L/S ratio      ", tls, nextR);
    PrintEdge("    Retail L/S ratio   ", rls, nextR);
    PrintEdge("    Taker buy/sell vol ", taker, nextR);
    PrintEdge("    Funding (baseline) ", fund, nextR);

    // Quantile-based edge: 看極端值區段 vs 平均的 nextRet 差
    // 為什麼:funding raw Pearson t=-0.76 但 strat-validate quantile threshold t=+5.93,
    // 說明 edge 在非線性極端值區域(只 funding 暴衝才有 edge)。OI/L-S 可能同理。
    Console.WriteLine($"\n  Quantile edge(top/bot 20% vs 中段,t-stat > 2 = 非線性 edge):");
    PrintQuantileEdge("    OI %change         ", oi, nextR);
    PrintQuantileEdge("    Top L/S ratio      ", tls, nextR);
    PrintQuantileEdge("    Retail L/S ratio   ", rls, nextR);
    PrintQuantileEdge("    Taker buy/sell vol ", taker, nextR);
    PrintQuantileEdge("    Funding (baseline) ", fund, nextR);

    // Pool 累積(這幣的資料加入跨幣 pool)
    poolFund.AddRange(fund); poolOi.AddRange(oi);
    poolTls.AddRange(tls); poolRls.AddRange(rls); poolTaker.AddRange(taker);
    poolNext.AddRange(nextR);
}

// 跨幣 pool t-stat — 即使單幣不顯著,8 幣方向一致就有意義
if (symbols.Length > 1)
{
    Console.WriteLine($"\n=== 跨 {symbols.Length} 幣 POOL t-stat(總樣本 {poolNext.Count} 天)===");
    Console.WriteLine($"  Linear Pearson:");
    PrintEdge("    OI %change         ", poolOi.ToArray(), poolNext.ToArray());
    PrintEdge("    Top L/S ratio      ", poolTls.ToArray(), poolNext.ToArray());
    PrintEdge("    Retail L/S ratio   ", poolRls.ToArray(), poolNext.ToArray());
    PrintEdge("    Taker buy/sell vol ", poolTaker.ToArray(), poolNext.ToArray());
    PrintEdge("    Funding (baseline) ", poolFund.ToArray(), poolNext.ToArray());
    Console.WriteLine($"  Quantile (top/bot 20%):");
    PrintQuantileEdge("    OI %change         ", poolOi.ToArray(), poolNext.ToArray());
    PrintQuantileEdge("    Top L/S ratio      ", poolTls.ToArray(), poolNext.ToArray());
    PrintQuantileEdge("    Retail L/S ratio   ", poolRls.ToArray(), poolNext.ToArray());
    PrintQuantileEdge("    Taker buy/sell vol ", poolTaker.ToArray(), poolNext.ToArray());
    PrintQuantileEdge("    Funding (baseline) ", poolFund.ToArray(), poolNext.ToArray());
}

Console.WriteLine($"\n=== 結論判讀 ===");
Console.WriteLine($"  - corr(指標, funding) < 0.3 → 獨立 alpha 源");
Console.WriteLine($"  - |corr(指標, todayRet)| > 0.5 → lookahead 風險、慎用");
Console.WriteLine($"  - corr(指標, nextRet) > 0.05 且 t-stat > 2 → 有 edge,進策略開發");

static double Pearson(double[] x, double[] y)
{
    if (x.Length != y.Length || x.Length < 3) return 0;
    double mx = x.Average(), my = y.Average();
    double sxy = 0, sxx = 0, syy = 0;
    for (int i = 0; i < x.Length; i++)
    {
        double dx = x[i] - mx, dy = y[i] - my;
        sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
    }
    return sxx == 0 || syy == 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
}

static void PrintEdge(string label, double[] signal, double[] nextRet)
{
    double r = Pearson(signal, nextRet);
    // Pearson r → t-stat: r * sqrt(n-2) / sqrt(1-r²)
    int n = signal.Length;
    double t = r * Math.Sqrt(n - 2) / Math.Sqrt(Math.Max(1e-9, 1 - r * r));
    string flag = Math.Abs(t) > 2.0 ? " ✅" : (Math.Abs(t) > 1.5 ? " ~" : "");
    Console.WriteLine($"{label} vs nextRet: r={r:+0.000;-0.000}  t={t:+0.00;-0.00}{flag}");
}

// 把 signal 分成 top/bot 20% + mid 60%,看極端值區段的 nextRet 平均是否顯著不同於中段。
// 回報:top 平均 / bot 平均 / spread = top - bot / t-stat(Welch two-sample)
static void PrintQuantileEdge(string label, double[] signal, double[] nextRet)
{
    int n = signal.Length;
    var paired = signal.Select((s, i) => (s, r: nextRet[i])).OrderBy(p => p.s).ToList();
    int q = Math.Max(5, n / 5);   // top/bot 各 20%(至少 5 個)
    var botArr = paired.Take(q).Select(p => p.r).ToArray();
    var topArr = paired.TakeLast(q).Select(p => p.r).ToArray();
    double botMean = botArr.Average(), topMean = topArr.Average();
    double spread = topMean - botMean;
    // Welch t: (m1-m2) / sqrt(v1/n1 + v2/n2)
    double botVar = botArr.Length < 2 ? 0 : botArr.Sum(x => (x - botMean) * (x - botMean)) / (botArr.Length - 1);
    double topVar = topArr.Length < 2 ? 0 : topArr.Sum(x => (x - topMean) * (x - topMean)) / (topArr.Length - 1);
    double se = Math.Sqrt(botVar / botArr.Length + topVar / topArr.Length);
    double t = se < 1e-9 ? 0 : spread / se;
    string flag = Math.Abs(t) > 2.0 ? " ✅" : (Math.Abs(t) > 1.5 ? " ~" : "");
    Console.WriteLine($"{label} top {topMean*100:+0.00;-0.00}% / bot {botMean*100:+0.00;-0.00}% / spread {spread*100:+0.00;-0.00}pp  t={t:+0.00;-0.00}{flag}");
}
