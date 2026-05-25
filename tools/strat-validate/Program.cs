// 廣宇宙策略驗證:12 檔幣 × walk-forward OOS。同時跑 long-only 與 long-short 兩種引擎對照,
// 對照 buy&hold、給可用判定 + 相關矩陣 + 低相關「組合層」回測(下一步)。
// 單一參數集、要求跨多檔通用(不 per-symbol 調參 = 非 curve-fit)。
using BrokerCore.Trading;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using System.Globalization;
using System.Text.Json;

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
// 固定 funding 假設(long 付正值):env FUNDING_RATE_PER_8H、預設 0.01%/8h(=0.03%/日)。
// 多頭實際常 3-5x;engine 按實際持有期累計(持越久咬越多)。灌進每根 bar、只在 applyFunding 時生效。
decimal fundingPer8h = decimal.TryParse(Environment.GetEnvironmentVariable("FUNDING_RATE_PER_8H"), out var fpr) && fpr >= 0 ? fpr : 0.0001m;
var symbols = new[]
{
    "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT", "ADAUSDT",
    "DOGEUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT", "DOTUSDT", "ATOMUSDT",
    // 2026-05-25 擴幣宇宙 12→20(堆樣本:更多幣 = pooling 後 fold 更多、廣度濾網更有力)
    "TRXUSDT", "UNIUSDT", "NEARUSDT", "APTUSDT", "ARBUSDT", "OPUSDT", "SUIUSDT", "INJUSDT",
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

// ── --allocate:穩健配置引擎(獨立快速模式,不跑完整報告)──────────────
// 4 步:① 入場閘(顯著 + full 正 + sharpe>0) ② edge×逆波動 raw 權重 ③ 朝等權收縮(λ=T/T*)
//        + 相關 haircut + 單腿上限 ④ vol-target 算整體曝險。輸出每腿 budget_pct + N_eff + 畢業判定。
if (args.Contains("--allocate")) { await RunAllocate(); return; }

// ── --xsmom:橫斷面動量(cross-sectional momentum)——結構不同的去相關 edge 研究 ──
// 跨幣排序:每 rebal 期 long 過去 lookback 報酬最強的 topK、short 最弱的 topK(等權)。
// 跟所有「單幣技術指標」正交;驗證有沒有 OOS edge + 跟現有書(decorr4)相不相關。
if (args.Contains("--xsmom")) { await RunXsMom(); return; }

// ── --carry:資金費 carry 研究(現貨多+永續空收 funding、近零風險、跟價格動量正交)──
// 量化:各幣 funding 年化多少、穩不穩、扣成本後淨 carry、小本金值不值得。
if (args.Contains("--carry")) { await RunCarry(); return; }

// ── --xsrev:短週期橫斷面反轉(long 跌最兇/short 漲最兇)+ 與動量相關性 ──
if (args.Contains("--xsrev")) { await RunXsRev(); return; }
// ── --fundsig:資金費當跨幣訊號(contrarian:long 低 funding/short 高 funding)──
if (args.Contains("--fundsig")) { await RunFundSig(); return; }

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
            FundingRate = fundingPer8h,   // 固定假設;只在 applyFunding=true 的回測生效
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

var loEq = PrintTable("Long-only(Benson 引擎)", (s, b, c) => BacktestEngine.RunWalkForward(s, b, c, 250, 90, 60), (s, b, c) => BacktestEngine.Run(s, b, c));
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
decimal MedOos1d(IStrategy s, decimal comm, decimal slip, bool funding = false)
{
    var oos = new List<decimal>();
    foreach (var kv in data)
        try { var w = BacktestEngine.RunWalkForward(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, 250, 90, 60, commission: comm, slippagePct: slip, applyFunding: funding); if (w.TotalFolds > 0) oos.Add(w.AvgTestReturnPct); }
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
Console.WriteLine($"\n=== 成本敏感度(1d、跨幣中位 OOS%;funding 假設 long 付 {fundingPer8h:P3}/8h)===");
Console.WriteLine($"  {"strategy",-16}{"gross",8}{"realistic",11}{"real+fund",11}{"pessim",9}{"trades/千根",13}");
Console.WriteLine($"  {"",-16}{"(0)",8}{"(.05費+.03滑)",11}{"(+funding)",11}{"(.15/邊)",9}");
foreach (var (name, s) in strats)
{
    var g  = MedOos1d(s, 0m, 0m);
    var r  = MedOos1d(s, 0.0005m, 0.0003m);
    var rf = MedOos1d(s, 0.0005m, 0.0003m, funding: true);
    var p  = MedOos1d(s, 0.0008m, 0.0007m);
    Console.WriteLine($"  {name,-16}{g,8:F1}{r,11:F1}{rf,11:F1}{p,9:F1}{AvgTrades1k(s),13:F1}");
}
Console.WriteLine("  → real+fund = realistic 再加 funding;realistic 正但 real+fund 轉負 = 被資金費(長抱)拖垮。");

// (5) 統計顯著性(1d、realistic 成本):pool 跨幣×fold 的 OOS 報酬,bootstrap 95% CI。
// CI 下界 > 0 才算「edge 跟 0 有顯著差異」(不是運氣);否則就是 noise。
List<decimal> PoolOosFolds(IStrategy s)
{
    var r = new List<decimal>();
    foreach (var kv in data)
        try { var w = BacktestEngine.RunWalkForward(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, 250, 90, 60, commission: 0.0005m, slippagePct: 0.0003m);
              foreach (var f in w.Folds.Where(f => f.Test != null)) r.Add(f.Test!.TotalReturnPct); }
        catch { }
    return r;
}
var rng = new Random(42);
(decimal mean, decimal lo, decimal hi, double t) BootCI(List<decimal> xs)
{
    if (xs.Count < 5) return (0, 0, 0, 0);
    double mean = (double)xs.Average();
    double sd = Math.Sqrt(xs.Select(x => ((double)x - mean) * ((double)x - mean)).Sum() / (xs.Count - 1));
    double se = sd / Math.Sqrt(xs.Count);
    double tStat = se > 0 ? mean / se : 0;
    var means = new double[2000];
    for (int b = 0; b < 2000; b++) { double sum = 0; for (int i = 0; i < xs.Count; i++) sum += (double)xs[rng.Next(xs.Count)]; means[b] = sum / xs.Count; }
    Array.Sort(means);
    return ((decimal)mean, (decimal)means[(int)(0.025 * 2000)], (decimal)means[(int)(0.975 * 2000)], tStat);
}
Console.WriteLine("\n=== 統計顯著性(1d、realistic 成本;pool 跨幣×fold OOS%、bootstrap 95% CI)===");
Console.WriteLine($"  {"strategy",-16}{"n",5}{"mean%",8}   {"95% CI",16}{"t",7}  判定");
int sigTested = 0, sigPassed = 0;
var sigNames = new HashSet<string>();
var sigT = new Dictionary<string, double>();
foreach (var (name, s) in strats.OrderByDescending(x => { var p = PoolOosFolds(x.s); return p.Count > 0 ? p.Average() : -999m; }))
{
    var pool = PoolOosFolds(s);
    var (m, lo, hi, t) = BootCI(pool);
    bool sig = lo > 0m && pool.Count >= 5;
    sigT[name] = t; if (sig) sigNames.Add(name);
    sigTested++; if (sig) sigPassed++;
    Console.WriteLine($"  {name,-16}{pool.Count,5}{m,8:F1}   [{lo,6:F1},{hi,6:F1}]{t,7:F2}  {(sig ? "✅ 顯著" : "—")}");
}
Console.WriteLine($"  測 {sigTested} 支、{sigPassed} 支 95%CI 下界>0。⚠ 但 folds 非獨立(重疊窗+幣高相關)→ CI 偏窄、t 偏高、顯著性高估;");
Console.WriteLine($"     且多重檢定下純運氣約 {sigTested * 0.05:F0} 支會假陽性 → 只信「t 高且 mean 大」的前幾名才穩。");

// (6) 顯著策略的最佳組合(只用統計顯著那群、去相關 |ρ|<0.4、反波動率配重)
// 全程用 long-only 權益(loEq)→ 跟顯著性(long-only OOS)+ 實際 perp_long_only 部署一致。
if (loEq.Count >= 2 && loEq.Values.Any(v => v.ContainsKey("BTCUSDT")))
{
    var members = sigNames.Where(n => loEq.ContainsKey(n) && loEq[n].ContainsKey("BTCUSDT")).ToList();
    if (members.Count >= 2)
    {
        // 相關用跨幣平均(不只 BTC)→ 對 long-only 更有代表性
        decimal Corr2(string a, string b)
        {
            var coins = loEq[a].Keys.Intersect(loEq[b].Keys).ToList();
            var cs = new List<decimal>();
            foreach (var co in coins)
            {
                int n = Math.Min(loEq[a][co].Count, loEq[b][co].Count);
                if (n < 3) continue;
                cs.Add(CorrelationGuard.PearsonOfReturns(loEq[a][co].Take(n).ToList(), loEq[b][co].Take(n).ToList()));
            }
            return cs.Count > 0 ? cs.Average() : 0m;
        }
        decimal AvgVol2(string n)
        {
            var vs = new List<decimal>();
            foreach (var c in loEq[n].Values)
            {
                if (c.Count < 3) continue;
                var rr = new List<decimal>();
                for (int t = 1; t < c.Count; t++) if (c[t - 1] > 0) rr.Add((c[t] - c[t - 1]) / c[t - 1]);
                if (rr.Count < 2) continue;
                var a = rr.Average();
                vs.Add((decimal)Math.Sqrt((double)rr.Select(x => (x - a) * (x - a)).Average()));
            }
            return vs.Count > 0 ? vs.Average() : 0m;
        }
        var ranked = members.OrderByDescending(n => sigT.GetValueOrDefault(n)).ToList();   // t 高的先選
        var picked = new List<string> { ranked[0] };
        foreach (var c in ranked.Skip(1))
            if (picked.All(p => Math.Abs(Corr2(c, p)) < 0.4m)) picked.Add(c);
        var invVol = picked.ToDictionary(n => n, n => { var v = AvgVol2(n); return v > 0 ? 1m / v : 0m; });
        var wsum = invVol.Values.Sum();
        Console.WriteLine("\n=== 顯著策略最佳組合(long-only、去相關精選 + 反波動率配重)===");
        Console.WriteLine($"  顯著候選({ranked.Count}): {string.Join(", ", ranked)}");
        Console.WriteLine($"  去相關精選({picked.Count}): {string.Join(", ", picked)}");
        Console.WriteLine("  建議配重(反波動率): " + string.Join("  ", picked.Select(n => $"{n} {(wsum > 0 ? invVol[n] / wsum : 0m):P0}")));
    }
}

// (7) 參數調校實驗:grid search + walk-forward,驗證「調參到底有沒有讓 OOS 變好」。
// degradation = OOS Sharpe / IS Sharpe;接近 1=參數穩健,遠<1 或負 = IS 漂亮 OOS 垃圾 = 過擬合。
// 只有 sma_cross / rsi_oversold 有 ParameterOptimizer grid;用它們示範原理(其餘策略本就單一固定參數、不調)。
Console.WriteLine("\n=== 參數調校實驗(anchored walk-forward;IS 找最佳參數 → OOS 驗證)===");
Console.WriteLine($"  {"strategy",-14}{"IS Sharpe",11}{"OOS Sharpe",12}{"degradation",13}{"OOS ret%",10}");
void WfOpt(string label, Func<List<BarData>, StrategyConfig, WalkForwardOptimizer.WalkForwardResult> run)
{
    var isS = new List<decimal>(); var oosS = new List<decimal>(); var oosR = new List<decimal>();
    foreach (var kv in data)
        try { var r = run(kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" });
              if (r.WindowCount > 0) { isS.Add(r.AvgInSampleSharpe); oosS.Add(r.AvgOutOfSampleSharpe); oosR.Add(r.AggregateOosReturnPct); } }
        catch { }
    if (isS.Count == 0) { Console.WriteLine($"  {label,-14}(無資料)"); return; }
    var avgIs = isS.Average(); var avgOos = oosS.Average();
    var deg = avgIs != 0 ? avgOos / avgIs : 0;
    Console.WriteLine($"  {label,-14}{avgIs,11:F2}{avgOos,12:F2}{deg,13:F2}{oosR.Average(),10:F1}");
}
WfOpt("sma_cross",    (b, c) => WalkForwardOptimizer.RunSma(b, c, 250, 90));
WfOpt("rsi_oversold", (b, c) => WalkForwardOptimizer.RunRsi(b, c, 250, 90));
Console.WriteLine("  → degradation 遠<1 = IS 找的最佳參數 OOS 站不住 = 調參=過擬合 → 印證「單一固定參數、不調參」是對的。");

// (8) 策略績效總表(1d、realistic 成本、full-period;跨幣中位)——年化報酬等白話指標
var liveStrats = new HashSet<string> { "decorr4_ls", "mfi", "dual_mom_ls", "donchian_fade_ls", "ts_momentum", "ma_regime_trend", "rsi_stoch" };
decimal Annualize(decimal totalRetPct, DateTime start, DateTime end)
{
    var days = (end - start).TotalDays;
    if (days < 30) return 0m;
    var basev = 1.0 + (double)totalRetPct / 100.0;
    if (basev <= 0) return -100m;
    return (decimal)((Math.Pow(basev, 365.0 / days) - 1.0) * 100.0);
}
var perfRows = new List<(string name, decimal ann, decimal sh, decimal dd, decimal wr, decimal pf, decimal tpy, bool live)>();
foreach (var (name, s) in strats)
{
    var anns = new List<decimal>(); var shs = new List<decimal>(); var dds = new List<decimal>();
    var wrs = new List<decimal>(); var pfs = new List<decimal>(); var tpys = new List<decimal>();
    foreach (var kv in data)
        try {
            var bt = BacktestEngine.Run(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, commission: 0.0005m, slippagePct: 0.0003m);
            if (bt.TotalBars < 100) continue;
            anns.Add(Annualize(bt.TotalReturnPct, bt.StartDate, bt.EndDate));
            shs.Add(bt.SharpeRatio); dds.Add(bt.MaxDrawdownPct); wrs.Add(bt.WinRate); pfs.Add(bt.ProfitFactor);
            var yrs = (bt.EndDate - bt.StartDate).TotalDays / 365.0;
            tpys.Add(yrs > 0 ? (decimal)(bt.TotalTrades / yrs) : 0m);
        } catch { }
    if (anns.Count == 0) continue;
    perfRows.Add((name, Median(anns), Math.Round(shs.Average(), 2), Math.Round(dds.Average(), 0),
        Math.Round(wrs.Average(), 0), Math.Round(pfs.Average(), 2), Math.Round(tpys.Average(), 0), liveStrats.Contains(name)));
}
Console.WriteLine("\n=== 策略績效總表(1d、realistic 成本、full-period;跨幣中位;★=現行 live)===");
Console.WriteLine($"  {"strategy",-16}{"年化%",8}{"Sharpe",8}{"maxDD%",8}{"勝率%",7}{"PF",7}{"交易/年",9}");
foreach (var r in perfRows.OrderByDescending(x => x.ann))
    Console.WriteLine($"  {(r.live ? "★" : " ")}{r.name,-15}{r.ann,8:F0}{r.sh,8:F2}{r.dd,8:F0}{r.wr,7:F0}{r.pf,7:F2}{r.tpy,9:F0}");
Console.WriteLine("  註:無槓桿 / long-only / 跨幣中位 / 含 realistic 成本。實際 5x ≈ 年化×~5(maxDD 也×5、且有強平風險)。");

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

// ════════════════════════════════════════════════════════════════════════════
// --allocate 配置引擎:把候選策略池跑成「可直接部署的權重」
//   每腿 = (策略 → 它的最佳幣);跨腿做穩健配重。全用 long-short 引擎 + realistic 成本。
// ════════════════════════════════════════════════════════════════════════════
async Task RunAllocate()
{
    decimal EnvD(string k, decimal def) => decimal.TryParse(Environment.GetEnvironmentVariable(k), out var v) ? v : def;
    bool EnvB(string k, bool def) => bool.TryParse(Environment.GetEnvironmentVariable(k), out var v) ? v : def;
    string EnvS(string k, string def) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } s ? s : def;
    decimal targetVol = EnvD("ALLOC_TARGET_VOL_ANNUAL", 0.40m);  // 目標組合年化波動(40%、crypto 多策略合理)
    decimal maxExp    = EnvD("ALLOC_MAX_EXPOSURE", 3.0m);         // 整體曝險上限(對齊真錢 3x)
    double  capW      = (double)EnvD("ALLOC_MAX_WEIGHT", 0.35m);  // 單腿權重上限
    double  shrinkT   = (double)EnvD("ALLOC_SHRINK_TRADES", 100m);// 收縮基準 T*(OOS fold 數達此才完全信最佳化)
    // BTC 核心腿:固定幣種 BTC、用集成(多策略共識 + confidence-sizing 依共識大小下注)、比例拉高、獨立碳出。
    bool    btcCore   = EnvB("ALLOC_BTC_CORE", true);
    double  btcCoreW  = (double)EnvD("ALLOC_BTC_CORE_WEIGHT", 0.40m); // BTC 核心固定佔書比重(拉高)
    string  btcStrat  = EnvS("ALLOC_BTC_CORE_STRATEGY", "decorr4_ls");// 集成策略(多策略共識下注大小)
    // forward 證據:抓 broker 的實盤 per-strategy P&L、當回測之外的第二證據。
    // 實盤(≥min 筆)實際賠 → 否決該腿(回測過但實盤垮 = 過擬合/regime 破)。沒設 URL → 純回測(向後相容)。
    string  fwdUrl    = EnvS("ALLOC_FORWARD_URL", "");
    string  fwdFile   = EnvS("ALLOC_FORWARD_FILE", "");   // 本地 JSON(cron 用 docker exec curl 產出、繞過 loopback 守衛)
    int     fwdMin    = (int)EnvD("ALLOC_FORWARD_MIN_TRADES", 10m);

    Console.WriteLine("=== 配置引擎 --allocate ===");
    Console.WriteLine($"  參數:目標年化波動 {targetVol:P0} · 曝險上限 {maxExp:F1}x · 單腿上限 {capW:P0} · 收縮基準 T*={shrinkT:F0} folds");
    if (btcCore) Console.WriteLine($"  BTC 核心腿:開(固定 BTC、策略 {btcStrat} 多策略共識、佔書 {btcCoreW:P0}、獨立於衛星配置)");
    Console.WriteLine("  (env 可調:ALLOC_TARGET_VOL_ANNUAL / ALLOC_MAX_EXPOSURE / ALLOC_MAX_WEIGHT / ALLOC_SHRINK_TRADES / ALLOC_BTC_CORE[_WEIGHT/_STRATEGY])\n");

    // 1. 抓 1d 資料
    var dat = new Dictionary<string, List<BarData>>();
    foreach (var sym in symbols) { try { var b = await Fetch(sym); if (b.Count >= 400) dat[sym] = b; } catch { } }
    Console.WriteLine($"資料就緒:{dat.Count}/{symbols.Length} 檔 1d\n");

    // forward 實盤證據(per-strategy:成交數 / 已實現 P&L / 勝率)
    var fwd = new Dictionary<string, (int n, decimal pnl, decimal wr)>(StringComparer.OrdinalIgnoreCase);
    if (!string.IsNullOrEmpty(fwdFile) || !string.IsNullOrEmpty(fwdUrl))
    {
        try
        {
            string fjson = !string.IsNullOrEmpty(fwdFile) && File.Exists(fwdFile)
                ? File.ReadAllText(fwdFile)
                : await http.GetStringAsync(fwdUrl);
            using var fdoc = JsonDocument.Parse(fjson);
            var root = fdoc.RootElement;
            var dataEl = root.TryGetProperty("data", out var de) ? de : root;   // 兼容 ApiResponse 包裝
            if (dataEl.TryGetProperty("strategies", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var s in arr.EnumerateArray())
                {
                    var nm = s.TryGetProperty("strategy", out var sn) ? sn.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(nm)) continue;
                    fwd[nm] = (
                        s.TryGetProperty("trades", out var tn) ? tn.GetInt32() : 0,
                        s.TryGetProperty("realized_pnl", out var pp) ? pp.GetDecimal() : 0m,
                        s.TryGetProperty("win_rate", out var ww) ? ww.GetDecimal() : 0m);
                }
            Console.WriteLine($"forward 實盤證據:{fwd.Count} 策略有成交;否決門檻 = 實盤 ≥{fwdMin} 筆且賠錢\n");
        }
        catch (Exception ex) { Console.WriteLine($"⚠ forward 抓取失敗、退回純回測:{ex.Message}\n"); }
    }
    if (dat.Count == 0) { Console.WriteLine("無資料、中止。"); return; }

    // 2. 每策略 → 選它的最佳幣(full-period Sharpe 最高)當部署腿;收集 sharpe/vol/curve/OOS folds
    var rngA = new Random(7);
    (decimal mean, decimal lo, decimal hi, double t) Boot(List<decimal> xs)
    {
        if (xs.Count < 5) return (xs.Count > 0 ? xs.Average() : 0, 0, 0, 0);
        double mean = (double)xs.Average();
        double sd = Math.Sqrt(xs.Select(x => ((double)x - mean) * ((double)x - mean)).Sum() / (xs.Count - 1));
        double se = sd / Math.Sqrt(xs.Count);
        double tStat = se > 0 ? mean / se : 0;
        var means = new double[2000];
        for (int b = 0; b < 2000; b++) { double sum = 0; for (int i = 0; i < xs.Count; i++) sum += (double)xs[rngA.Next(xs.Count)]; means[b] = sum / xs.Count; }
        Array.Sort(means);
        return ((decimal)mean, (decimal)means[50], (decimal)means[1950], tStat);
    }
    List<double> DailyRets(List<decimal> curve, int take)
    {
        var c = curve.Skip(Math.Max(0, curve.Count - take)).ToList();
        var r = new List<double>();
        for (int t = 1; t < c.Count; t++) r.Add(c[t - 1] > 0 ? (double)((c[t] - c[t - 1]) / c[t - 1]) : 0);
        return r;
    }
    double AnnVol(List<decimal> curve)
    {
        var r = DailyRets(curve, curve.Count);
        if (r.Count < 2) return 0;
        var a = r.Average();
        return Math.Sqrt(r.Select(x => (x - a) * (x - a)).Average()) * Math.Sqrt(252);
    }

    // 顯著性改用「跨幣 pooling」(每支策略池化所有幣的 OOS folds → fold 數 ×N倍、CI 才有統計力);
    // 但 pooling 會因幣高相關高估顯著 → 另加「廣度濾網」:≥60% 幣 OOS 為正,防單一暴漲幣灌假顯著。
    // 部署腿仍取「最佳幣」(Sharpe 最高);vol/相關/權重用該腿曲線。
    decimal breadthMin = EnvD("ALLOC_BREADTH_MIN", 0.60m);
    int minPoolFolds = (int)EnvD("ALLOC_MIN_POOL_FOLDS", 30m);
    var cands = new List<Cand>();
    foreach (var (name, s) in strats)
    {
        var perCoin = new Dictionary<string, (decimal sh, List<decimal> curve, decimal ret, decimal dd)>();
        var poolFolds = new List<decimal>();   // 跨幣池化的 OOS test-fold 報酬
        int coinsTested = 0, coinsPos = 0;     // 廣度:幾個幣 OOS 為正
        foreach (var kv in dat)
        {
            try
            {
                var bt = LongShortBacktestEngine.Run(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, commission: 0.0005m, slippagePct: 0.0003m);
                if (bt.TotalBars >= 100) perCoin[kv.Key] = (bt.SharpeRatio, bt.EquityCurve.Select(e => e.Value).ToList(), bt.TotalReturnPct, bt.MaxDrawdownPct);
            }
            catch { }
            try
            {
                var wf = LongShortBacktestEngine.RunWalkForward(s, kv.Value, new StrategyConfig { Symbol = kv.Key, Interval = "1d" }, 250, 90, 60, commission: 0.0005m, slippagePct: 0.0003m);
                if (wf.TotalFolds == 0) continue;
                coinsTested++;
                if (wf.AvgTestReturnPct > 0) coinsPos++;
                foreach (var f in wf.Folds.Where(f => f.Test != null)) poolFolds.Add(f.Test!.TotalReturnPct);
            }
            catch { }
        }
        if (perCoin.Count == 0 || coinsTested == 0) continue;
        var (m, lo, hi, tstat) = Boot(poolFolds);
        decimal breadth = (decimal)coinsPos / coinsTested;
        var best = perCoin.OrderByDescending(p => p.Value.sh).First();
        cands.Add(new Cand(name, perCoin, poolFolds.Count, lo, tstat, breadth, best.Value.sh, best.Value.ret));
    }

    // 3. 入場閘:① pooled CI 下界>0(顯著)② 廣度 ≥60% 幣正(防單幣灌水)③ 最佳幣 Sharpe>0 + 報酬正 ④ pool fold 夠
    bool Pass(Cand c) => c.CiLo > 0m && c.Breadth >= breadthMin && c.BestSharpe > 0m && c.BestRet > 0m && c.Folds >= minPoolFolds;
    var passed = cands.Where(Pass).OrderByDescending(c => c.T).ToList();
    var rejected = cands.Where(c => !Pass(c)).ToList();

    Console.WriteLine($"=== 入場閘:{cands.Count} 候選 → {passed.Count} 通過(跨幣顯著 + 廣度≥{breadthMin:P0} + full正)===");
    Console.WriteLine($"  (env 可調:ALLOC_BREADTH_MIN={breadthMin:F2} · ALLOC_MIN_POOL_FOLDS={minPoolFolds})");
    foreach (var c in rejected.OrderByDescending(c => c.BestSharpe))
    {
        var bc = c.PerCoin.OrderByDescending(p => p.Value.sh).First().Key;
        var why = c.Folds < minPoolFolds ? $"fold<{minPoolFolds}" : c.CiLo <= 0m ? "不顯著(CI含0)" : c.Breadth < breadthMin ? $"廣度低({c.Breadth:P0}幣正)" : c.BestSharpe <= 0m ? "Sharpe≤0" : c.BestRet <= 0m ? "full賠" : "?";
        Console.WriteLine($"   ✗ {c.Name,-16}@{Sh(bc),-5} Sharpe {c.BestSharpe,5:F2} 廣度 {c.Breadth,4:P0} CI下界 {c.CiLo,6:F1} fold {c.Folds,3} — 剔除:{why}");
    }
    // 3.5 forward 否決:回測過、但實盤 ≥fwdMin 筆且實際賠錢 → 剔除(過擬合/regime 破的鐵證)
    if (fwd.Count > 0)
    {
        var fwdVetoed = new List<(Cand c, int n, decimal pnl)>();
        passed = passed.Where(c =>
        {
            if (fwd.TryGetValue(c.Name, out var f) && f.n >= fwdMin && f.pnl < 0m) { fwdVetoed.Add((c, f.n, f.pnl)); return false; }
            return true;
        }).ToList();
        if (fwdVetoed.Count > 0)
        {
            Console.WriteLine($"=== forward 否決:{fwdVetoed.Count} 支回測過、但實盤賠錢被剔 ===");
            foreach (var (c, n, pnl) in fwdVetoed)
                Console.WriteLine($"   ⛔ {c.Name,-16} 實盤 {n} 筆、已實現 {pnl:F2} USDT — 回測過但實盤垮、不上真錢");
        }
    }
    if (passed.Count == 0) { Console.WriteLine("\n無腿通過入場閘 → 不建議配置任何真錢。先回 paper 累積樣本。"); return; }

    // 3a. BTC 核心腿:固定幣 BTC、用集成策略(多策略共識 + confidence-sizing 依共識大小下注)碳出,
    //     獨立於下面的衛星 Sharpe 最大化;它的策略 + BTC 幣都不進衛星指派。
    Leg? core = null;
    var btcBackers = cands.Where(c => c.PerCoin.TryGetValue("BTCUSDT", out var v) && v.sh > 0m)
                          .OrderByDescending(c => c.PerCoin["BTCUSDT"].sh).ToList();
    if (btcCore)
    {
        var cc = cands.FirstOrDefault(c => c.Name == btcStrat);
        if (cc != null && cc.PerCoin.TryGetValue("BTCUSDT", out var bv) && bv.sh > 0m)
            core = new Leg(cc.Name, "BTCUSDT", bv.sh, (decimal)AnnVol(bv.curve), bv.curve, cc.Folds, cc.CiLo, cc.T, bv.ret, bv.dd, cc.Breadth);
        else { Console.WriteLine($"   ⚠ BTC 核心策略 {btcStrat} 在 BTC 無正 edge → 本次停用核心腿"); btcCore = false; }
    }

    // 3b. 衛星指派:每腿不同幣(真錢一 symbol 一策略 + 真去相關);核心開時排除 BTC 幣 + 核心策略。
    //     t 高的先選它的最佳「可用」幣(需 Sharpe>0 且 full 正);撞到已佔的幣就退而選次佳。
    var taken = new HashSet<string>();
    if (btcCore) taken.Add("BTCUSDT");
    var pool = new List<Leg>();
    foreach (var c in passed)
    {
        if (btcCore && c.Name == btcStrat) continue;   // 核心策略已碳出到 BTC
        var pick = c.PerCoin.Where(p => !taken.Contains(p.Key) && p.Value.sh > 0m && p.Value.ret > 0m)
                            .OrderByDescending(p => p.Value.sh).Select(p => (k: p.Key, v: p.Value)).FirstOrDefault();
        if (pick.k == null) { Console.WriteLine($"   ⚠ {c.Name} 無剩餘可用幣可指派(都被佔/負)→ 跳過"); continue; }
        taken.Add(pick.k);
        pool.Add(new Leg(c.Name, pick.k, pick.v.sh, (decimal)AnnVol(pick.v.curve), pick.v.curve, c.Folds, c.CiLo, c.T, pick.v.ret, pick.v.dd, c.Breadth));
    }
    int coreIdx = -1;
    if (core != null) { pool.Insert(0, core); coreIdx = 0; }   // 核心腿放第一
    if (pool.Count == 0) { Console.WriteLine("\n指派後無可部署腿。"); return; }

    // 4a. 對齊腿權益曲線(共同尾長),算相關 + 日報酬(給 vol-target 共變異)
    int N = pool.Count;
    int L = pool.Min(l => l.Curve.Count);
    var rets = pool.Select(l => DailyRets(l.Curve, L)).ToList();
    int RL = rets.Min(r => r.Count);
    for (int i = 0; i < N; i++) rets[i] = rets[i].Skip(rets[i].Count - RL).ToList();
    double[,] rho = new double[N, N];
    for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
            rho[i, j] = (double)CorrelationGuard.PearsonOfReturns(
                pool[i].Curve.Skip(pool[i].Curve.Count - L).ToList(),
                pool[j].Curve.Skip(pool[j].Curve.Count - L).ToList());

    // 4b. raw = max(0,Sharpe)/Vol → 相關 haircut(被一堆腿相關 = 降權)→ tilt 權重
    var raw = new double[N];
    for (int i = 0; i < N; i++) raw[i] = pool[i].Vol > 0m ? Math.Max(0, (double)pool[i].Sharpe) / (double)pool[i].Vol : 0;
    var haircut = new double[N];
    for (int i = 0; i < N; i++)
    {
        double corrSum = 0;
        for (int j = 0; j < N; j++) if (j != i) corrSum += Math.Max(0, rho[i, j]);
        haircut[i] = 1.0 / (1.0 + corrSum);
    }
    var tilt = new double[N];
    for (int i = 0; i < N; i++) tilt[i] = raw[i] * haircut[i];
    double tsum = tilt.Sum();
    var w = new double[N];
    for (int i = 0; i < N; i++) w[i] = tsum > 0 ? tilt[i] / tsum : 1.0 / N;

    // 4c. 朝等權收縮(每腿 λ=min(1,T/T*),資料少就靠等權)→ 重新正規化
    for (int i = 0; i < N; i++) { double lam = Math.Min(1.0, pool[i].Folds / shrinkT); w[i] = lam * w[i] + (1 - lam) * (1.0 / N); }
    double ws = w.Sum(); for (int i = 0; i < N; i++) w[i] /= ws;

    // 4d. 單腿上限 + 多餘量按比例分給未封頂者(迭代);最後正規化
    for (int iter = 0; iter < 12; iter++)
    {
        double over = 0; var free = new List<int>();
        for (int i = 0; i < N; i++) { if (w[i] > capW + 1e-9) { over += w[i] - capW; w[i] = capW; } else free.Add(i); }
        if (over < 1e-9) break;
        double freeSum = free.Sum(i => w[i]);
        if (freeSum < 1e-9) break;
        foreach (var i in free) w[i] += over * (w[i] / freeSum);
    }
    double ws2 = w.Sum(); for (int i = 0; i < N; i++) w[i] /= ws2;

    // 4d-2. BTC 核心腿:把它的權重固定/拉高到 btcCoreW,其餘按比例縮放(獨立於 Sharpe 最大化、可超單腿上限)。
    if (coreIdx >= 0 && N > 1)
    {
        double cw = Math.Min(0.9, btcCoreW);
        double othersOld = 1.0 - w[coreIdx];
        double othersNew = 1.0 - cw;
        if (othersOld > 1e-9) for (int i = 0; i < N; i++) if (i != coreIdx) w[i] *= othersNew / othersOld;
        w[coreIdx] = cw;
    }

    // 4e. vol-target:組合年化波動 = sqrt(w'Σw)·sqrt(252);曝險 = min(maxExp, targetVol/組合波動)
    double portDailyVar = 0;
    for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
        {
            var ai = rets[i].Average(); var aj = rets[j].Average();
            double cov = 0; for (int t = 0; t < RL; t++) cov += (rets[i][t] - ai) * (rets[j][t] - aj);
            cov /= Math.Max(1, RL - 1);
            portDailyVar += w[i] * w[j] * cov;
        }
    double portAnnVol = Math.Sqrt(Math.Max(0, portDailyVar)) * Math.Sqrt(252);
    double exposure = portAnnVol > 1e-9 ? Math.Min((double)maxExp, (double)targetVol / portAnnVol) : (double)maxExp;

    // 4f. N_eff(有效獨立押注數)+ 平均相關
    double sumSq = w.Sum(x => x * x);
    double nEff = sumSq > 0 ? 1.0 / sumSq : 0;
    double offDiag = 0; int cnt = 0;
    for (int i = 0; i < N; i++) for (int j = i + 1; j < N; j++) { offDiag += rho[i, j]; cnt++; }
    double avgRho = cnt > 0 ? offDiag / cnt : 0;

    // ── 輸出 ──
    Console.WriteLine($"\n=== 建議配置({N} 腿;total exposure {exposure:F2}x;◆=BTC 核心腿)===");
    Console.WriteLine($"  {"strategy@coin",-24}{"Sharpe",8}{"annVol",8}{"廣度",7}{"folds",7}{"weight",8}{"budget_pct",12}");
    var budgets = new List<(string coin, string strat, decimal bp)>();
    for (int i = 0; i < N; i++)
    {
        decimal bp = Math.Round((decimal)(w[i] * exposure) * 100m, 0);
        budgets.Add((pool[i].Coin, pool[i].Name, bp));
        string tag = (i == coreIdx ? "◆ " : "  ") + pool[i].Name + "@" + Sh(pool[i].Coin);
        Console.WriteLine($"  {tag,-24}{pool[i].Sharpe,8:F2}{pool[i].Vol,7:P0}{pool[i].Breadth,7:P0}{pool[i].Folds,7}{w[i],8:P0}{bp,11:F0}%");
    }
    Console.WriteLine($"  {"合計",-24}{"",8}{"",8}{"",7}{"",7}{w.Sum(),8:P0}{w.Sum() * exposure * 100,11:F0}%");
    if (coreIdx >= 0)
        Console.WriteLine($"\n  ◆ BTC 核心:{pool[coreIdx].Name}(多策略集成、confidence-sizing 依共識大小下注)固定佔書 {btcCoreW:P0}。" +
            $"\n     撐腰共識 — {btcBackers.Count} 支策略在 BTC 上有正 edge:" +
            string.Join("、", btcBackers.Take(8).Select(c => $"{c.Name}({c.PerCoin["BTCUSDT"].sh:F2})")));
    Console.WriteLine($"\n  組合年化波動 {portAnnVol:P0} → 為打到目標 {targetVol:P0}、整體曝險 = {exposure:F2}x(上限 {maxExp:F1}x)");
    Console.WriteLine($"  有效獨立押注數 N_eff = {nEff:F1} / {N} 腿   平均兩兩相關 ρ̄ = {avgRho:F2}   " +
        (nEff < N * 0.6 ? "⚠ N_eff 遠低於腿數 = 假分散(腿太像)" : "✓ 分散有效"));

    // 為什麼沒選 BTC?把每條腿「選中幣 vs BTC」的回測 Sharpe/報酬攤出來。
    // 引擎選的是「策略主動交易 edge 最強的幣」(Sharpe),不是「最有價值的資產」(buy&hold)——
    // BTC 最有效率/最被研究透 → 主動策略 edge 通常最薄;alt 沒效率、波動大 → edge 反而高。
    Console.WriteLine("\n=== 衛星腿 選中幣 vs BTC 回測 Sharpe(引擎挑的是 edge 不是資產價值;BTC 已另由核心腿持有)===");
    for (int i = 0; i < N; i++)
    {
        if (i == coreIdx) continue;   // 核心腿本身就是 BTC、不比
        var l = pool[i];
        var c = cands.First(x => x.Name == l.Name);
        bool hasBtc = c.PerCoin.TryGetValue("BTCUSDT", out var bv);
        string btcCol = hasBtc ? $"BTC Sh {bv.sh,5:F2} (ret {bv.ret,5:F0}%)" : "BTC 無資料";
        string verdict = !hasBtc ? "" : bv.sh >= l.Sharpe ? " ⚠ BTC 其實更強?!" : bv.sh <= 0m ? " → BTC 上此策略賠錢/無 edge" : " → BTC edge 較弱";
        Console.WriteLine($"   {l.Name,-16} 選 {Sh(l.Coin),-5} Sh {l.Sharpe,5:F2}  ·  {btcCol}{verdict}");
    }
    Console.WriteLine("   註:量的是『策略在該幣上的主動 edge』。BTC edge 薄 ≠ BTC 不值得 → 故另設核心腿用多策略集成下注 BTC。");

    // 相關矩陣
    Console.WriteLine("\n=== 選中腿 相關矩陣(全期權益日報酬)===");
    Console.WriteLine("  " + new string(' ', 18) + string.Join("", pool.Select(l => $"{Sh(l.Coin),8}")));
    for (int i = 0; i < N; i++)
        Console.WriteLine($"  {pool[i].Name + "@" + Sh(pool[i].Coin),-18}" + string.Join("", Enumerable.Range(0, N).Select(j => $"{rho[i, j],8:F2}")));

    // 畢業判定(paper→真錢):通過入場閘 = 統計上夠格;再標出跟組內其他腿最大相關(>0.6 = 上真錢前要再想)
    Console.WriteLine("\n=== 畢業判定(paper → 真錢)===");
    Console.WriteLine("  通過入場閘 = 統計上夠格上真錢。max ρ = 跟其他選中腿的最高相關(>0.6 = 加它紅利低、考慮替換):");
    for (int i = 0; i < N; i++)
    {
        double maxR = 0; int who = -1;
        for (int j = 0; j < N; j++) if (j != i && rho[i, j] > maxR) { maxR = rho[i, j]; who = j; }
        string flag = maxR > 0.6 ? $"⚠ 與 {pool[who].Name}@{Sh(pool[who].Coin)} ρ={maxR:F2}" : $"✓ 獨立(max ρ {maxR:F2})";
        // forward 實盤確認狀態
        string live = "live —(無 forward 資料)";
        if (fwd.TryGetValue(pool[i].Name, out var f))
            live = f.n >= fwdMin ? (f.pnl > 0m ? $"live ✓ 確認({f.n}筆 +{f.pnl:F1} 勝率{f.wr:P0})" : $"live ⚠ {f.n}筆 {f.pnl:F1}")
                                 : $"live …{f.n}筆(<{fwdMin}、樣本不足、暫看回測)";
        Console.WriteLine($"   {pool[i].Name,-16}@{Sh(pool[i].Coin),-5} → {flag} · {live}");
    }

    // 可直接貼的 SQL(budget_pct 寫回 watchlist;預設印 bingx、實際看你部署在哪交易所)
    Console.WriteLine("\n=== 可貼 SQL(把 budget_pct 寫回真錢 watchlist;交易所/symbol 格式自行對應)===");
    foreach (var (coin, strat, bp) in budgets)
        Console.WriteLine($"   UPDATE auto_trade_watchlist SET budget_pct={bp} WHERE strategy='{strat}'; -- {strat}@{Sh(coin)}");
    Console.WriteLine("\n   ⚠ 真錢 budget 改完必須立刻 restart broker(否則跑動的 PersistWatch 會用記憶體舊值覆蓋回去)。");
}

// ════════════════════════════════════════════════════════════════════════════
// --xsmom 橫斷面動量:跨幣排序、long 強勢 topK / short 弱勢 topK。結構不同的去相關 edge。
// ════════════════════════════════════════════════════════════════════════════
async Task RunXsMom()
{
    decimal EnvX(string k, decimal def) => decimal.TryParse(Environment.GetEnvironmentVariable(k), out var v) ? v : def;
    int topK = (int)EnvX("XSMOM_TOPK", 3m);
    decimal costSide = EnvX("XSMOM_COST_PER_SIDE", 0.0008m);   // 8bps/邊(perp taker+滑點)

    Console.WriteLine("=== 橫斷面動量 --xsmom(跨幣排序;long topK 強 / short topK 弱、等權)===");
    Console.WriteLine($"  topK={topK} · 成本 {costSide:P2}/邊(env XSMOM_TOPK / XSMOM_COST_PER_SIDE)\n");

    var dat = new Dictionary<string, List<BarData>>();
    foreach (var sym in symbols) { try { var b = await Fetch(sym); if (b.Count >= 400) dat[sym] = b; } catch { } }
    var coins = dat.Keys.ToList();
    if (coins.Count < topK * 2 + 1) { Console.WriteLine("幣數不足。"); return; }
    int L = dat.Values.Min(b => b.Count);
    var cl = coins.ToDictionary(c => c, c => dat[c].Skip(dat[c].Count - L).Select(b => b.Close).ToList());
    Console.WriteLine($"宇宙:{coins.Count} 幣、對齊尾段 {L} 日(~{L / 365.0:F1} 年)\n");

    // 核心回測 → (權益曲線, 平均換手率)。ls=true 多空、false 純多 topK。
    (List<decimal> eq, decimal turnover) Run(int lookback, int rebal, bool ls, decimal cost)
    {
        var eq = new List<decimal> { 1m };
        List<string> curL = new(), curS = new();
        int rebals = 0; decimal turnSum = 0m;
        for (int t = lookback; t < L - 1; t++)
        {
            if ((t - lookback) % rebal == 0)
            {
                var rank = coins.Select(c => (c, r: cl[c][t - lookback] > 0 ? (cl[c][t] - cl[c][t - lookback]) / cl[c][t - lookback] : 0m))
                                .OrderByDescending(x => x.r).ToList();
                var nL = rank.Take(topK).Select(x => x.c).ToList();
                var nS = ls ? rank.AsEnumerable().Reverse().Take(topK).Select(x => x.c).ToList() : new List<string>();
                int changed = nL.Except(curL).Count() + curL.Except(nL).Count() + nS.Except(curS).Count() + curS.Except(nS).Count();
                int basket = topK * (ls ? 2 : 1);
                decimal tov = basket > 0 ? (decimal)changed / (2 * basket) : 0m;
                turnSum += tov; rebals++;
                eq[^1] *= (1m - tov * 2m * cost);   // 換手比例 × 來回成本
                curL = nL; curS = nS;
            }
            decimal Ret(List<string> set) => set.Count == 0 ? 0m : set.Average(c => cl[c][t] > 0 ? (cl[c][t + 1] - cl[c][t]) / cl[c][t] : 0m);
            decimal pr = Ret(curL) - (ls ? Ret(curS) : 0m);
            eq.Add(eq[^1] * (1m + pr));
        }
        return (eq, rebals > 0 ? turnSum / rebals : 0m);
    }

    // 敏感度:lookback × rebal(防單一參數僥倖;穩健 = 多格都正)
    int[] lbs = { 30, 60, 90, 120 };
    int[] rbs = { 7, 14, 30 };
    foreach (var ls in new[] { true, false })
    {
        Console.WriteLine($"=== {(ls ? "多空(market-neutral)" : "純多 topK")} 敏感度(realistic 成本;格=Sharpe / 年化%)===");
        Console.WriteLine("  lookback\\rebal " + string.Join("", rbs.Select(r => $"{r + "d",14}")));
        foreach (var lb in lbs)
        {
            var cells = rbs.Select(rb =>
            {
                var (eq, _) = Run(lb, rb, ls, costSide);
                var st = StatsOf(eq);
                var ann = eq.Count > 30 ? (decimal)((Math.Pow((double)eq[^1], 365.0 / eq.Count) - 1) * 100) : 0m;
                return $"{st.sharpe,6:F2}/{ann,5:F0}%";
            });
            Console.WriteLine($"  {lb + "d",-14}" + string.Join("  ", cells));
        }
        Console.WriteLine();
    }

    // 代表配置細看(env XSMOM_LOOKBACK/REBAL 可指定要驗哪格)+ OOS(後半段)+ vs 買入持有
    int repLb = (int)EnvX("XSMOM_LOOKBACK", 30m);
    int repRb = (int)EnvX("XSMOM_REBAL", 7m);
    var (eqF, tov2) = Run(repLb, repRb, true, costSide);
    var (eqG, _) = Run(repLb, repRb, true, 0m);
    var full = StatsOf(eqF);
    int half = eqF.Count / 2;
    var oos = StatsOf(eqF.Skip(half).ToList());
    Console.WriteLine($"=== 代表配置 多空 lookback{repLb}/rebal{repRb} ===");
    Console.WriteLine($"  全期(realistic): ret {full.ret,6:F0}%  Sharpe {full.sharpe:F2}  maxDD {full.dd:F0}%  換手 {tov2:P0}/rebal");
    Console.WriteLine($"  全期(gross 0成本): Sharpe {StatsOf(eqG).sharpe:F2}  → 成本侵蝕 {StatsOf(eqG).sharpe - full.sharpe:F2}");
    Console.WriteLine($"  後半段 OOS:      ret {oos.ret,6:F0}%  Sharpe {oos.sharpe:F2}  maxDD {oos.dd:F0}%");
    // 買入持有等權基準
    var bhEq = new List<decimal> { 1m };
    for (int t = 0; t < L - 1; t++) { decimal r = coins.Average(c => cl[c][t] > 0 ? (cl[c][t + 1] - cl[c][t]) / cl[c][t] : 0m); bhEq.Add(bhEq[^1] * (1m + r)); }
    var bh = StatsOf(bhEq);
    Console.WriteLine($"  vs 等權買入持有:  ret {bh.ret,6:F0}%  Sharpe {bh.sharpe:F2}  maxDD {bh.dd:F0}%");

    // ★ 重點:跟現有書(decorr4_ls@BTC)相不相關 = 是不是真分散
    try
    {
        var ens = strats.First(x => x.name == "decorr4_ls").s;
        var dc = LongShortBacktestEngine.Run(ens, dat["BTCUSDT"], new StrategyConfig { Symbol = "BTCUSDT", Interval = "1d" }, commission: 0.0005m, slippagePct: 0.0003m)
                 .EquityCurve.Select(e => e.Value).ToList();
        int n = Math.Min(eqF.Count, dc.Count);
        var rho = CorrelationGuard.PearsonOfReturns(eqF.Skip(eqF.Count - n).ToList(), dc.Skip(dc.Count - n).ToList());
        Console.WriteLine($"\n=== 與現有書 decorr4_ls@BTC 的相關 ρ = {rho:F2} ===");
        Console.WriteLine($"  → {(Math.Abs((double)rho) < 0.3 ? "✅ 低相關、真分散紅利:值得當新一類 sleeve" : "⚠ 相關偏高、分散紅利有限")}");
    }
    catch (Exception ex) { Console.WriteLine($"相關計算失敗:{ex.Message}"); }

    Console.WriteLine($"\n判定(數據驅動):OOS Sharpe {oos.sharpe:F2} → " +
        (oos.sharpe > 0.3m ? "✅ OOS 站得住、可進 paper" : oos.sharpe > 0m ? "⚠ OOS 微正但弱、再觀察" : "❌ OOS 翻負、edge 不穩、別上") +
        $" · 與 decorr4 ρ 低=分散有但 edge 才是門檻。");
    Console.WriteLine("  (env XSMOM_LOOKBACK / XSMOM_REBAL 換格驗 OOS;敏感度表跨越正負 = 參數脆、過擬合風險高。");
}

// ════════════════════════════════════════════════════════════════════════════
// --carry 資金費 carry:現貨多 + 永續空收 funding。近零方向風險、跟價格 edge 正交的「穩定底盤」。
// 量化各幣 funding 年化/穩定度 + 籃子淨 carry + 小本金可行性。
// ════════════════════════════════════════════════════════════════════════════
async Task RunCarry()
{
    int topN = (int)(decimal.TryParse(Environment.GetEnvironmentVariable("CARRY_TOPN"), out var tn) ? tn : 5m);
    Console.WriteLine("=== 資金費 carry --carry(現貨多+永續空收 funding;年化=rate×3×365)===\n");

    // 抓 Binance USDT-M funding 歷史(8h 一次、1000 筆≈333 天)
    async Task<List<decimal>> FetchFunding(string sym)
    {
        try
        {
            var json = await http.GetStringAsync($"https://fapi.binance.com/fapi/v1/fundingRate?symbol={sym}&limit=1000");
            using var doc = JsonDocument.Parse(json);
            var outl = new List<decimal>();
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("fundingRate", out var fr) && decimal.TryParse(fr.GetString(), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    outl.Add(v);
            return outl;
        }
        catch { return new List<decimal>(); }
    }

    var rows = new List<(string coin, decimal ann, decimal recentAnn, decimal pctPos, decimal annStd, int n)>();
    foreach (var sym in symbols)
    {
        var f = await FetchFunding(sym);
        if (f.Count < 100) continue;
        decimal avg = f.Average();
        decimal ann = avg * 3m * 365m * 100m;
        var recent = f.Skip(Math.Max(0, f.Count - 90)).ToList();   // 近 ~30 天
        decimal recentAnn = recent.Average() * 3m * 365m * 100m;
        decimal pctPos = (decimal)f.Count(x => x > 0m) / f.Count * 100m;
        var a = f.Average();
        decimal std = (decimal)Math.Sqrt((double)f.Select(x => (x - a) * (x - a)).Average()) * 3m * 365m * 100m;
        rows.Add((Sh(sym), Math.Round(ann, 1), Math.Round(recentAnn, 1), Math.Round(pctPos, 0), Math.Round(std, 0), f.Count));
    }
    if (rows.Count == 0) { Console.WriteLine("無 funding 資料。"); return; }

    Console.WriteLine($"  {"coin",-7}{"年化%",8}{"近30d年化%",12}{"%正",7}{"年化波動%",11}{"n",6}");
    foreach (var r in rows.OrderByDescending(r => r.ann))
        Console.WriteLine($"  {r.coin,-7}{r.ann,8:F1}{r.recentAnn,12:F1}{r.pctPos,6:F0}%{r.annStd,11:F0}{r.n,6}");

    // 籃子:取年化 funding 最高的 topN(現貨多+永續空、等權)→ 平均 carry
    var top = rows.OrderByDescending(r => r.ann).Take(topN).ToList();
    decimal basketAnn = top.Average(r => r.ann);
    decimal basketRecent = top.Average(r => r.recentAnn);
    // 成本拖累估:每幣一對(現貨多+永續空)、開+平 4 條腿 × ~0.05% taker ≈ 0.2%/輪;月 rebal ≈ 2.4%/年 + 點差滑點 ~1% → 抓 ~3%/年
    decimal costDrag = 3.0m;
    decimal netAnn = basketAnn - costDrag;
    Console.WriteLine($"\n=== 籃子(年化 funding 最高 {topN} 幣、現貨多+永續空、等權)===");
    Console.WriteLine($"  成員:{string.Join(", ", top.Select(r => $"{r.coin}({r.ann:F0}%)"))}");
    Console.WriteLine($"  毛 carry 年化 ~{basketAnn:F1}%(近30d ~{basketRecent:F1}%)− 成本拖累 ~{costDrag:F0}% = **淨 ~{netAnn:F1}%/年**");
    Console.WriteLine($"  風險:近零方向(delta-neutral)、跟價格動量/TA 正交 → 真分散底盤;尾部=交易所/穩定幣脫鉤、強平、funding 反轉。");

    // 小本金可行性
    Console.WriteLine($"\n=== 小本金可行性 ===");
    Console.WriteLine($"  {topN} 幣 carry = {topN * 2} 個小倉(每幣 現貨+永續各一)。BingX 最小名目 ~5 USDT/倉。");
    Console.WriteLine($"  $350 本金 → 每倉 ~{350.0 / (topN * 2):F0} USDT,逼近最小單、手續費吃掉大半 carry → **不划算**。");
    Console.WriteLine($"  建議:本金 ≥ ${topN * 2 * 75} 左右(每倉 ~75 USDT、fee 佔比夠小)再上;當『穩定底盤』、跟方向性 edge 並存。");
    Console.WriteLine($"  ⚠ 毛 carry {basketAnn:F0}% 看似高多半是少數高波動 alt 灌的(年化波動欄越大越不穩);BTC/ETH funding 通常才年化個位數。");
}

// ════════════════════════════════════════════════════════════════════════════
// --xsrev 短週期橫斷面反轉:long 近期跌最兇 / short 漲最兇。跟動量負/低相關 = 配對紅利。
// ⚠ 高換手(日頻 rebal)→ 成本是生死關。
// ════════════════════════════════════════════════════════════════════════════
async Task RunXsRev()
{
    decimal EnvR(string k, decimal def) => decimal.TryParse(Environment.GetEnvironmentVariable(k), out var v) ? v : def;
    int topK = (int)EnvR("XSREV_TOPK", 3m);
    decimal costSide = EnvR("XSREV_COST_PER_SIDE", 0.0008m);
    Console.WriteLine("=== 短週期橫斷面反轉 --xsrev(long 跌最兇 / short 漲最兇、等權市場中性)===");
    Console.WriteLine($"  topK={topK} · 成本 {costSide:P2}/邊\n");

    var dat = new Dictionary<string, List<BarData>>();
    foreach (var sym in symbols) { try { var b = await Fetch(sym); if (b.Count >= 400) dat[sym] = b; } catch { } }
    var coins = dat.Keys.ToList();
    int L = dat.Values.Min(b => b.Count);
    var cl = coins.ToDictionary(c => c, c => dat[c].Skip(dat[c].Count - L).Select(b => b.Close).ToList());
    Console.WriteLine($"宇宙:{coins.Count} 幣、對齊尾段 {L} 日\n");

    // reversal=true:long bottomK(跌最兇)/short topK(漲最兇);false=動量
    (List<decimal> eq, decimal tov) Run(int lookback, int rebal, bool reversal, decimal cost)
    {
        var eq = new List<decimal> { 1m }; List<string> curL = new(), curS = new(); int rb = 0; decimal ts = 0m;
        for (int t = lookback; t < L - 1; t++)
        {
            if ((t - lookback) % rebal == 0)
            {
                var rank = coins.Select(c => (c, r: cl[c][t - lookback] > 0 ? (cl[c][t] - cl[c][t - lookback]) / cl[c][t - lookback] : 0m)).OrderByDescending(x => x.r).ToList();
                var winners = rank.Take(topK).Select(x => x.c).ToList();
                var losers = rank.AsEnumerable().Reverse().Take(topK).Select(x => x.c).ToList();
                var nL = reversal ? losers : winners;
                var nS = reversal ? winners : losers;
                int changed = nL.Except(curL).Count() + curL.Except(nL).Count() + nS.Except(curS).Count() + curS.Except(nS).Count();
                decimal tov = (decimal)changed / (4 * topK); ts += tov; rb++;
                eq[^1] *= (1m - tov * 2m * cost); curL = nL; curS = nS;
            }
            decimal Ret(List<string> s) => s.Count == 0 ? 0m : s.Average(c => cl[c][t] > 0 ? (cl[c][t + 1] - cl[c][t]) / cl[c][t] : 0m);
            eq.Add(eq[^1] * (1m + Ret(curL) - Ret(curS)));
        }
        return (eq, rb > 0 ? ts / rb : 0m);
    }

    int[] lbs = { 1, 2, 3, 5 }; int[] rbs = { 1, 2, 3 };
    Console.WriteLine("=== 反轉敏感度(realistic 成本;格=Sharpe / 年化%)===");
    Console.WriteLine("  lookback\\rebal " + string.Join("", rbs.Select(r => $"{r + "d",14}")));
    foreach (var lb in lbs)
    {
        var cells = rbs.Select(rb => { var (eq, _) = Run(lb, rb, true, costSide); var st = StatsOf(eq); var ann = eq.Count > 30 ? (decimal)((Math.Pow((double)eq[^1], 365.0 / eq.Count) - 1) * 100) : 0m; return $"{st.sharpe,6:F2}/{ann,5:F0}%"; });
        Console.WriteLine($"  {lb + "d",-14}" + string.Join("  ", cells));
    }

    int repLb = (int)EnvR("XSREV_LOOKBACK", 3m), repRb = (int)EnvR("XSREV_REBAL", 2m);
    var (eqF, tov2) = Run(repLb, repRb, true, costSide);
    var (eqG, _) = Run(repLb, repRb, true, 0m);
    var full = StatsOf(eqF); var oos = StatsOf(eqF.Skip(eqF.Count / 2).ToList());
    Console.WriteLine($"\n=== 代表 反轉 lookback{repLb}/rebal{repRb} ===");
    Console.WriteLine($"  全期 realistic: Sharpe {full.sharpe:F2} ret {full.ret:F0}% maxDD {full.dd:F0}% 換手 {tov2:P0}/rebal");
    Console.WriteLine($"  全期 gross 0成本: Sharpe {StatsOf(eqG).sharpe:F2}  → ⚠ 成本侵蝕 {StatsOf(eqG).sharpe - full.sharpe:F2}(高換手致命傷)");
    Console.WriteLine($"  後半 OOS: Sharpe {oos.sharpe:F2} ret {oos.ret:F0}%");
    // 與動量的相關(配對紅利關鍵)
    var (eqMom, _) = Run(30, 7, false, costSide);
    int n = Math.Min(eqF.Count, eqMom.Count);
    var rhoMom = CorrelationGuard.PearsonOfReturns(eqF.Skip(eqF.Count - n).ToList(), eqMom.Skip(eqMom.Count - n).ToList());
    Console.WriteLine($"\n  與 xsmom 動量(30/7)相關 ρ = {rhoMom:F2} → {(rhoMom < 0m ? "✅ 負相關、絕佳配對(動量+反轉互補)" : rhoMom < 0.3m ? "○ 低相關、可配對" : "⚠ 同向、配對紅利低")}");
    Console.WriteLine($"  判定:OOS Sharpe {oos.sharpe:F2} + 成本後 {(full.sharpe > 0.3m ? "撐得住" : "被成本咬爛")} → {(oos.sharpe > 0.3m && full.sharpe > 0.3m ? "可進 paper" : "反轉被換手成本吃掉、典型結果、不上")}");
}

// ════════════════════════════════════════════════════════════════════════════
// --fundsig 資金費當跨幣訊號:contrarian — long funding 最低(空擁擠)/short 最高(多擁擠)。
// ════════════════════════════════════════════════════════════════════════════
async Task RunFundSig()
{
    int topK = (int)(decimal.TryParse(Environment.GetEnvironmentVariable("FUNDSIG_TOPK"), out var tk) ? tk : 3m);
    decimal costSide = 0.0008m;
    Console.WriteLine("=== 資金費跨幣訊號 --fundsig(contrarian:long 低 funding / short 高 funding、等權)===\n");

    async Task<List<decimal>> FetchFundDaily(string sym)
    {
        // Binance fundingRate 單次 limit 實測只回 ~200 筆 → 分頁往回(endTime 遞減)抓 ~2 年。
        try
        {
            var all = new List<(long t, decimal r)>();
            long? endTime = null;
            for (int page = 0; page < 12; page++)
            {
                var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={sym}&limit=1000" + (endTime.HasValue ? $"&endTime={endTime.Value}" : "");
                var json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var batch = new List<(long, decimal)>();
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.TryGetProperty("fundingTime", out var ft) && decimal.TryParse(e.GetProperty("fundingRate").GetString(), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        batch.Add((ft.GetInt64(), v));
                if (batch.Count == 0) break;
                all.AddRange(batch);
                endTime = batch.Min(x => x.Item1) - 1;   // 往更早抓
                if (batch.Count < 150) break;            // 沒更多了
                await Task.Delay(120);
            }
            var pts = all.OrderBy(x => x.t).Select(x => x.r).ToList();
            var daily = new List<decimal>();   // 3 個 8h ≈ 1 日
            for (int i = 0; i + 2 < pts.Count; i += 3) daily.Add(pts[i] + pts[i + 1] + pts[i + 2]);
            return daily;
        }
        catch { return new List<decimal>(); }
    }

    var fund = new Dictionary<string, List<decimal>>(); var px = new Dictionary<string, List<decimal>>();
    foreach (var sym in symbols)
    {
        var fd = await FetchFundDaily(sym); var b = await Fetch(sym);
        // 要求 ≥250 天 funding+price,避免某個短歷史幣把對齊窗口截到很短(→ 過擬合假象)
        if (fd.Count >= 250 && b.Count >= 250) { fund[sym] = fd; px[sym] = b.Select(x => x.Close).ToList(); }
        else Console.WriteLine($"  (跳過 {Sh(sym)}:funding {fd.Count}d / price {b.Count}d 不足 250)");
    }
    var coins = fund.Keys.ToList();
    if (coins.Count < topK * 2 + 1) { Console.WriteLine("資料不足。"); return; }
    int L = coins.Min(c => Math.Min(fund[c].Count, px[c].Count));
    var fd2 = coins.ToDictionary(c => c, c => fund[c].Skip(fund[c].Count - L).ToList());
    var px2 = coins.ToDictionary(c => c, c => px[c].Skip(px[c].Count - L).ToList());
    Console.WriteLine($"宇宙:{coins.Count} 幣、對齊尾段 {L} 日\n");

    (List<decimal> eq, decimal tov) Run(int sigLb, int rebal, decimal cost)
    {
        var eq = new List<decimal> { 1m }; List<string> curL = new(), curS = new(); int rb = 0; decimal ts = 0m;
        for (int t = sigLb; t < L - 1; t++)
        {
            if ((t - sigLb) % rebal == 0)
            {
                var rank = coins.Select(c => (c, s: fd2[c].Skip(t - sigLb).Take(sigLb).DefaultIfEmpty(0m).Average())).OrderBy(x => x.s).ToList();   // 升序:funding 最低在前
                var nL = rank.Take(topK).Select(x => x.c).ToList();          // long 低 funding
                var nS = rank.AsEnumerable().Reverse().Take(topK).Select(x => x.c).ToList();  // short 高 funding
                int changed = nL.Except(curL).Count() + curL.Except(nL).Count() + nS.Except(curS).Count() + curS.Except(nS).Count();
                decimal tov = (decimal)changed / (4 * topK); ts += tov; rb++;
                eq[^1] *= (1m - tov * 2m * cost); curL = nL; curS = nS;
            }
            decimal Ret(List<string> s) => s.Count == 0 ? 0m : s.Average(c => px2[c][t] > 0 ? (px2[c][t + 1] - px2[c][t]) / px2[c][t] : 0m);
            eq.Add(eq[^1] * (1m + Ret(curL) - Ret(curS)));
        }
        return (eq, rb > 0 ? ts / rb : 0m);
    }

    int[] lbs = { 3, 7, 14 }; int[] rbs = { 3, 7 };
    Console.WriteLine("=== 敏感度(realistic 成本;格=Sharpe / 年化%)===");
    Console.WriteLine("  sigLookback\\rebal " + string.Join("", rbs.Select(r => $"{r + "d",14}")));
    foreach (var lb in lbs)
    {
        var cells = rbs.Select(rb => { var (eq, _) = Run(lb, rb, costSide); var st = StatsOf(eq); var ann = eq.Count > 30 ? (decimal)((Math.Pow((double)eq[^1], 365.0 / eq.Count) - 1) * 100) : 0m; return $"{st.sharpe,6:F2}/{ann,5:F0}%"; });
        Console.WriteLine($"  {lb + "d",-17}" + string.Join("  ", cells));
    }
    var (eqF, tov2) = Run(7, 7, costSide);
    var full = StatsOf(eqF); var oos = StatsOf(eqF.Skip(eqF.Count / 2).ToList());
    Console.WriteLine($"\n=== 代表 funding-contrarian sigLb7/rebal7 ===");
    Console.WriteLine($"  全期: Sharpe {full.sharpe:F2} ret {full.ret:F0}% maxDD {full.dd:F0}% 換手 {tov2:P0}");
    Console.WriteLine($"  後半 OOS: Sharpe {oos.sharpe:F2} ret {oos.ret:F0}%");
    Console.WriteLine($"  判定:OOS Sharpe {oos.sharpe:F2} → {(oos.sharpe > 0.3m ? "✅ 有戲、可進 paper" : oos.sharpe > 0m ? "⚠ 微弱" : "❌ 無 edge")}(funding 含 carry 成分、本就帶 contrarian 味)");
}

// --allocate 用:一條部署腿(策略 → 指派到的幣)的統計。Folds=跨幣池化 fold 數、Breadth=幾成幣 OOS 正
record Leg(string Name, string Coin, decimal Sharpe, decimal Vol, System.Collections.Generic.List<decimal> Curve, int Folds, decimal CiLo, double T, decimal Ret, decimal Dd, decimal Breadth);

// --allocate 候選:一支策略跨所有幣的成績(PerCoin)+ 池化顯著性,供入場閘 + 唯一幣指派
record Cand(string Name, System.Collections.Generic.Dictionary<string, (decimal sh, System.Collections.Generic.List<decimal> curve, decimal ret, decimal dd)> PerCoin, int Folds, decimal CiLo, double T, decimal Breadth, decimal BestSharpe, decimal BestRet);
