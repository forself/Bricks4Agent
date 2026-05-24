// 廣宇宙策略驗證:12 檔幣 × walk-forward OOS。同時跑 long-only 與 long-short 兩種引擎對照,
// 對照 buy&hold、給可用判定 + 相關矩陣 + 低相關「組合層」回測(下一步)。
// 單一參數集、要求跨多檔通用(不 per-symbol 調參 = 非 curve-fit)。
using BrokerCore.Trading;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using System.Globalization;
using System.Text.Json;

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var symbols = new[]
{
    "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT", "ADAUSDT",
    "DOGEUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT", "DOTUSDT", "ATOMUSDT",
};

(string name, IStrategy s)[] strats =
{
    // 實際部署中(live/shadow)— 這次重點:看它們在哪個幣最強,精準分配
    ("smc",              new SmcStrategy()),
    ("mfi",              new MfiStrategy()),
    ("rsi_stoch",        new StochasticStrategy()),
    ("don_trend",        new DonTrendStrategy()),
    ("rsi2_rev",         new Rsi2RevStrategy()),
    ("boll_rev",         new BollRevStrategy()),
    // 第一批(趨勢家族,原本偏多用、實為多空對稱)
    ("ts_momentum",      new TsMomentumStrategy()),
    ("chandelier_trend", new ChandelierTrendStrategy()),
    ("ma_regime_trend",  new MaRegimeTrendStrategy()),
    ("dual_thrust",      new DualThrustStrategy()),
    ("accel_momentum",   new AccelMomentumStrategy()),
    // 第二批(原生多空)
    ("dual_mom_ls",      new DualMomentumLsStrategy()),
    ("di_trend_ls",      new DiTrendLsStrategy()),
    ("supertrend_ls",    new SuperTrendLsStrategy()),
    ("bb_revert_ls",     new BollingerRevertLsStrategy()),
    ("donchian_fade_ls", new DonchianFadeLsStrategy()),
    // 第三批(諧波 / 斐波那契)
    ("fib_retrace_ls",   new FibRetraceLsStrategy()),
    ("harmonic_ls",      new HarmonicLsStrategy()),
    // 一鍵淨加權 ensemble(去相關4支、反波動率權重)— 對照「真組合」
    ("decorr4_ls", new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        { (new DualMomentumLsStrategy(), 0.38m), (new DualThrustStrategy(), 0.32m),
          (new BollingerRevertLsStrategy(), 0.19m), (new FibRetraceLsStrategy(), 0.10m) }, name: "decorr4_ls")),
};

async Task<List<BarData>> Fetch(string sym, string interval = "1d")
{
    var url = $"https://api.binance.com/api/v3/klines?symbol={sym}&interval={interval}&limit=1000";
    var json = await http.GetStringAsync(url);
    using var doc = JsonDocument.Parse(json);
    var bars = new List<BarData>();
    foreach (var k in doc.RootElement.EnumerateArray())
        bars.Add(new BarData
        {
            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()).UtcDateTime,
            Open = decimal.Parse(k[1].GetString()!, CultureInfo.InvariantCulture),
            High = decimal.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
            Low = decimal.Parse(k[3].GetString()!, CultureInfo.InvariantCulture),
            Close = decimal.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
            Volume = decimal.Parse(k[5].GetString()!, CultureInfo.InvariantCulture),
        });
    return bars;
}

decimal Median(List<decimal> xs)
{
    if (xs.Count == 0) return 0m;
    var s = xs.OrderBy(x => x).ToList(); int n = s.Count;
    return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2m;
}

