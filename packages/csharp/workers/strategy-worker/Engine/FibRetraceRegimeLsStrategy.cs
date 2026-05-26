using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// H1-Fib 研究實驗(2026-05-26、見 docs/reports/FibRetraceResearch-Log.md):
///   FibRetrace + RegimeDetector 趨勢確認 —— 必須是真正的 TrendingUp/Down regime。
///
/// 假設:baseline fib_retrace_ls 的「hi > li」單看高低先後判趨勢、太鬆,
/// 微小高低差的橫盤會被當趨勢進場(回撤接刀)→ DD 110% 的根因。
/// RegimeDetector 用 SMA50 斜率 + ATR + BB width 給出更嚴的趨勢判定。
///
/// 邏輯與 FibRetraceLsStrategy 一致(60-bar swing + golden zone 0.382-0.618 + Fib 擴展目標),
/// 多兩道閘門:
///   - uptrend(hi > li)→ 必須 regime.Type == TrendingUp
///   - downtrend(li > hi)→ 必須 regime.Type == TrendingDown
///   - 其餘 regime(Squeeze / RangeBound / HighVol / Unclear)→ hold
///
/// 預期:DD 顯著降、OOS edge 保留(雖然 +fold% 可能略降、但 Sharpe 維持 0.25+ 可接受)。
/// 若 H1-Fib 成立(DD <80% + OOS 中位 >0 + Sharpe >0.25)→ 考慮合併回 fib_retrace_ls 加參數。
/// </summary>
public class FibRetraceRegimeLsStrategy : IStrategy
{
    public string Name => "fib_retrace_regime_ls";
    public string Description => "H1-Fib: FibRetrace + RegimeDetector 真趨勢確認 — 降 DD";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 70;   // RegimeDetector 要 70 根
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 60;
    private const decimal MinRangePct = 6m;
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
        if (bars.Count < Math.Max(lookback + 1, MinBars))
            return Hold(config, $"資料不足(需 {Math.Max(lookback + 1, MinBars)}+ 根)");

        // 近 lookback 根的擺動高/低 + index
        int start = bars.Count - lookback;
        decimal high = decimal.MinValue, low = decimal.MaxValue;
        int hi = start, li = start;
        for (int i = start; i < bars.Count; i++)
        {
            if (bars[i].High > high) { high = bars[i].High; hi = i; }
            if (bars[i].Low < low)   { low  = bars[i].Low;  li = i; }
        }
        decimal range = high - low;
        if (low <= 0m || range <= 0m || range / low * 100m < minRangePct)
            return Hold(config, $"擺動幅度不足(<{minRangePct}%)");

        decimal close = bars[^1].Close;
        decimal r = (close - low) / range;
        bool uptrend = hi > li;
        bool golden = r >= GoldenLo && r <= GoldenHi;

        // ── H1-Fib 閘門:RegimeDetector 必須與 fib 自己判定的方向相符 ──
        var regime = RegimeDetector.Detect(bars);
        bool regimeMatchUp   = uptrend  && regime.Type == RegimeDetector.RegimeType.TrendingUp;
        bool regimeMatchDown = !uptrend && regime.Type == RegimeDetector.RegimeType.TrendingDown;
        if (!regimeMatchUp && !regimeMatchDown)
            return Hold(config, $"fib {(uptrend ? "升" : "跌")}勢 但 regime={regime.Type} 不確認 — hold");

        if (!golden)
            return Hold(config, $"r={r:F2} 不在黃金區");

        decimal centerDist = Math.Abs(r - 0.5m);
        string action = uptrend ? "buy" : "sell";
        decimal confidence = Math.Clamp(0.65m + (0.118m - centerDist) * 0.5m, 0.6m, 0.9m);
        string reason = $"[regime {regime.Type}] {(uptrend ? "升勢回撤" : "跌勢反彈")}至黃金區"
                      + $"(r={r:F2}, slope={regime.Sma50Slope:F2}%) — 順勢{(uptrend ? "做多" : "做空")}";

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
                ["swing_high"]      = Math.Round(high, 4),
                ["swing_low"]       = Math.Round(low, 4),
                ["retr_ratio"]      = Math.Round(r, 4),
                ["ext_1272"]        = Math.Round(ext1272, 4),
                ["ext_1618"]        = Math.Round(ext1618, 4),
                ["regime"]          = (decimal)(int)regime.Type,
                ["sma50_slope_pct"] = regime.Sma50Slope,
                ["price"]           = Math.Round(close, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "fib_retrace_regime_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
