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

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

string[] symbols = { "BTCUSDT", "ETHUSDT", "BNBUSDT", "DOGEUSDT", "LTCUSDT" };

(string name, IStrategy s)[] strats =
{
    // 部署中的單腿(decorr4_ls 是 composite、無 ParamSchema 故跳過)
    ("dual_mom_ls",      new DualMomentumLsStrategy()),
    ("ma_regime_trend",  new MaRegimeTrendStrategy()),
    ("mfi",              new MfiStrategy()),
    ("rsi_stoch",        new StochasticStrategy()),
    // 研究中的關鍵腿(decorr4 components + 對沖腿)
    ("fib_retrace_ls",   new FibRetraceLsStrategy()),
    ("dual_thrust",      new DualThrustStrategy()),
    ("bb_revert_ls",     new BollingerRevertLsStrategy()),
};

async Task<List<BarData>> Fetch(string sym)
{
    var url = $"https://api.binance.com/api/v3/klines?symbol={sym}&interval=1d&limit=1000";
    var json = await http.GetStringAsync(url);
    using var doc = JsonDocument.Parse(json);
    var bars = new List<BarData>();
    foreach (var k in doc.RootElement.EnumerateArray())
        bars.Add(new BarData
        {
            OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()).UtcDateTime,
            Open  = decimal.Parse(k[1].GetString()!, CultureInfo.InvariantCulture),
            High  = decimal.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
            Low   = decimal.Parse(k[3].GetString()!, CultureInfo.InvariantCulture),
            Close = decimal.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
            Volume = decimal.Parse(k[5].GetString()!, CultureInfo.InvariantCulture),
        });
    return bars;
}

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
        var r = GenericWalkForwardOptimizer.Optimize(strat, bars, cfg, trainBars: 250, testBars: 90, cash: 1000m);
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