// 從權益值序列算 (報酬%, 年化Sharpe, maxDD%)
(decimal ret, decimal sharpe, decimal dd) StatsOf(List<decimal> eq)
{
    if (eq.Count < 2) return (0, 0, 0);
    var rets = new List<decimal>();
    for (int i = 1; i < eq.Count; i++) { if (eq[i - 1] > 0) rets.Add((eq[i] - eq[i - 1]) / eq[i - 1]); }
    decimal tot = eq[0] > 0 ? (eq[^1] - eq[0]) / eq[0] * 100m : 0m;
    decimal sh = 0m;
    if (rets.Count > 1)
    {
        var a = rets.Average();
        var sd = (decimal)Math.Sqrt((double)rets.Select(r => (r - a) * (r - a)).Average());
        sh = sd > 0 ? a / sd * (decimal)Math.Sqrt(252) : 0m;
    }
    decimal peak = eq[0], mdd = 0m;
    foreach (var v in eq) { if (v > peak) peak = v; var d = peak > 0 ? (peak - v) / peak * 100m : 0m; if (d > mdd) mdd = d; }
    return (Math.Round(tot, 1), Math.Round(sh, 2), Math.Round(mdd, 1));
}

var data = new Dictionary<string, List<BarData>>();
foreach (var sym in symbols)
{
    try { var b = await Fetch(sym); if (b.Count >= 400) data[sym] = b; }
    catch (Exception ex) { Console.WriteLine($"{sym}: {ex.Message}"); }
}
Console.WriteLine($"\n資料就緒:{data.Count}/{symbols.Length} 檔(≥400 日線)");

var bhRets = new List<decimal>(); var bhShs = new List<decimal>(); var bhDds = new List<decimal>();
foreach (var kv in data) { var st = StatsOf(kv.Value.Select(b => b.Close).ToList()); bhRets.Add(st.ret); bhShs.Add(st.sharpe); bhDds.Add(st.dd); }
if (data.Count > 0)
    Console.WriteLine($"=== Buy & Hold 基準(全期)===  中位 ret {Median(bhRets):F0}% | 平均 Sharpe {bhShs.Average():F2} | 平均 maxDD {bhDds.Average():F0}%");

// 一張表 = 一種引擎(long-only / long-short);回傳 [strat][symbol]=全期權益曲線(給相關/組合用)
Dictionary<string, Dictionary<string, List<decimal>>> PrintTable(
    string title,
    Func<IStrategy, List<BarData>, StrategyConfig, BacktestEngine.WalkForwardResult> wf,
    Func<IStrategy, List<BarData>, StrategyConfig, BacktestEngine.BacktestResult> run)
{
    Console.WriteLine($"\n=== {title} 可用性(OOS train250/test90/stride60 跨檔;Full=全期連續、無調參)===");
    Console.WriteLine($"  {"strategy",-16}{"OOSsym+%",9}{"OOSmed%",9}{"+fold%",8}│{"fullRet%",10}{"fullSh",8}{"fullDD%",9}{"DD<BH%",8}  判定");
    var eqAll = new Dictionary<string, Dictionary<string, List<decimal>>>();
    foreach (var (name, s) in strats)
    {
        int symPos = 0, symTot = 0, ddBeat = 0;
        var oosRets = new List<decimal>(); var foldRets = new List<decimal>();
        var fullRets = new List<decimal>(); var fullShs = new List<decimal>(); var fullDds = new List<decimal>();
        var perSym = new Dictionary<string, List<decimal>>();
        foreach (var kv in data)
        {
            var cfg = new StrategyConfig { Symbol = kv.Key, Interval = "1d" };
            BacktestEngine.WalkForwardResult w;
            try { w = wf(s, kv.Value, cfg); } catch { continue; }
            if (w.TotalFolds == 0) continue;
            symTot++;
            if (w.AvgTestReturnPct > 0) symPos++;
            oosRets.Add(w.AvgTestReturnPct);
            foreach (var f in w.Folds.Where(f => f.Test != null)) foldRets.Add(f.Test!.TotalReturnPct);
            var bt = run(s, kv.Value, cfg);
            fullRets.Add(bt.TotalReturnPct); fullShs.Add(bt.SharpeRatio); fullDds.Add(bt.MaxDrawdownPct);
            perSym[kv.Key] = bt.EquityCurve.Select(e => e.Value).ToList();
            if (bt.MaxDrawdownPct < StatsOf(kv.Value.Select(b => b.Close).ToList()).dd) ddBeat++;
        }
        if (symTot == 0) { Console.WriteLine($"  {name,-16} (無資料)"); continue; }
        decimal symPosPct = (decimal)symPos / symTot * 100m;
        decimal medRet = Median(oosRets);
        decimal foldPos = foldRets.Count > 0 ? (decimal)foldRets.Count(r => r > 0) / foldRets.Count * 100m : 0m;
        decimal fSh = fullShs.Average();
        bool usable = medRet > 0 && symPosPct >= 60m && fSh > 0m;
        Console.WriteLine($"  {name,-16}{symPosPct,8:F0}%{medRet,9:F1}{foldPos,7:F0}%│{fullRets.Average(),10:F0}{fSh,8:F2}{fullDds.Average(),9:F0}{(decimal)ddBeat / symTot * 100m,7:F0}%  {(usable ? "✅ 可用" : "❌")}");
        eqAll[name] = perSym;
    }
    return eqAll;
}

