using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Regime 歸因分析(Regime Attribution)—— 量化「分行情用對策略」到底比「一條策略跑到底」好多少。
///
/// 回答用戶的命題:回測能測出「什麼行情適合什麼策略」,那在各自優勢區間發揮、取長補短,
/// 是否能取代單一策略到底?具體優化多少?
///
/// 做法(signal-level attribution、非完整回測——每根 bar 獨立、用下一根報酬當該訊號的損益、
/// 無手續費/複利/持倉,目的是「相對比較哪個策略在哪個 regime 有 edge」,不是絕對績效):
///   1. 逐根 bar 用 RegimeDetector 標 regime;
///   2. 每個策略在該 bar 的 signal(buy/sell/hold)× 下一根報酬 → 累積到 (策略,regime) 桶;
///   3. 三條對比線:
///      - BestSingle  = 全程最佳單策略(baseline,把每個 regime 的損益加總取最大那個)
///      - Oracle      = 每個 regime 事後挑最佳策略之和(in-sample 上限、有 look-ahead → 只是「潛力天花板」)
///      - 真實切換    = 把 regime_adaptive / character_ensemble 也放進候選,看它們的全程總和抓到多少 Oracle
///   4. ImprovementPctVsBestSingle = (Oracle − BestSingle) / |BestSingle| → 取長補短的潛力上限。
/// </summary>
public static class RegimeAttributionAnalyzer
{
    public class RegimeStat
    {
        public decimal ReturnSum { get; set; }   // 該 (策略,regime) 桶的累積報酬(小數,×100 = %)
        public int Bars { get; set; }
        public int Wins { get; set; }
        public int Trades { get; set; }          // 非 hold 的次數
        public decimal WinRate => Trades > 0 ? Math.Round((decimal)Wins / Trades, 4) : 0m;
    }

    public class Result
    {
        public int AnalyzedBars { get; set; }
        public Dictionary<string, int> RegimeBars { get; set; } = new();                        // regime → 出現幾根
        public Dictionary<string, Dictionary<string, decimal>> Matrix { get; set; } = new();    // 策略 → regime → 報酬%
        public Dictionary<string, decimal> StrategyTotalPct { get; set; } = new();              // 策略 → 全程總報酬%(含 regime_adaptive = 真實切換)
        public Dictionary<string, string> PerRegimeBest { get; set; } = new();                  // regime → 最佳策略
        public string BestSingleStrategy { get; set; } = "";
        public decimal BestSingleReturnPct { get; set; }
        public decimal OracleReturnPct { get; set; }                 // 每 regime 用最佳之和(in-sample 上限)
        public decimal ImprovementPctVsBestSingle { get; set; }      // (Oracle − BestSingle)/|BestSingle|×100
        public string? Error { get; set; }
    }

    public static Result Analyze(
        List<BarData> bars, IReadOnlyList<IStrategy> strategies, StrategyConfig config, int warmup = 50)
    {
        var r = new Result();
        if (bars == null || bars.Count < warmup + 10) { r.Error = $"not enough bars (need ≥ {warmup + 10})"; return r; }
        if (strategies == null || strategies.Count == 0) { r.Error = "no strategies"; return r; }

        var stats = new Dictionary<string, Dictionary<string, RegimeStat>>();
        foreach (var s in strategies) stats[s.Name] = new();
        var regimeBars = new Dictionary<string, int>();

        // 逐 bar:標 regime + 各策略 signal × 下一根報酬 → 歸桶(留最後一根算 next return)
        for (int i = warmup; i < bars.Count - 1; i++)
        {
            var window = bars.GetRange(0, i + 1);
            var regime = RegimeDetector.Detect(window).Type.ToString();
            regimeBars[regime] = regimeBars.GetValueOrDefault(regime) + 1;

            decimal baseClose = bars[i].Close;
            if (baseClose <= 0) continue;
            decimal ret = (bars[i + 1].Close - baseClose) / baseClose;

            foreach (var s in strategies)
            {
                var sig = SafeEval(s, window, config);
                decimal pnl = sig.Action == "buy" ? ret : sig.Action == "sell" ? -ret : 0m;
                if (!stats[s.Name].TryGetValue(regime, out var st)) stats[s.Name][regime] = st = new();
                st.ReturnSum += pnl;
                st.Bars++;
                if (sig.Action is "buy" or "sell") { st.Trades++; if (pnl > 0) st.Wins++; }
            }
        }
        r.AnalyzedBars = Math.Max(0, bars.Count - 1 - warmup);
        r.RegimeBars = regimeBars;

        // 矩陣 + 每策略全程總和
        foreach (var s in strategies)
        {
            r.Matrix[s.Name] = new();
            decimal total = 0m;
            foreach (var (rg, st) in stats[s.Name]) { r.Matrix[s.Name][rg] = Math.Round(st.ReturnSum * 100m, 4); total += st.ReturnSum; }
            r.StrategyTotalPct[s.Name] = Math.Round(total * 100m, 4);
        }

        // 每 regime 最佳策略 + Oracle(每 regime 用最佳之和 = in-sample 上限)
        decimal oracle = 0m;
        foreach (var rg in regimeBars.Keys)
        {
            string bestS = ""; decimal bestV = decimal.MinValue;
            foreach (var s in strategies)
            {
                var v = stats[s.Name].TryGetValue(rg, out var st) ? st.ReturnSum : 0m;
                if (v > bestV) { bestV = v; bestS = s.Name; }
            }
            r.PerRegimeBest[rg] = bestS;
            if (bestV != decimal.MinValue) oracle += bestV;
        }
        r.OracleReturnPct = Math.Round(oracle * 100m, 4);

        // 最佳單策略(baseline)+ 改善%
        var best = r.StrategyTotalPct.OrderByDescending(kv => kv.Value).First();
        r.BestSingleStrategy = best.Key;
        r.BestSingleReturnPct = best.Value;
        r.ImprovementPctVsBestSingle = best.Value != 0m
            ? Math.Round((r.OracleReturnPct - best.Value) / Math.Abs(best.Value) * 100m, 2)
            : 0m;

        return r;
    }

    private static Signal SafeEval(IStrategy s, List<BarData> bars, StrategyConfig config)
    {
        try { return s.Evaluate(bars, config); }
        catch
        {
            return new Signal { SignalId = "err", Strategy = s.Name, Symbol = config.Symbol,
                Exchange = config.Exchange, Action = "hold", Confidence = 0m, Interval = config.Interval };
        }
    }
}
