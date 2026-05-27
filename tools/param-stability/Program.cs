// Walk-forward 參數穩定度檢驗(2026-05-26 研究日記)。
// 對 portfolio.json 部署中(或關注中)的策略,跑 GenericWalkForwardOptimizer,
// 看現行參數是不是真的最穩、有沒有過擬合(ParamStability + Verdict)。
//
// 設計:每 (strategy × symbol) 一個 walk-forward(train 250 / test 90),
// 比對 default param 與 grid-search 後最佳 param 在 OOS 的表現差距;
// 穩定度=最佳參數跨 window 一致度(高=robust、低=curve-fit 徵兆)。
using BrokerCore.Trading;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using System.Globalization;
using System.Text.Json;

string[] symbols = { "BTCUSDT", "ETHUSDT", "BNBUSDT", "DOGEUSDT", "LTCUSDT" };

// --validate-robust: 把已發現的 5 個 robust pair 拿到 LongShortBacktestEngine
// (部署用引擎)跑 walk-forward,看 opt params 在 LS 下是否仍勝預設。
if (args.Contains("--validate-robust"))
{
    await RunValidateRobust();
    return;
}

// --validate-candidates: 同 symbol 不同策略(default 參數)比較,評估換腿可能性
// (ETH/LTC 換腿候選驗證,portfolio review 後續)
if (args.Contains("--validate-candidates"))
{
    await RunValidateCandidates();
    return;
}

// --validate-ltc-fib-robust: 對本日最強發現 LTC × fib_retrace 做 robustness 補測
// 跨時框 + 不同 walk-forward 設定,確認不是 1d 假象
if (args.Contains("--validate-ltc-fib-robust"))
{
    await RunValidateLtcFibRobust();
    return;
}

// --validate-harm-prz-scan: H15 王牌候選的 per-symbol LS 驗證
// LTC/OP/NEAR/APT × harm_prz_scan10 vs 現部署策略對比
if (args.Contains("--validate-harm-prz-scan"))
{
    await RunValidateHarmPrzScan();
    return;
}

// --validate-widepz: H14+H15 疊加(harm_prz_scan10_widepz)的 per-symbol robustness
// OP/NEAR/ADA/DOT × widepz 跨多 walk-forward 配置,確認 28% 不是 sample luck
if (args.Contains("--validate-widepz"))
{
    await RunValidateWidepz();
    return;
}

// --validate-h17-confsizing: H17 路線圖 — harm_prz_scan10 confidence-based sizing A/B
// strategy 已輸出 Confidence 0.6-0.95;引擎 confidenceSizing=true → 名目 × Confidence。看 Sharpe ↑↓
if (args.Contains("--validate-h17-confsizing"))
{
    await RunValidateH17ConfSizing();
    return;
}

// --validate-h18-atr-trail: H18 路線圖 — ATR trailing SL × harm_prz_scan10/widepz
// 引擎 atrTrailMultiplier>0 每根 ratchet activeStopPrice 往有利方向。看趨勢段能不能挽救 widepz TP 太緊。
if (args.Contains("--validate-h18-atr-trail"))
{
    await RunValidateH18AtrTrail();
    return;
}

// --validate-h18-trend: H18 ATR trailing 對「無 TP 的趨勢策略」的影響(預期真正受益對象)
// 直接影響 ETH/BNB 換腿評估 — ma_regime_trend(保守候選)加 trail 後能否反超 fib_retrace_ls
if (args.Contains("--validate-h18-trend"))
{
    await RunValidateH18OnTrendStrats();
    return;
}

// --validate-h18-peak-trail: H18 live-align — peak-based trailing(跟 AutoTraderService 同邏輯)A/B
// 對 fib_retrace_ls × ETH 跑多種 peak trail 配置、對比 ATR trail / baseline,看 live 套用後是否一致受益
if (args.Contains("--validate-h18-peak-trail"))
{
    await RunValidateH18PeakTrail();
    return;
}

// --validate-h21-vol-div: H21 — Volume divergence A/B(harm_prz_scan10、baseline vs 加分模式 vs 硬閘模式)
if (args.Contains("--validate-h21-vol-div"))
{
    await RunValidateH21VolDiv();
    return;
}

// --validate-paramsweep: H22+ — harm_prz_scan10 / widepz 參數面 sweep
//   scanWindows × przWidening × 5 主場幣、找邊際遞減點
if (args.Contains("--validate-paramsweep"))
{
    await RunValidateParamSweep();
    return;
}

// --test-pagination: 驗證 H22 KlineCache 分頁能否正確抓到 2000 bars
if (args.Contains("--test-pagination"))
{
    Console.WriteLine("=== H22 Binance 分頁測試 ===");
    foreach (var sym in new[] { "BTCUSDT", "ETHUSDT", "OPUSDT" })
    {
        Console.WriteLine($"抓 {sym} 2000 bars...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d", limit: 2000);
        sw.Stop();
        Console.WriteLine($"  → {bars.Count} bars ({bars[0].OpenTime:yyyy-MM-dd} → {bars[^1].OpenTime:yyyy-MM-dd}), {sw.ElapsedMilliseconds}ms");
    }
    return;
}