PrintTable("Long-only(Benson 引擎)", (s, b, c) => BacktestEngine.RunWalkForward(s, b, c, 250, 90, 60), (s, b, c) => BacktestEngine.Run(s, b, c));
var lsEq = PrintTable("Long-short(新引擎)", (s, b, c) => LongShortBacktestEngine.RunWalkForward(s, b, c, 250, 90, 60), (s, b, c) => LongShortBacktestEngine.Run(s, b, c));

// ── 多時框 策略 × 幣 分析(預設 1h~1w,找跨時框穩健最優解)──────────────
// long-only 引擎(對應實際 perp_long_only)。每時框 per(策略,幣) OOS = walk-forward avg test%。
// 跨時框一致 = 真 edge 的證據;只在單一時框好 = 多半噪音/單一行情(如 XRP 那波大漲)。
string[] intervals = { "1h", "4h", "12h", "1d", "1w" };
string Sh(string s) => s.Replace("USDT", "");

// 跑單一時框的 策略×幣 grid(printGrid=true 才印完整表),回傳 strat->coin->OOS%
Dictionary<string, Dictionary<string, decimal>> PerCoinMatrix(string iv, Dictionary<string, List<BarData>> dv, bool printGrid)
{
    var coins = dv.Keys.ToList();
    var outp = new Dictionary<string, Dictionary<string, decimal>>();
    if (printGrid)
    {
        Console.WriteLine($"\n=== [{iv}] 策略 × 幣 OOS 報酬%矩陣(long-only、walk-forward avg test%;★=該策略最佳幣)===");
        Console.WriteLine("  " + new string(' ', 14) + string.Join("", coins.Select(c => $"{Sh(c),7}")) + "  │ 最佳 / 中位");
    }
    foreach (var (name, s) in strats)
    {
        var row = new Dictionary<string, decimal>();
        foreach (var c in coins)
        {
            try { var w = BacktestEngine.RunWalkForward(s, dv[c], new StrategyConfig { Symbol = c, Interval = iv }, 250, 90, 60);
                  if (w.TotalFolds > 0) row[c] = Math.Round(w.AvgTestReturnPct, 1); }
            catch { }
        }
        if (row.Count == 0) continue;
        outp[name] = row;
        if (printGrid)
        {
            var best = row.OrderByDescending(kv => kv.Value).First();
            var med = Median(row.Values.ToList());
            var cells = coins.Select(c => !row.ContainsKey(c) ? $"{"-",7}" : $"{row[c],6:F0}{(c == best.Key ? "★" : " ")}");
            Console.WriteLine($"  {name,-14}{string.Join("", cells)}  │ {Sh(best.Key)} {best.Value,4:F0}% / 中位{med,4:F0}%");
        }
    }
    return outp;
}

// 跑每個時框(1d 重用上面抓的;其餘現抓);只印 1d 完整 grid、其餘只收數據
var tf = new Dictionary<string, Dictionary<string, Dictionary<string, decimal>>>();
foreach (var iv in intervals)
{
    Dictionary<string, List<BarData>> dv;
    if (iv == "1d") dv = data;
    else { dv = new(); foreach (var sym in symbols) { try { var b = await Fetch(sym, iv); if (b.Count >= 350) dv[sym] = b; } catch { } } }
    tf[iv] = PerCoinMatrix(iv, dv, printGrid: iv == "1d");
    Console.WriteLine($"  [時框 {iv}] {dv.Count} 幣 × {tf[iv].Count} 策略 跑完");
}

