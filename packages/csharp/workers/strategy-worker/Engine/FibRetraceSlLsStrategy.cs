using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// H2-Fib 研究實驗(2026-05-26、見 docs/reports/FibRetraceResearch-Log.md):
///   FibRetraceLs + textbook Fib 失效停損 ── 跌破 swing_low(升勢)或漲破 swing_high(跌勢)即平倉。
///
/// baseline FibRetraceLsStrategy 沒自帶 SL → 趨勢一旦破、策略不知道、抱單到反向訊號 → DD 失控
/// (UsableCombos 顯示 long-only DD 110%、LS LTC DD 46%、BNB DD 35%)。
///
/// 改動:Signal.StopPrice 設為 textbook Fib invalidation level:
///   - bullish:  SL = swing_low  × (1 − buffer)
///   - bearish: SL = swing_high × (1 + buffer)
/// buffer 預設 0.5%、避免被假突破/wick 觸發。
///
/// 邏輯與 FibRetraceLsStrategy 完全相同(60-bar swing、golden zone、Fib 擴展目標),
/// 唯一差別是 Signal.StopPrice 多設一個值給 LongShortBacktestEngine 讀(2026-05-26 加 SL 支援)。
///
/// 預期:DD 大幅降(textbook SL 該本來就有)、edge 可能略降(some 真反轉前的 wick 會觸發 SL)、
/// 但綜合 Sharpe 應提升(風險換報酬比改善)。
/// </summary>
public class FibRetraceSlLsStrategy : IStrategy
{
    public string Name => "fib_retrace_sl_ls";
    public string Description => "H2-Fib: FibRetrace + textbook Fib 失效停損(跌破 swing_low/漲破 swing_high)";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 70;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 60;
    private const decimal MinRangePct = 6m;
    private const decimal GoldenLo = 0.382m;
    private const decimal GoldenHi = 0.618m;
    private const decimal SlBuffer = 0.005m;   // 0.5% 緩衝、避免被 wick 觸發

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["fib_lookback"]      = new() { Type = "int",     Default = Lookback,    Min = 30, Max = 120, Step = 10, Description = "擺動回看根數" },
        ["fib_min_range_pct"] = new() { Type = "decimal", Default = MinRangePct, Min = 3,  Max = 15,  Step = 1,  Description = "最小擺動幅度 %" },
        ["fib_sl_buffer"]     = new() { Type = "decimal", Default = SlBuffer,    Min = 0m, Max = 0.02m, Step = 0.005m, Description = "SL 緩衝(swing 點 × (1±buffer))" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int lookback = config.GetParam("fib_lookback", Lookback);
        decimal minRangePct = config.GetParam("fib_min_range_pct", MinRangePct);
        decimal slBuffer = config.GetParam("fib_sl_buffer", SlBuffer);
        if (bars.Count < lookback + 1) return Hold(config, $"資料不足");

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

        if (!golden) return Hold(config, $"r={r:F2} 不在黃金區");

        decimal centerDist = Math.Abs(r - 0.5m);
        string action; decimal sl;
        if (uptrend)
        {
            action = "buy";
            sl = low * (1m - slBuffer);     // bullish SL:swing_low 之下 buffer%
        }
        else
        {
            action = "sell";
            sl = high * (1m + slBuffer);    // bearish SL:swing_high 之上 buffer%
        }
        decimal confidence = Math.Clamp(0.65m + (0.118m - centerDist) * 0.5m, 0.6m, 0.9m);
        decimal ext1272 = uptrend ? low + 1.272m * range : high - 1.272m * range;
        decimal ext1618 = uptrend ? low + 1.618m * range : high - 1.618m * range;
        string reason = $"[SL@{sl:F2}] {(uptrend ? "升勢回撤" : "跌勢反彈")}至黃金區"
                      + $"(r={r:F2}, 高 {high:F2}/低 {low:F2}) — 順勢{(uptrend ? "做多" : "做空")}";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            StopPrice = Math.Round(sl, 4),         // ★ H2-Fib 核心改動:engine 會讀這個
            TargetPrice = Math.Round(ext1272, 4),  // 順便給 TP:Fib 1.272 擴展
            Indicators = new()
            {
                ["swing_high"] = Math.Round(high, 4),
                ["swing_low"]  = Math.Round(low, 4),
                ["retr_ratio"] = Math.Round(r, 4),
                ["ext_1272"]   = Math.Round(ext1272, 4),
                ["ext_1618"]   = Math.Round(ext1618, 4),
                ["sl_price"]   = Math.Round(sl, 4),
                ["price"]      = Math.Round(close, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "fib_retrace_sl_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