// 第一輪(2026-05-26)5 支已測過;這輪只補測之前被 MaxGrid=400 擋掉的兩支。
// 跑全部用 --all 旗標(較長運行時間)。
bool runAll = args.Contains("--all");
(string name, IStrategy s)[] strats = runAll
    ? new (string, IStrategy)[]
    {
        ("dual_mom_ls",      new DualMomentumLsStrategy()),
        ("ma_regime_trend",  new MaRegimeTrendStrategy()),
        ("mfi",              new MfiStrategy()),
        ("rsi_stoch",        new StochasticStrategy()),
        ("fib_retrace_ls",   new FibRetraceLsStrategy()),
        ("dual_thrust",      new DualThrustStrategy()),
        ("bb_revert_ls",     new BollingerRevertLsStrategy()),
    }
    : new (string, IStrategy)[]
    {
        ("dual_thrust",      new DualThrustStrategy()),   // grid 5832
        ("bb_revert_ls",     new BollingerRevertLsStrategy()), // grid 1296
    };
const int MaxGrid = 6000;   // 提高上限(預設 400)允許 dual_thrust(5832)、bb_revert(1296)

async Task<List<BarData>> Fetch(string sym) => await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");

Console.WriteLine("=== Walk-Forward 參數穩定度檢驗 ===");
Console.WriteLine($"資料: {symbols.Length} 檔幣 daily klines (limit=1000)");
Console.WriteLine("方法: train 250 / test 90 / grid-search 找每 window 最佳參數 → OOS 評");
Console.WriteLine("讀: ParamStability 0-1(高=參數跨 window 穩、低=curve-fit 徵兆)、Verdict 白話判語\n");

var data = new Dictionary<string, List<BarData>>();
foreach (var sym in symbols)
{
    try
    {
        var bars = await Fetch(sym);
        data[sym] = bars;
        Console.WriteLine($"  {sym}: {bars.Count} bars ({bars[0].OpenTime:yyyy-MM-dd} → {bars[^1].OpenTime:yyyy-MM-dd})");
    }
    catch (Exception e) { Console.WriteLine($"  {sym}: FETCH 失敗 {e.Message}"); }
}
Console.WriteLine();

Console.WriteLine("─── 結果(每 strategy × symbol)───");
Console.WriteLine($"{"strategy",-18} {"symbol",-10} {"grid",4} {"wins",5} {"def OOS%",10} {"opt OOS%",10} {"穩定度",8}  判語 | 最常選參數");
Console.WriteLine(new string('─', 130));

foreach (var (name, strat) in strats)
{
    foreach (var sym in symbols)
    {
        if (!data.ContainsKey(sym)) continue;
        var bars = data[sym];
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        var r = GenericWalkForwardOptimizer.Optimize(strat, bars, cfg, trainBars: 250, testBars: 90, cash: 1000m, maxGrid: MaxGrid);
        if (r.Error != null)
        {
            Console.WriteLine($"{name,-18} {sym,-10}  ERROR: {r.Error}");
            continue;
        }
        var paramsStr = string.Join(", ", r.MostCommonBestParams.Select(kv => $"{kv.Key}={kv.Value}"));
        Console.WriteLine($"{name,-18} {sym,-10} {r.GridSize,4} {r.WindowCount,5} {r.DefOosReturnPct,10:F1} {r.OptOosReturnPct,10:F1} {r.ParamStability,8:F2}  {r.Verdict} | {paramsStr}");
    }
    Console.WriteLine();
}

Console.WriteLine("(穩定度 ≥ 0.7 通常算 robust;< 0.5 多半過擬合徵兆,但要結合 opt vs def OOS 一起看)");