// (1) 每策略 跨時框中位 OOS(穩定 = 多時框都正)
Console.WriteLine("\n=== 每策略 跨時框中位 OOS%(穩定 = 多時框都正)===");
Console.WriteLine("  " + $"{"strategy",-16}" + string.Join("", intervals.Select(iv => $"{iv,6}")) + $"   平均  正/{intervals.Length}");
var stratScore = new Dictionary<string, (decimal avgMed, int posTf)>();
foreach (var (name, _) in strats)
{
    var meds = new List<decimal>(); var cells = new List<string>(); int pos = 0;
    foreach (var iv in intervals)
    {
        if (tf[iv].TryGetValue(name, out var row) && row.Count > 0)
        { var m = Median(row.Values.ToList()); meds.Add(m); if (m > 0) pos++; cells.Add($"{m,6:F0}"); }
        else cells.Add($"{"-",6}");
    }
    var avg = meds.Count > 0 ? Math.Round(meds.Average(), 1) : 0;
    stratScore[name] = (avg, pos);
    Console.WriteLine($"  {name,-16}{string.Join("", cells)}   {avg,5:F1}  {pos}/{intervals.Length}");
}

// (2) 每幣 跨時框最佳策略(要求 ≥3/5 時框為正,濾單時框噪音;取跨時框平均最高)
Console.WriteLine("\n=== 每幣 跨時框最佳 long-only 策略(要求 ≥3/5 時框正、取跨時框平均最高)===");
var finalPick = new Dictionary<string, (string name, decimal avg, int pos)>();
foreach (var coin in data.Keys)
{
    var cand = new List<(string name, decimal avg, int pos)>();
    foreach (var (name, _) in strats)
    {
        var vals = new List<decimal>(); int pos = 0;
        foreach (var iv in intervals)
            if (tf[iv].TryGetValue(name, out var row) && row.TryGetValue(coin, out var v)) { vals.Add(v); if (v > 0) pos++; }
        if (vals.Count >= 3) cand.Add((name, Math.Round(vals.Average(), 1), pos));
    }
    var ok = cand.Where(c => c.pos >= 3).OrderByDescending(c => c.avg).ToList();
    if (ok.Count == 0) ok = cand.OrderByDescending(c => c.avg).ToList();
    if (ok.Count == 0) continue;
    finalPick[coin] = ok[0];
    var alt = string.Join(", ", ok.Skip(1).Take(2).Select(c => $"{c.name}({c.avg:F0},{c.pos}/5)"));
    Console.WriteLine($"    {Sh(coin),-6}→ {ok[0].name,-16} 跨時框avg {ok[0].avg,4:F0}% (正{ok[0].pos}/5)   次:{alt}");
}

// (3) 穩健策略總排名(正時框數優先,再平均中位)
Console.WriteLine("\n=== 穩健策略總排名(正時框數 → 平均中位 OOS%)===");
foreach (var kv in stratScore.OrderByDescending(x => x.Value.posTf).ThenByDescending(x => x.Value.avgMed))
    Console.WriteLine($"    {kv.Key,-16} 正時框 {kv.Value.posTf}/{intervals.Length}  平均中位 {kv.Value.avgMed,5:F1}%");

