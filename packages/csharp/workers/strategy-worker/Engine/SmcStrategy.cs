using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// SMC（Smart Money Concepts）策略 —— 機構派價格結構交易。
///
/// 進場（見 <see cref="Smc"/>）：
///   多頭結構（BOS_Up）+ 價回測有效 bullish Order Block / FVG → buy
///   空頭結構（BOS_Down）+ 價反彈到 bearish OB / FVG          → sell
///   剛發生 CHoCH（結構轉向）→ 早期反轉訊號
///   無回測觸發 → hold
///
/// 停損建議交給 risk-worker / AutoTrader（策略只給方向 + 信心 + zone 區間參考）。
/// </summary>
public class SmcStrategy : IStrategy
{
    public string Name => "smc";
    public string Description => "Smart Money Concepts — BOS/CHoCH 結構 + Order Block / FVG 回測進場";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 40;                  // pivot window 3 + 結構 + 回測空間
    public decimal MinCapitalUsdt => 300m;     // 觸發較稀疏、需週轉本金

    private const int PivotWindow = 3;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars)
            return Hold(config, $"Not enough data for SMC (need ≥ {MinBars} bars)");

        var st = Smc.Detect(bars, PivotWindow);
        var price = bars[^1].Close;

        var action = st.Signal;                       // buy / sell / hold
        var confidence = action == "hold" ? 0m : Math.Clamp(st.Confidence, 0.5m, 0.95m);

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name,
            Symbol = config.Symbol,
            Exchange = config.Exchange,
            Action = action,
            Confidence = Math.Round(confidence, 2),
            Reason = st.Reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["price"]           = Math.Round(price, 4),
                ["trend"]           = st.Trend == "up" ? 1m : st.Trend == "down" ? -1m : 0m,
                ["break_type"]      = st.BreakType switch
                {
                    "BOS_Up"     => 1m,
                    "BOS_Down"   => -1m,
                    "CHoCH_Up"   => 2m,
                    "CHoCH_Down" => -2m,
                    _ => 0m,
                },
                ["bars_since_break"] = st.BarsSinceBreak,
                ["signal_type"]     = st.SignalType switch
                {
                    "OB_Retest"  => 1m,
                    "FVG_Retest" => 2m,
                    "CHoCH"      => 3m,
                    _ => 0m,
                },
                ["zone_low"]        = st.ZoneLow,
                ["zone_high"]       = st.ZoneHigh,
                ["active_bull_ob"]  = st.ActiveBullObCount,
                ["active_bear_ob"]  = st.ActiveBearObCount,
                ["active_bull_fvg"] = st.ActiveBullFvgCount,
                ["active_bear_fvg"] = st.ActiveBearFvgCount,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "smc",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
