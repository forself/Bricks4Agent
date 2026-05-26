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