// (4) 成本敏感度(1d):edge 扣手續費+滑點後還剩多少 + 交易頻率(頻率高=被成本磨更兇)
// 上面矩陣已含預設 0.1%/邊手續費;這裡明列三種成本看 edge 衰減。
// 註:資金費(funding)未計 — Binance K 線不帶 funding_rate;多單在多頭通常「付」funding,
//     故真實淨值還會比下表 realistic 再差一點(尤其長抱)。
decimal MedOos1d(IStrategy s, decimal comm, decimal slip)
{
    var oos = new List<decimal>();
    foreach (var kv in data)
        try { var w = BacktestEngine.RunWalkForward(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, 250, 90, 60, commission: comm, slippagePct: slip); if (w.TotalFolds > 0) oos.Add(w.AvgTestReturnPct); }
        catch { }
    return oos.Count > 0 ? Median(oos) : 0;
}
decimal AvgTrades1k(IStrategy s)
{
    var tr = new List<decimal>();
    foreach (var kv in data)
        try { var bt = BacktestEngine.Run(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }); tr.Add(bt.TotalTrades / (decimal)kv.Value.Count * 1000m); }
        catch { }
    return tr.Count > 0 ? Math.Round(tr.Average(), 1) : 0;
}
Console.WriteLine("\n=== 成本敏感度(1d、跨幣中位 OOS%)===");
Console.WriteLine($"  {"strategy",-16}{"gross",8}{"realistic",11}{"pessim",9}{"trades/千根",13}");
Console.WriteLine($"  {"",-16}{"(0)",8}{"(.05費+.03滑)",11}{"(.15/邊)",9}");
foreach (var (name, s) in strats)
{
    var g = MedOos1d(s, 0m, 0m);
    var r = MedOos1d(s, 0.0005m, 0.0003m);
    var p = MedOos1d(s, 0.0008m, 0.0007m);
    Console.WriteLine($"  {name,-16}{g,8:F1}{r,11:F1}{p,9:F1}{AvgTrades1k(s),13:F1}");
}
Console.WriteLine("  → realistic 仍正 = edge 撐得過成本;gross 正但 realistic 轉負 = 被手續費吃光(常見高頻)。");

// 相關矩陣(long-short, BTC 全期權益報酬)
if (lsEq.Count >= 2 && lsEq.Values.First().ContainsKey("BTCUSDT"))
{
    var names = lsEq.Keys.Where(n => lsEq[n].ContainsKey("BTCUSDT")).ToList();
    int len = names.Min(n => lsEq[n]["BTCUSDT"].Count);
    Console.WriteLine("\n=== Long-short 相關矩陣(BTC 全期權益報酬)===");
    Console.WriteLine("  " + new string(' ', 16) + string.Join("", names.Select(n => $"{n.Substring(0, Math.Min(8, n.Length)),10}")));
    foreach (var a in names)
    {
        var ea = lsEq[a]["BTCUSDT"].Take(len).ToList();
        var cells = names.Select(b => $"{CorrelationGuard.PearsonOfReturns(ea, lsEq[b]["BTCUSDT"].Take(len).ToList()),10:F2}");
        Console.WriteLine($"  {a,-16}" + string.Join("", cells));
    }
}

// ── 組合層回測(long-short, 跨檔平均)──
// 用「報酬加權」建組合:每腿算日報酬,組合日報酬 = Σ w_i·r_i,再累乘成權益算 Sharpe/DD。
// riskWeighted=false → 等權(w=1/N);true → 反波動率加權(w∝1/vol、降權高波動腿)。
(decimal ret, decimal sharpe, decimal dd) Portfolio(List<string> members, bool riskWeighted = false)
{
    var rs = new List<decimal>(); var ss = new List<decimal>(); var ds = new List<decimal>();
    foreach (var kv in data)
    {
        var curves = members.Where(m => lsEq.ContainsKey(m) && lsEq[m].ContainsKey(kv.Key)).Select(m => lsEq[m][kv.Key]).ToList();
        if (curves.Count < members.Count) continue;
        int len = curves.Min(c => c.Count);
        if (len < 3) continue;

        // 每腿日報酬
        var legRets = new List<decimal[]>();
        foreach (var c in curves)
        {
            var r = new decimal[len - 1];
            for (int t = 1; t < len; t++) r[t - 1] = c[t - 1] > 0 ? (c[t] - c[t - 1]) / c[t - 1] : 0m;
            legRets.Add(r);
        }
        // 權重
        var w = new decimal[curves.Count];
        if (riskWeighted)
        {
            decimal wsum = 0m;
            for (int i = 0; i < curves.Count; i++)
            {
                var avg = legRets[i].Average();
                var vol = (decimal)Math.Sqrt((double)legRets[i].Select(x => (x - avg) * (x - avg)).Average());
                w[i] = vol > 0m ? 1m / vol : 0m; wsum += w[i];
            }
            if (wsum <= 0m) continue;
            for (int i = 0; i < w.Length; i++) w[i] /= wsum;
        }
        else for (int i = 0; i < w.Length; i++) w[i] = 1m / curves.Count;

        // 組合權益(初值 1、依加權日報酬累乘)
        var port = new List<decimal>(len) { 1m };
        for (int t = 0; t < len - 1; t++)
        {
            decimal pr = 0m;
            for (int i = 0; i < legRets.Count; i++) pr += w[i] * legRets[i][t];
            port.Add(port[^1] * (1m + pr));
        }
        var st = StatsOf(port);
        rs.Add(st.ret); ss.Add(st.sharpe); ds.Add(st.dd);
    }
    return (rs.Count > 0 ? Math.Round(rs.Average(), 1) : 0, ss.Count > 0 ? Math.Round(ss.Average(), 2) : 0, ds.Count > 0 ? Math.Round(ds.Average(), 1) : 0);
}

