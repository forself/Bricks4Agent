using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// OI 確認突破(2026-06-10 從零開發、crypto 專屬)。
///
/// 機制:價格突破近期高/低點時,用 OI 變化區分「真突破 vs 假突破」:
///   - 上破 + OI「上升」(新多單進場、有新錢)→ 真突破 → LONG 跟趨勢
///   - 下破 + OI「上升」(新空單進場)→ 真破底 → SHORT 跟趨勢
///   - 突破但 OI「持平/下降」(只是空頭回補/多頭平倉、沒新錢)→ 假突破 → 不追
///
/// 跟現有 vol_breakout 的差異 = OI 確認(過濾掉沒有新資金支撐的假突破)。
/// 用 BarData.OpenInterest + Donchian 突破。
/// </summary>
public class OiBreakoutStrategy : IStrategy
{
    private readonly string _name;
    private readonly int _lookback;
    private readonly decimal _oiRise;   // OI 上升門檻(新錢確認)

    public OiBreakoutStrategy(string name = "oi_breakout", int lookback = 20, decimal oiRise = 0.03m)
    {
        _name = name;
        _lookback = lookback;
        _oiRise = oiRise;
    }

    public string Name => _name;
    public string Description => $"OI 確認突破 — {_lookback}日 Donchian 突破 + OI 上升(≥{_oiRise:P0}、新錢確認)→ 跟趨勢(過濾假突破)";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["oibrk_lookback"] = new() { Type = "decimal", Default = 20m,   Choices = new object[] { 10m, 20m, 30m, 55m },        Description = "Donchian 突破回看窗" },
        ["oibrk_oi_rise"]  = new() { Type = "decimal", Default = 0.03m, Choices = new object[] { 0.0m, 0.02m, 0.03m, 0.05m }, Description = "OI 上升門檻(新錢確認)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        int lb = (int)config.GetParam("oibrk_lookback", (decimal)_lookback);
        var oiRise = config.GetParam("oibrk_oi_rise", _oiRise);
        if (lb < 5) lb = 5;
        if (bars.Count < lb + 2) return Hold(config, "lookback 不足");

        var last = bars[^1]; var prev = bars[^2];
        // 突破水準:不含今日的前 lb 根 high/low(Donchian)
        decimal hi = decimal.MinValue, lo = decimal.MaxValue;
        for (int i = bars.Count - 1 - lb; i < bars.Count - 1; i++)
        {
            if (bars[i].High > hi) hi = bars[i].High;
            if (bars[i].Low < lo) lo = bars[i].Low;
        }

        if (!(last.OpenInterest is decimal oiCur && oiCur > 0m &&
              prev.OpenInterest is decimal oiPrev && oiPrev > 0m))
            return Hold(config, "無 OI 資料(非 perp 或未接 metrics)— 自動降級");
        decimal oiChange = (oiCur - oiPrev) / oiPrev;
        bool newMoney = oiChange >= oiRise;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (last.Close > hi && newMoney)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (oiChange - oiRise), 0.5m, 0.9m);
            reason = $"上破 {lb}日高 {hi:F4} + OI 升 {oiChange:P1}(新錢進場)= 真突破 → LONG 跟趨勢";
        }
        else if (last.Close < lo && newMoney)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (oiChange - oiRise), 0.5m, 0.9m);
            reason = $"下破 {lb}日低 {lo:F4} + OI 升 {oiChange:P1}(新空進場)= 真破底 → SHORT 跟趨勢";
        }
        else if (last.Close > hi || last.Close < lo)
        {
            reason = $"突破但 OI 變化 {oiChange:P1} < {oiRise:P0}(沒新錢、可能假突破/回補)— 不追";
        }
        else
        {
            reason = $"未突破 {lb}日區間 [{lo:F4}, {hi:F4}] — 觀望";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name,
            Symbol = config.Symbol,
            Exchange = config.Exchange,
            Action = action,
            Confidence = Math.Round(confidence, 2),
            Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["oi_change"] = Math.Round(oiChange, 4),
                ["brk_hi"] = Math.Round(hi, 6),
                ["brk_lo"] = Math.Round(lo, 6),
            },
        };
    }

    private Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = _name,
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
