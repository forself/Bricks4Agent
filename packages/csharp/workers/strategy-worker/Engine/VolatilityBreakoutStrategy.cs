using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 波動率擠壓突破策略 —— 只在「低波動擠壓 + 價格突破近期區間」時出手,其餘時間 hold。
///
/// 設計目的是跟既有方向型策略正交:它不預測方向,而是等市場自己從擠壓裡選邊。
///   1. VolatilityRegime 算 ATR 百分位;百分位 &lt; squeeze_pct(預設 0.3)才視為擠壓。
///   2. 擠壓中,若收盤突破近 N 根的高點 → buy;跌破近 N 根低點 → sell;否則 hold。
/// 因為大多數時間不是擠壓、就是沒突破,這策略訊號稀疏 —— 這正是它在 ensemble 裡的價值:
/// 只在「變盤點」加一票,不跟趨勢/動量策略在同方向疊加雜訊。
/// </summary>
public class VolatilityBreakoutStrategy : IStrategy
{
    public string Name => "volatility_breakout";
    public string Description => "Volatility Breakout — ATR 擠壓 + 突破近期區間才進場,訊號稀疏、與方向型正交";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 120;
    public decimal MinCapitalUsdt => 150m;

    private const int AtrPeriod = 14;
    private const int VolLookback = 100;
    private const decimal SqueezePct = 0.3m;
    private const int BreakoutLookback = 20;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["vol_squeeze_pct"]       = new() { Type = "decimal", Default = 0.3m, Choices = new object[] { 0.2m, 0.25m, 0.3m, 0.35m }, Description = "判定擠壓的 ATR 百分位上界" },
        ["vol_breakout_lookback"] = new() { Type = "int",     Default = 20,  Min = 10, Max = 40, Step = 5,                       Description = "突破區間回看根數" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var squeezePct = config.GetParam("vol_squeeze_pct", SqueezePct);
        var brkLookback = config.GetParam("vol_breakout_lookback", BreakoutLookback);

        var vr = VolatilityRegime.Compute(bars, AtrPeriod, VolLookback);
        if (vr == null) return Hold(config, "波動率百分位無法計算(資料不足)");
        var (atr, pct) = vr.Value;

        var price = bars[^1].Close;
        bool squeeze = pct < squeezePct;

        // 近 brkLookback 根(不含當前)的高低點 = 突破基準
        int start = bars.Count - 1 - brkLookback;
        if (start < 0) start = 0;
        decimal priorHigh = decimal.MinValue, priorLow = decimal.MaxValue;
        for (int i = start; i < bars.Count - 1; i++)
        {
            if (bars[i].High > priorHigh) priorHigh = bars[i].High;
            if (bars[i].Low  < priorLow)  priorLow  = bars[i].Low;
        }

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (!squeeze)
        {
            reason = $"波動率百分位 {pct:P0} ≥ 擠壓門檻 {squeezePct:P0} — 非擠壓、不追";
        }
        else if (price > priorHigh)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (squeezePct - pct), 0.5m, 0.9m);
            reason = $"擠壓({pct:P0})後收盤 {price} 突破近 {brkLookback} 根高點 {priorHigh} → 向上變盤";
        }
        else if (price < priorLow)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (squeezePct - pct), 0.5m, 0.9m);
            reason = $"擠壓({pct:P0})後收盤 {price} 跌破近 {brkLookback} 根低點 {priorLow} → 向下變盤";
        }
        else
        {
            reason = $"擠壓中({pct:P0})但價 {price} 仍在 [{priorLow}, {priorHigh}] 區間內 — 等突破";
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                ["price"]           = Math.Round(price, 4),
                ["atr"]             = atr,
                ["vol_percentile"]  = pct,
                ["prior_high"]      = Math.Round(priorHigh, 4),
                ["prior_low"]       = Math.Round(priorLow, 4),
                ["squeeze"]         = squeeze ? 1m : 0m,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "volatility_breakout",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