// 組合 OOS:每檔跑多空 RunPortfolioWalkForward、池化 test fold 報酬 → (avgOOS%, %fold正)
(decimal avgRet, decimal posPct) PortfolioOos(List<string> members)
{
    var subs = members.Select(m => strats.First(x => x.name == m).s).ToList();
    var foldRets = new List<decimal>();
    foreach (var kv in data)
    {
        var wf = LongShortBacktestEngine.RunPortfolioWalkForward(subs, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, 250, 90, 60);
        foreach (var f in wf.Folds.Where(f => f.Test != null)) foldRets.Add(f.Test!.TotalReturnPct);
    }
    if (foldRets.Count == 0) return (0, 0);
    return (Math.Round(foldRets.Average(), 1), Math.Round((decimal)foldRets.Count(r => r > 0) / foldRets.Count * 100m, 0));
}

void PrintCombo(string label, List<string> members)
{
    var fe = Portfolio(members, false);
    var fr = Portfolio(members, true);
    var o = PortfolioOos(members);
    Console.WriteLine($"  {label,-26} 等權[Sh {fe.sharpe,5:F2} DD {fe.dd,4:F0}%]  風險加權[Sh {fr.sharpe,5:F2} DD {fr.dd,4:F0}%]  OOS[avg {o.avgRet,5:F1}% +fold {o.posPct,3:F0}%]");
}