async Task RunValidateWidepz()
{
    Console.WriteLine("=== H14+H15 widepz per-symbol robustness ===");
    Console.WriteLine("OP/NEAR/ADA/DOT × harm_prz_scan10_widepz 跨多 walk-forward 配置,");
    Console.WriteLine("確認 strat-validate 的 per-symbol edge 不是 sample luck。\n");

    var widepz = () => new HarmonicPrzLsStrategy(
        patternWhitelist: null, name: "harm_prz_scan10_widepz",
        scanWindows: 10, przWidening: 0.15m);

    string[] symbols = { "OPUSDT", "NEARUSDT", "ADAUSDT", "DOTUSDT", "INJUSDT", "BTCUSDT" };
    var configs = new[]
    {
        (train:200, test:60,  stride:40, label:"200/60/40"),
        (train:250, test:90,  stride:60, label:"250/90/60 (baseline)"),
        (train:300, test:90,  stride:60, label:"300/90/60"),
        (train:250, test:120, stride:60, label:"250/120/60"),
    };

    foreach (var sym in symbols)
    {
        Console.WriteLine($"── {sym} ──");
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"  {"config",-22} {"folds",6} {"OOSmed%",8} {"AvgRet%",8} {"Sharpe",7} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
        foreach (var (tr, te, st, label) in configs)
        {
            var r = LongShortBacktestEngine.RunWalkForward(widepz(), bars, cfg, trainBars: tr, testBars: te, stride: st);
            if (r.TotalFolds == 0) { Console.WriteLine($"  {label,-22} (no folds)"); continue; }
            Console.WriteLine($"  {label,-22} {r.TotalFolds,6} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,7:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
}

async Task RunValidateHarmPrzScan()
{
    Console.WriteLine("=== H15 王牌候選 harm_prz_scan10 per-symbol LS 驗證 ===");
    Console.WriteLine("LTC/OP/NEAR/APT/INJ:strat-validate 顯示這些幣上 harm_prz_scan10 有明顯 edge,");
    Console.WriteLine("這裡用 LongShortBacktestEngine 跑 walk-forward 確認 + 對比現部署或最強對照。\n");

    var groups = new List<(string sym, IStrategy[] strats, string note)>
    {
        ("LTCUSDT",  new IStrategy[]
        {
            new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10),
            new FibRetraceLsStrategy(),       // 對照:LTC 上 fib 是另一個強候選(Sharpe 1.40)
            new StochasticStrategy(),         // 對照:現部署 rsi_stoch
        }, "LTC:現 rsi_stoch、fib def 強候選"),
        ("OPUSDT",   new IStrategy[]
        {
            new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10),
            new FibRetraceLsStrategy(),
            new MfiStrategy(),
        }, "OP:strat-validate 顯示 1d 21% best"),
        ("NEARUSDT", new IStrategy[]
        {
            new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10),
            new FibRetraceLsStrategy(),
            new BollingerRevertLsStrategy(),
        }, "NEAR:4/5 時框 avg 9%"),
        ("APTUSDT",  new IStrategy[]
        {
            new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10),
            new FibRetraceLsStrategy(),
            new BollingerRevertLsStrategy(),
        }, "APT:3/5 時框 avg 6%"),
        ("INJUSDT",  new IStrategy[]
        {
            new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10),
            new FibRetraceLsStrategy(),
        }, "INJ:3/5 時框 avg 8%"),
    };

    foreach (var (sym, strats4, note) in groups)
    {
        Console.WriteLine($"── {note} ──");
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"  {"strategy",-22} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
        foreach (var s in strats4)
        {
            var r = LongShortBacktestEngine.RunWalkForward(s, bars, cfg, trainBars: 250, testBars: 90, stride: 60);
            Console.WriteLine($"  {s.Name,-22} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
}

async Task RunValidateH17ConfSizing()
{
    Console.WriteLine("=== H17 — harm_prz_scan10 confidence-based sizing A/B(LS 引擎)===");
    Console.WriteLine("Strategy 已輸出 Confidence(pattern fit + candle confirm + RSI div, 0.6-0.95)");
    Console.WriteLine("引擎 confidenceSizing=true → 名目 × Confidence(縮量 5-40%、低 conf trade 弱化)");
    Console.WriteLine("假設:低信心 trade edge 較弱 → 縮量降低 drag → Sharpe ↑;反證:winner 被砍 → Return ↓\n");

    var coins = new[] { "LTCUSDT", "OPUSDT", "NEARUSDT", "APTUSDT", "INJUSDT", "ADAUSDT", "DOTUSDT" };
    foreach (var sym in coins)
    {
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"── {sym} ──");
        Console.WriteLine($"  {"mode",-22} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
        foreach (var (label, useConfSize) in new[] { ("confSize=off (baseline)", false), ("confSize=on  (H17)", true) })
        {
            var strat = new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10);
            var r = LongShortBacktestEngine.RunWalkForward(strat, bars, cfg,
                trainBars: 250, testBars: 90, stride: 60,
                commission: 0.0005m, slippagePct: 0.0003m, confidenceSizing: useConfSize);
            Console.WriteLine($"  {label,-22} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
    Console.WriteLine("解讀:on vs off Sharpe 一致升 = H17 加分;一致降 = 縮量殺 edge;mixed = 看主場/分歧");
}

async Task RunValidateH18AtrTrail()
{
    Console.WriteLine("=== H18 — ATR trailing SL × harm_prz_scan10 / widepz(LS 引擎、3 multiplier 對比)===");
    Console.WriteLine("引擎每根 ratchet activeStopPrice 往有利方向(只縮不放)。");
    Console.WriteLine("假設:widepz 配置下 TP 太緊提早出場 → trailing 接管後讓趨勢跑、Sharpe ↑ / DD ↓");
    Console.WriteLine("multiplier:0=baseline(無 trail)、2.0=保守、3.0=寬鬆\n");

    var strats = new (string label, Func<HarmonicPrzLsStrategy> mk)[]
    {
        ("scan10",         () => new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10", scanWindows: 10)),
        ("scan10_widepz",  () => new HarmonicPrzLsStrategy(patternWhitelist: null, name: "harm_prz_scan10_widepz", scanWindows: 10, przWidening: 0.15m)),
    };
    var multipliers = new decimal[] { 0m, 2.0m, 3.0m };
    var coins = new[] { "LTCUSDT", "OPUSDT", "ADAUSDT", "INJUSDT" };

    foreach (var (label, mkStrat) in strats)
    {
        Console.WriteLine($"┌─── {label} ───");
        foreach (var sym in coins)
        {
            var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
            var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
            Console.WriteLine($"│  {sym}");
            Console.WriteLine($"│    {"trail",-10} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
            foreach (var m in multipliers)
            {
                var strat = mkStrat();
                var r = LongShortBacktestEngine.RunWalkForward(strat, bars, cfg,
                    trainBars: 250, testBars: 90, stride: 60,
                    commission: 0.0005m, slippagePct: 0.0003m,
                    atrTrailMultiplier: m, atrPeriod: 14);
                var ml = m == 0m ? "off" : $"{m:F1}x";
                Console.WriteLine($"│    {ml,-10} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
            }
            Console.WriteLine("│");
        }
        Console.WriteLine();
    }
    Console.WriteLine("解讀:trail on 對比 off 同 Sharpe / DD 變化。widepz 若 Sharpe ↑ DD ↓ = H18 有效救 TP 太緊。");
}

async Task RunValidateH18OnTrendStrats()
{
    Console.WriteLine("=== H18 — ATR trailing × 趨勢策略(無 TP、預期受益對象)===");
    Console.WriteLine("假設:不發 TargetPrice 的策略沒有 TP 提早出場、trail 真正接管 → Sharpe ↑ / DD ↓");
    Console.WriteLine("關鍵問題:ma_regime_trend × ETH 加 trail 後能否反超 fib_retrace_ls(Sharpe 0.97)?\n");

    // 焦點:目前實盤 + 換腿候選的 trend 策略 × 對應幣
    var cases = new (string strategyLabel, Func<IStrategy> mk, string sym, string note)[]
    {
        ("ma_regime_trend",  () => new MaRegimeTrendStrategy(),     "ETHUSDT", "ETH 換腿保守候選"),
        ("fib_retrace_ls",   () => new FibRetraceLsStrategy(),       "ETHUSDT", "ETH 換腿激進候選(TP-driven)"),
        ("ma_regime_trend",  () => new MaRegimeTrendStrategy(),     "BNBUSDT", "BNB 換腿候選 1/3"),
        ("dual_thrust",      () => new DualThrustStrategy(),         "BNBUSDT", "BNB 換腿候選 2/3"),
        ("bb_revert_ls",     () => new BollingerRevertLsStrategy(), "BNBUSDT", "BNB 換腿候選 3/3"),
        ("fib_retrace_ls",   () => new FibRetraceLsStrategy(),       "BNBUSDT", "BNB 換腿候選 4(TP-driven 對照)"),
        ("dual_thrust",      () => new DualThrustStrategy(),         "SOLUSDT", "現 SOL 腿"),
    };
    var multipliers = new decimal[] { 0m, 2.0m, 3.0m };

    foreach (var (label, mk, sym, note) in cases)
    {
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"── {label} × {sym}({note})──");
        Console.WriteLine($"  {"trail",-13} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
        // H18 補正:傳 defaultInitialSlPct=5(配合 AutoTrader 5% 預設)讓 trail 有 base 可 ratchet
        foreach (var m in multipliers)
        {
            var strat = mk();
            var r = LongShortBacktestEngine.RunWalkForward(strat, bars, cfg,
                trainBars: 250, testBars: 90, stride: 60,
                commission: 0.0005m, slippagePct: 0.0003m,
                atrTrailMultiplier: m, atrPeriod: 14,
                defaultInitialSlPct: m > 0m ? 5m : 0m);  // trail 開才 bootstrap 5% SL;trail 關保持原 signal-driven
            var ml = m == 0m ? "off" : $"{m:F1}x+5%SL";
            Console.WriteLine($"  {ml,-13} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
    Console.WriteLine("ETH 換腿評估:若 ma_regime_trend × ETH 加 trail Sharpe > 0.97 → 保守選項超激進 fib_retrace_ls,值得換");
}

async Task RunValidateH18PeakTrail()
{
    Console.WriteLine("=== H18 live-align — peak-based trailing × fib_retrace_ls/ma_regime × ETH/BNB ===");
    Console.WriteLine("跟 AutoTraderService 同邏輯:peak 達 trigger 後、SL = peak × (1 ∓ distance%);只 ratchet。");
    Console.WriteLine("目的:驗證 H18 ATR 結論在 live-aligned peak 機制下是否一致(換腿 finalize 用)\n");

    var cases = new (string strategyLabel, Func<IStrategy> mk, string sym, string note)[]
    {
        ("fib_retrace_ls",   () => new FibRetraceLsStrategy(),       "ETHUSDT", "ETH 換腿候選 / H18 ATR 王者"),
        ("ma_regime_trend",  () => new MaRegimeTrendStrategy(),     "ETHUSDT", "ETH 保守候選"),
        ("ma_regime_trend",  () => new MaRegimeTrendStrategy(),     "BNBUSDT", "BNB 現役"),
        ("bb_revert_ls",     () => new BollingerRevertLsStrategy(), "ETHUSDT", "Bollinger 補測 — ETH 上未驗"),
        ("bb_revert_ls",     () => new BollingerRevertLsStrategy(), "BNBUSDT", "BNB 換腿候選之一"),
        ("bb_revert_ls",     () => new BollingerRevertLsStrategy(), "ADAUSDT", "ADA 上 H18 widepz baseline 已強(Sharpe 1.95)"),
    };
    // 配置:baseline / ATR 對照 / 多 peak 配置(從 live 預設 + 變體)
    var trailConfigs = new (string label, decimal atrMult, decimal peakTrig, decimal peakDist)[]
    {
        ("baseline",          0m,   0m,   0m  ),
        ("ATR 2.0x+5%SL",     2.0m, 0m,   0m  ),  // ATR 對照(從 H18 winner)
        ("peak 3%/2%(live)", 0m,   3m,   2m  ),  // live 預設方向
        ("peak 5%/3%",        0m,   5m,   3m  ),  // 中等
        ("peak 5%/5%",        0m,   5m,   5m  ),  // 鬆
        ("peak 3%/5%",        0m,   3m,   5m  ),  // 早觸發 + 鬆 SL
    };

    foreach (var (label, mk, sym, note) in cases)
    {
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"── {label} × {sym}({note})──");
        Console.WriteLine($"  {"trail",-22} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"Return/DD",10} {"WinRate"}");
        foreach (var (tlabel, atrMult, peakTrig, peakDist) in trailConfigs)
        {
            var strat = mk();
            var r = LongShortBacktestEngine.RunWalkForward(strat, bars, cfg,
                trainBars: 250, testBars: 90, stride: 60,
                commission: 0.0005m, slippagePct: 0.0003m,
                atrTrailMultiplier: atrMult, atrPeriod: 14,
                defaultInitialSlPct: (atrMult > 0m || peakTrig > 0m) ? 5m : 0m,
                peakTrailTriggerPct: peakTrig, peakTrailDistancePct: peakDist);
            var rdd = r.WorstTestDdPct > 0m ? r.AvgTestReturnPct / r.WorstTestDdPct : 0m;
            Console.WriteLine($"  {tlabel,-22} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {rdd,10:F2} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
    Console.WriteLine("關鍵問題:peak 機制下 fib×ETH 是否仍 Return/DD 大幅勝 baseline?");
    Console.WriteLine("若是 → ETH 換腿(fib + live peak trail)結論 robust、可推進 shadow 階段");
}

async Task RunValidateH21VolDiv()
{
    Console.WriteLine("=== H21 — Volume divergence × harm_prz_scan10 三模 A/B ===");
    Console.WriteLine("D 點量 ≥ 1.5×(B→D-1 平均)= 確認反轉有量能撐(諧波理論:真實反轉伴隨機構參與)");
    Console.WriteLine("baseline / 加分(+0.10 conf 但不過濾) / 硬閘(無 vol div 不進場) 三模對比\n");

    var coins = new[] { "LTCUSDT", "OPUSDT", "APTUSDT", "INJUSDT", "ADAUSDT" };
    var modes = new (string label, Func<HarmonicPrzLsStrategy> mk)[]
    {
        ("baseline",       () => new HarmonicPrzLsStrategy(name: "harm_prz_scan10", scanWindows: 10)),
        ("vol_div +0.10",  () => new HarmonicPrzLsStrategy(name: "harm_prz_scan10", scanWindows: 10, volDivConfBonus: 0.10m)),
        ("vol_div HARD",   () => new HarmonicPrzLsStrategy(name: "harm_prz_scan10", scanWindows: 10, requireVolDivToEnter: true)),
    };
    foreach (var sym in coins)
    {
        var bars = await ToolsShared.KlineCache.FetchOrLoad(sym, "1d");
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"── {sym} ──");
        Console.WriteLine($"  {"mode",-16} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
        foreach (var (label, mk) in modes)
        {
            var strat = mk();
            var r = LongShortBacktestEngine.RunWalkForward(strat, bars, cfg,
                trainBars: 250, testBars: 90, stride: 60,
                commission: 0.0005m, slippagePct: 0.0003m);
            Console.WriteLine($"  {label,-16} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
    Console.WriteLine("解讀:加分模式 vs baseline 差異小 → vol div 不是 conf 區分器(同 H17 結論)");
    Console.WriteLine("    硬閘模式 vs baseline:Sharpe ↑ + +folds ↓ = 確實是 quality 過濾器、值得當 filter");
}

async Task RunValidateLtcFibRobust()
{
    Console.WriteLine("=== LTC × fib_retrace_ls Robustness 補測 ===");
    Console.WriteLine("本日最強發現:LTC × fib_retrace_ls(default) Sharpe 1.40。");
    Console.WriteLine("確認不是 1d 假象 + 不是特定 train/test 配置運氣。\n");

    Task<List<BarData>> FetchTf(string interval) => ToolsShared.KlineCache.FetchOrLoad("LTCUSDT", interval);

    Console.WriteLine("── 1. 跨時框測試(walk-forward train250/test90/stride60、default params)──");
    Console.WriteLine($"  {"interval",-8} {"bars",5} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
    var intervals = new[] { "1h", "4h", "12h", "1d", "1w" };
    foreach (var iv in intervals)
    {
        var bars = await FetchTf(iv);
        var cfg = new StrategyConfig { Symbol = "LTCUSDT", Exchange = "binance", Interval = iv };
        var r = LongShortBacktestEngine.RunWalkForward(new FibRetraceLsStrategy(), bars, cfg, trainBars: 250, testBars: 90, stride: 60);
        if (r.TotalFolds == 0)
        {
            Console.WriteLine($"  {iv,-8} {bars.Count,5}  (bar 不足做 walk-forward)");
            continue;
        }
        Console.WriteLine($"  {iv,-8} {bars.Count,5} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
    }

    Console.WriteLine("\n── 2. 1d 不同 walk-forward 配置(default params)──");
    Console.WriteLine($"  {"train/test/stride",-20} {"folds",6} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
    var bars1d = await FetchTf("1d");
    var cfg1d = new StrategyConfig { Symbol = "LTCUSDT", Exchange = "binance", Interval = "1d" };
    var configs = new[]
    {
        (train:200, test:60,  stride:40),
        (train:200, test:90,  stride:60),
        (train:250, test:90,  stride:60),   // baseline (前面已測)
        (train:250, test:120, stride:60),
        (train:300, test:90,  stride:60),
        (train:300, test:120, stride:90),
        (train:400, test:90,  stride:60),
    };
    foreach (var (tr, te, st) in configs)
    {
        var r = LongShortBacktestEngine.RunWalkForward(new FibRetraceLsStrategy(), bars1d, cfg1d, trainBars: tr, testBars: te, stride: st);
        string label = $"{tr}/{te}/{st}";
        if (r.TotalFolds == 0)
        {
            Console.WriteLine($"  {label,-20}  (bar 不足)");
            continue;
        }
        Console.WriteLine($"  {label,-20} {r.TotalFolds,6} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
    }

    Console.WriteLine("\n── 3. 對照:rsi_stoch(現部署)在同一組配置 ──");
    Console.WriteLine($"  {"train/test/stride",-20} {"folds",6} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
    foreach (var (tr, te, st) in configs)
    {
        var r = LongShortBacktestEngine.RunWalkForward(new StochasticStrategy(), bars1d, cfg1d, trainBars: tr, testBars: te, stride: st);
        string label = $"{tr}/{te}/{st}";
        if (r.TotalFolds == 0) continue;
        Console.WriteLine($"  {label,-20} {r.TotalFolds,6} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
    }
    Console.WriteLine("\n(目標:若 fib 在多時框/多配置下都顯著勝 rsi_stoch、就是 robust 換腿候選)");
}

async Task RunValidateCandidates()
{
    Console.WriteLine("=== 換腿候選 LS 驗證(同 symbol 不同策略、default 參數)===");
    Console.WriteLine("補完 portfolio review:ETH/LTC 換腿候選在 LS walk-forward 下是否真的勝部署中。\n");

    var groups = new List<(string sym, IStrategy[] strats, string note)>
    {
        ("ETHUSDT", new IStrategy[]
        {
            new MfiStrategy(),                  // 現部署
            new MaRegimeTrendStrategy(),        // 候選 1(長線 def OOS 45.9%)
            new FibRetraceLsStrategy(),         // 候選 2(長線 def OOS 45.6%)
        }, "ETH:現 mfi (def 4.4% 是 portfolio 最低)"),
        ("LTCUSDT", new IStrategy[]
        {
            new StochasticStrategy(),           // 現部署 rsi_stoch
            new FibRetraceLsStrategy(),         // 候選(長線 def OOS 120.4%,35 結果最高)
            new MfiStrategy(),                  // 第二候選(def 26.1%、use-default)
        }, "LTC:現 rsi_stoch (LS mixed、AvgRet -2.7%)"),
    };

    Task<List<BarData>> Fetch3(string s) => ToolsShared.KlineCache.FetchOrLoad(s, "1d");

    foreach (var (sym, strats3, note) in groups)
    {
        Console.WriteLine($"── {note} ──");
        var bars = await Fetch3(sym);
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        Console.WriteLine($"  {"strategy",-20} {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate"}");
        foreach (var s in strats3)
        {
            var r = LongShortBacktestEngine.RunWalkForward(s, bars, cfg, trainBars: 250, testBars: 90, stride: 60);
            string marker = strats3[0] == s ? "(現)" : "(候選)";
            Console.WriteLine($"  {s.Name + " " + marker,-20} {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}");
        }
        Console.WriteLine();
    }
}

async Task RunValidateRobust()
{
    Console.WriteLine("=== Robust Pair LS-Engine 驗證 ===");
    Console.WriteLine("把 5 個 robust pair 的最佳參數拿到 LongShortBacktestEngine(部署引擎)跑 walk-forward,");
    Console.WriteLine("看 opt 在 LS 下是否仍勝 def(walk-forward optimizer 用的是 long-only 引擎,LS 才是真環境)\n");

    var pairs = new (string sym, IStrategy strat, Dictionary<string, object> opt)[]
    {
        ("DOGEUSDT", new BollingerRevertLsStrategy(),
            new Dictionary<string, object> { ["bb_period"]=10, ["bb_trend_sma"]=60, ["bb_entry_z"]=1.25m }),
        ("LTCUSDT",  new StochasticStrategy(),
            new Dictionary<string, object> { ["rsi_period"]=7, ["stoch_k"]=7, ["stoch_d"]=3 }),
        ("BNBUSDT",  new BollingerRevertLsStrategy(),
            new Dictionary<string, object> { ["bb_period"]=15, ["bb_trend_sma"]=70, ["bb_entry_z"]=1m }),
        ("BNBUSDT",  new DualThrustStrategy(),
            new Dictionary<string, object> { ["dt_lookback"]=5, ["dt_trend_sma"]=80, ["dt_k1"]=0.8m, ["dt_k2"]=0.5m }),
        ("BNBUSDT",  new FibRetraceLsStrategy(),
            new Dictionary<string, object> { ["fib_lookback"]=90, ["fib_min_range_pct"]=5m }),
    };

    Task<List<BarData>> Fetch2(string s) => ToolsShared.KlineCache.FetchOrLoad(s, "1d");

    Console.WriteLine($"{"strategy",-18} {"sym",-9} {"變體",-6}  {"OOSmed%",8} {"AvgRet%",8} {"AvgSh",6} {"WorstDD%",9} {"+folds",7} {"WinRate",8}");
    Console.WriteLine(new string('─', 110));

    foreach (var (sym, strat, optParams) in pairs)
    {
        var bars = await Fetch2(sym);
        var cfgDef = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        var cfgOpt = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d", Params = optParams };

        var def = LongShortBacktestEngine.RunWalkForward(strat, bars, cfgDef, trainBars: 250, testBars: 90, stride: 60);
        var opt = LongShortBacktestEngine.RunWalkForward(strat, bars, cfgOpt, trainBars: 250, testBars: 90, stride: 60);

        string Row(string variant, BacktestEngine.WalkForwardResult r) =>
            $"{strat.Name,-18} {sym,-9} {variant,-6}  {r.MedianTestReturnPct,8:F1} {r.AvgTestReturnPct,8:F1} {r.AvgTestSharpe,6:F2} {r.WorstTestDdPct,9:F1} {$"{r.PositiveTestFolds}/{r.TotalFolds}",7} {r.AvgTestWinRate,8:F2}";

        Console.WriteLine(Row("def", def));
        Console.WriteLine(Row("opt", opt));
        Console.WriteLine();
    }
}

async Task RunValidateParamSweep()
{
    Console.WriteLine("=== H22+ harm_prz 參數面 sweep — scanWindows × przWidening × 5 主場幣 ===");
    Console.WriteLine("Baseline: scanWindows=10, przWidening=0.15(t-stat 5.54)");
    Console.WriteLine("找邊際遞減點:scan 縮成 5 是否更純?widepz 開到 20% 是否撈更多訊號?\n");

    string[] symbols = { "LTCUSDT", "OPUSDT", "ADAUSDT", "INJUSDT", "APTUSDT" };
    int[] scanWindowOpts = { 5, 10, 15, 20, 30 };
    decimal[] widepzOpts = { 0m, 0.10m, 0.15m, 0.20m };

    // 為每個幣 fetch 一次 bars(複用)
    var barsCache = new Dictionary<string, List<BarData>>();
    foreach (var s in symbols)
    {
        barsCache[s] = await ToolsShared.KlineCache.FetchOrLoad(s, "1d");
        Console.WriteLine($"  {s} bars loaded: {barsCache[s].Count}");
    }
    Console.WriteLine();

    // 跑每個 (scan, widepz, symbol) tuple
    // 結果 aggregate by (scan, widepz) 跨幣中位 Sharpe + AvgRet,找最強組合
    var results = new List<(int scan, decimal pz, string sym, decimal sharpe, decimal medRet, decimal ddPct, int folds, int posFolds)>();
    foreach (var scan in scanWindowOpts)
    foreach (var pz in widepzOpts)
    foreach (var sym in symbols)
    {
        var name = $"sc{scan}_pz{(int)(pz*100)}";
        var strat = new HarmonicPrzLsStrategy(patternWhitelist: null, name: name, scanWindows: scan, przWidening: pz);
        var cfg = new StrategyConfig { Symbol = sym, Exchange = "binance", Interval = "1d" };
        var r = LongShortBacktestEngine.RunWalkForward(strat, barsCache[sym], cfg, trainBars: 250, testBars: 90, stride: 60);
        if (r.TotalFolds == 0) continue;
        results.Add((scan, pz, sym, r.AvgTestSharpe, r.MedianTestReturnPct, r.WorstTestDdPct, r.TotalFolds, r.PositiveTestFolds));
    }

    // 表 1:跨 (scan × pz) 跨幣 avg Sharpe
    Console.WriteLine("=== 表 1:跨幣 avg Sharpe(粗體 = baseline scan=10 / pz=15)===");
    Console.WriteLine($"  {"scanWin",-8}{"pz=0",10}{"pz=10",10}{"pz=15★",10}{"pz=20",10}");
    foreach (var scan in scanWindowOpts)
    {
        Console.Write($"  {scan,-8}");
        foreach (var pz in widepzOpts)
        {
            var subset = results.Where(r => r.scan == scan && r.pz == pz).ToList();
            var avgSh = subset.Count > 0 ? subset.Average(r => r.sharpe) : 0m;
            var marker = (scan == 10 && pz == 0.15m) ? "★" : "";
            Console.Write($"{avgSh,9:F2}{marker,1}");
        }
        Console.WriteLine();
    }

    // 表 2:跨幣中位 Return%
    Console.WriteLine("\n=== 表 2:跨幣中位 OOS Return%(walk-forward mean of medians)===");
    Console.WriteLine($"  {"scanWin",-8}{"pz=0",10}{"pz=10",10}{"pz=15★",10}{"pz=20",10}");
    foreach (var scan in scanWindowOpts)
    {
        Console.Write($"  {scan,-8}");
        foreach (var pz in widepzOpts)
        {
            var subset = results.Where(r => r.scan == scan && r.pz == pz).ToList();
            var avgRet = subset.Count > 0 ? subset.Average(r => r.medRet) : 0m;
            var marker = (scan == 10 && pz == 0.15m) ? "★" : "";
            Console.Write($"{avgRet,9:F1}{marker,1}");
        }
        Console.WriteLine();
    }

    // 表 3:跨幣中位 Worst DD%(越低越好)
    Console.WriteLine("\n=== 表 3:跨幣中位 Worst DD%(越低越穩)===");
    Console.WriteLine($"  {"scanWin",-8}{"pz=0",10}{"pz=10",10}{"pz=15★",10}{"pz=20",10}");
    foreach (var scan in scanWindowOpts)
    {
        Console.Write($"  {scan,-8}");
        foreach (var pz in widepzOpts)
        {
            var subset = results.Where(r => r.scan == scan && r.pz == pz).ToList();
            var medDd = subset.Count > 0 ? subset.Average(r => r.ddPct) : 0m;
            var marker = (scan == 10 && pz == 0.15m) ? "★" : "";
            Console.Write($"{medDd,9:F1}{marker,1}");
        }
        Console.WriteLine();
    }

    // 找 top-5 (scan, pz) 組合按 cross-symbol Sharpe
    Console.WriteLine("\n=== Top 5 (scan × pz) 跨幣 avg Sharpe 排名 ===");
    var ranked = results
        .GroupBy(r => (r.scan, r.pz))
        .Select(g => new
        {
            scan = g.Key.scan,
            pz = g.Key.pz,
            avgSh = g.Average(r => r.sharpe),
            avgRet = g.Average(r => r.medRet),
            avgDd = g.Average(r => r.ddPct),
        })
        .OrderByDescending(g => g.avgSh)
        .Take(5);
    Console.WriteLine($"  {"rank",-5}{"scan",6}{"pz",6}{"avg Sharpe",12}{"avg Ret%",10}{"avg DD%",10}");
    int rank = 1;
    foreach (var g in ranked)
    {
        var marker = (g.scan == 10 && g.pz == 0.15m) ? " (baseline)" : "";
        Console.WriteLine($"  {rank,-5}{g.scan,6}{(int)(g.pz*100),6}{g.avgSh,12:F2}{g.avgRet,10:F1}{g.avgDd,10:F1}{marker}");
        rank++;
    }
}
