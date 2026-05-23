using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 斐波那契回撤(多空)—— 趨勢方向上,等價格回撤到「黃金區(0.382–0.618)」順勢進場,
/// 用 Fib 擴展(1.272/1.618)當停利目標。完全自寫、不依賴既有指標類:
///   1. 近 lookback 根找擺動高/低 + 各自 index → 高在後=升勢、低在後=跌勢。
///   2. 回撤比例 r = (close − low)/(high − low)。
///   3. 升勢且 r ∈ [0.382, 0.618](回檔到黃金區)→ buy;跌勢且 r ∈ 同區(反彈到黃金區)→ sell(做空)。
///   4. Fib 擴展目標寫進 Indicators(ext_1272 / ext_1618):升勢在高之上、跌勢在低之下。
/// 「順勢 + 黃金區」過濾掉逆勢接刀;進在回檔/反彈 → 跟動量/突破家族的進場點錯開(去相關來源)。
/// 多空對稱:long-only sell=平倉、LongShortBacktestEngine sell=反手做空。無 lookahead:全用回看資料。
/// </summary>
public class FibRetraceLsStrategy : IStrategy
{
    public string Name => "fib_retrace_ls";
    public string Description => "Fib 回撤(多空)— 順勢回撤黃金區進場、Fib 擴展當目標";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 70;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 60;
    private const decimal MinRangePct = 6m;    // 擺動幅度需 ≥ 此 %(濾掉沒方向的盤整)
    private const decimal GoldenLo = 0.382m;
    private const decimal GoldenHi = 0.618m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["fib_lookback"]      = new() { Type = "int",     Default = Lookback,    Min = 30, Max = 120, Step = 10, Description = "擺動回看根數" },
        ["fib_min_range_pct"] = new() { Type = "decimal", Default = MinRangePct, Min = 3,  Max = 15,  Step = 1,  Description = "最小擺動幅度 %" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int lookback = config.GetParam("fib_lookback", Lookback);
        decimal minRangePct = config.GetParam("fib_min_range_pct", MinRangePct);
        if (bars.Count < lookback + 1) return Hold(config, $"資料不足(需 {lookback + 1}+ 根)");

        // 近 lookback 根的擺動高/低 + index(不含序列外)
        int start = bars.Count - lookback;
        decimal high = decimal.MinValue, low = decimal.MaxValue;
        int hi = start, li = start;
        for (int i = start; i < bars.Count; i++)
        {
            if (bars[i].High > high) { high = bars[i].High; hi = i; }
            if (bars[i].Low < low) { low = bars[i].Low; li = i; }
        }
        decimal range = high - low;
        if (low <= 0m || range <= 0m || range / low * 100m < minRangePct)
            return Hold(config, $"擺動幅度不足(<{minRangePct}%)— 無明確趨勢");

        decimal close = bars[^1].Close;
        decimal r = (close - low) / range;                 // 0=在低、1=在高
        bool uptrend = hi > li;                              // 高在後 → 升勢(低→高)
        bool golden = r >= GoldenLo && r <= GoldenHi;       // 黃金區
        decimal centerDist = Math.Abs(r - 0.5m);            // 離黃金區中心多遠(越近信心略高)

        string action; decimal confidence; string reason;
        if (uptrend && golden)
        {
            action = "buy";
            confidence = Math.Clamp(0.65m + (0.118m - centerDist) * 0.5m, 0.6m, 0.9m);
            reason = $"升勢回撤至黃金區(r={r:F2}, 高 {high:F2}/低 {low:F2})— 順勢做多";
        }
        else if (!uptrend && golden)
        {
            action = "sell";
            confidence = Math.Clamp(0.65m + (0.118m - centerDist) * 0.5m, 0.6m, 0.9m);
            reason = $"跌勢反彈至黃金區(r={r:F2}, 高 {high:F2}/低 {low:F2})— 順勢做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"r={r:F2} 不在黃金區 或 趨勢/位置不符 — 維持";
        }

        // Fib 擴展目標(把「宣告未用」的擴展用起來):升勢在高之上、跌勢在低之下
        decimal ext1272 = uptrend ? low + 1.272m * range : high - 1.272m * range;
        decimal ext1618 = uptrend ? low + 1.618m * range : high - 1.618m * range;

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["swing_high"] = Math.Round(high, 4),
                ["swing_low"]  = Math.Round(low, 4),
                ["retr_ratio"] = Math.Round(r, 4),
                ["ext_1272"]   = Math.Round(ext1272, 4),
                ["ext_1618"]   = Math.Round(ext1618, 4),
                ["price"]      = Math.Round(close, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "fib_retrace_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