if (lsEq.Count >= 2 && lsEq.Values.First().ContainsKey("BTCUSDT"))
{
    var all = lsEq.Keys.ToList();
    int len = all.Min(n => lsEq[n]["BTCUSDT"].Count);
    decimal Corr(string a, string b) => CorrelationGuard.PearsonOfReturns(lsEq[a]["BTCUSDT"].Take(len).ToList(), lsEq[b]["BTCUSDT"].Take(len).ToList());
    decimal AvgSharpe(string n) => lsEq[n].Values.Select(c => StatsOf(c).sharpe).Average();

    Console.WriteLine("\n=== 組合層回測(long-short、等權;full=全期跨檔平均, OOS=walk-forward 池化)===");
    foreach (var name in all.OrderByDescending(AvgSharpe))
    {
        var rs = new List<decimal>(); var ss = new List<decimal>(); var ds = new List<decimal>();
        foreach (var kv in lsEq[name]) { var st = StatsOf(kv.Value); rs.Add(st.ret); ss.Add(st.sharpe); ds.Add(st.dd); }
        Console.WriteLine($"  單腿 {name,-18} full[ret {rs.Average(),5:F0}% Sh {ss.Average(),5:F2} DD {ds.Average(),4:F0}%]");
    }

    PrintCombo($"組合 全部 {all.Count} 支等權", all);

    // 貪婪挑去相關組合:候選先濾掉負期望(Sharpe≤0,去相關但賠錢的不要),再從 Sharpe 高起、納入「對已選全部 |ρ|<0.4」者
    var ranked = all.Where(n => AvgSharpe(n) > 0m).OrderByDescending(AvgSharpe).ToList();
    var picked = new List<string> { ranked[0] };
    foreach (var cand in ranked.Skip(1))
        if (picked.All(p => Math.Abs(Corr(cand, p)) < 0.4m)) picked.Add(cand);
    PrintCombo($"組合 去相關精選({picked.Count}支)", picked);
    Console.WriteLine($"     去相關精選成員:{string.Join(", ", picked)}");

    // 印出去相關精選的「反波動率權重」(跨檔平均、正規化)→ 給實盤獨立部署配重用
    decimal AvgVol(string n)
    {
        var vols = new List<decimal>();
        foreach (var c in lsEq[n].Values)
        {
            if (c.Count < 3) continue;
            var rr = new List<decimal>();
            for (int t = 1; t < c.Count; t++) if (c[t - 1] > 0) rr.Add((c[t] - c[t - 1]) / c[t - 1]);
            if (rr.Count < 2) continue;
            var a = rr.Average();
            vols.Add((decimal)Math.Sqrt((double)rr.Select(x => (x - a) * (x - a)).Average()));
        }
        return vols.Count > 0 ? vols.Average() : 0m;
    }
    var invVol = picked.ToDictionary(n => n, n => { var v = AvgVol(n); return v > 0 ? 1m / v : 0m; });
    var wsum2 = invVol.Values.Sum();
    Console.WriteLine("     建議風險加權(反波動率、正規化):" +
        string.Join("  ", picked.Select(n => $"{n} {(wsum2 > 0 ? invVol[n] / wsum2 : 0m):P0}")));
}

// ── decorr4_ls 一鍵淨加權 ensemble:fixed-notional vs confidence-sizing,對照真組合 ──
if (strats.Any(x => x.name == "decorr4_ls"))
{
    var ens = strats.First(x => x.name == "decorr4_ls").s;
    (decimal ret, decimal sharpe, decimal dd, decimal oos) EnsAgg(bool confSizing)
    {
        var rs = new List<decimal>(); var ss = new List<decimal>(); var ds = new List<decimal>(); var oosR = new List<decimal>();
        foreach (var kv in data)
        {
            var cfg = new StrategyConfig { Symbol = kv.Key, Interval = "1d" };
            var bt = LongShortBacktestEngine.Run(ens, kv.Value, cfg, confidenceSizing: confSizing);
            rs.Add(bt.TotalReturnPct); ss.Add(bt.SharpeRatio); ds.Add(bt.MaxDrawdownPct);
            var wf = LongShortBacktestEngine.RunWalkForward(ens, kv.Value, cfg, 250, 90, 60, confidenceSizing: confSizing);
            if (wf.TotalFolds > 0) oosR.Add(wf.AvgTestReturnPct);
        }
        return (Math.Round(rs.Average(), 0), Math.Round(ss.Average(), 2), Math.Round(ds.Average(), 0), oosR.Count > 0 ? Math.Round(oosR.Average(), 1) : 0);
    }
    var fix = EnsAgg(false);
    var cs = EnsAgg(true);
    Console.WriteLine("\n=== decorr4_ls 一鍵淨加權 ensemble(單一 watch 可部署)===");
    Console.WriteLine($"  固定部位         full[ret {fix.ret,4:F0}% Sh {fix.sharpe:F2} DD {fix.dd,3:F0}%] OOS {fix.oos:F1}%");
    Console.WriteLine($"  confidence-sizing full[ret {cs.ret,4:F0}% Sh {cs.sharpe:F2} DD {cs.dd,3:F0}%] OOS {cs.oos:F1}%  ← 分歧縮量、最接近真組合");
    Console.WriteLine("  對照 真組合(4獨立·風險加權):Sh 0.62 / DD 46%(見上方組合層)");
}

Console.WriteLine("\n判定 = OOS 中位報酬>0 且 ≥60% 檔 OOS 正報酬 且 全期連續 Sharpe>0。組合層比較單腿:Sharpe 升 / maxDD 降 = 去相關紅利。");
