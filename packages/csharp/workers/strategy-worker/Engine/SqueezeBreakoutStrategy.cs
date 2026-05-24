using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Squeeze 擠壓突破 —— 布林帶(20,2)寬度壓到近 100 bar 低點(低波動擠壓)後、價格突破上/下軌 → 順勢進。
/// 經典「波動收縮 → 擴張」edge;現有策略沒有純擠壓突破(布林是回歸、dual_thrust 不看擠壓)。
///   buy:  寬度 ≤ 近 100 bar 最小寬度 × 1.15(仍在擠壓)且 收盤 > 上軌(向上突破)
///   sell: 同上擠壓 且 收盤 < 下軌(向下突破)
/// 2026-05-25 新增、先在 paper 場驗證(zero 真錢)。
/// </summary>
public class SqueezeBreakoutStrategy : IStrategy
{
    public string Name => "squeeze_breakout";
    public string Description => "布林擠壓突破 — BB(20,2) 寬度壓到近期低點後、突破上/下軌順勢進";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 120;
    public decimal MinCapitalUsdt => 100m;

    private static (decimal upper, decimal lower, decimal mid, decimal width) Bb(List<BarData> bars, int endIdx, int p)
    {
        decimal sma = 0; for (int k = endIdx - p + 1; k <= endIdx; k++) sma += bars[k].Close; sma /= p;
        decimal varSum = 0; for (int k = endIdx - p + 1; k <= endIdx; k++) varSum += (bars[k].Close - sma) * (bars[k].Close - sma);
        decimal sd = (decimal)Math.Sqrt((double)(varSum / p));
        return (sma + 2m * sd, sma - 2m * sd, sma, 4m * sd);   // width = upper − lower = 4·sd
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"need ≥{MinBars} bars");
        int n = bars.Count;
        var (upper, lower, _, width) = Bb(bars, n - 1, 20);
        decimal close = bars[^1].Close;

        decimal minW = decimal.MaxValue;
        for (int i = n - 100; i < n; i++) { var w = Bb(bars, i, 20).width; if (w < minW) minW = w; }
        bool squeezed = width <= minW * 1.15m;

        string action = "hold"; decimal conf = 0m; string reason;
        if (squeezed && close > upper)
        {
            action = "buy"; conf = 0.7m;
            reason = $"擠壓(寬{width:F4}≈低點{minW:F4})+ 收盤 {close:F4} 突破上軌 {upper:F4} — 向上突破";
        }
        else if (squeezed && close < lower)
        {
            action = "sell"; conf = 0.7m;
            reason = $"擠壓 + 收盤 {close:F4} 跌破下軌 {lower:F4} — 向下突破";
        }
        else reason = squeezed ? $"擠壓中、未突破({lower:F4}<{close:F4}<{upper:F4})" : $"波動未收縮(寬{width:F4} vs 低點{minW:F4})";

        return Sig(config, action, conf, reason, new()
        {
            ["price"] = Math.Round(close, 4), ["bb_upper"] = Math.Round(upper, 4),
            ["bb_lower"] = Math.Round(lower, 4), ["bb_width"] = Math.Round(width, 4), ["min_width_100"] = Math.Round(minW, 4),
        });
    }

    private Signal Hold(StrategyConfig c, string r) => Sig(c, "hold", 0m, r, new());
    private Signal Sig(StrategyConfig c, string a, decimal conf, string r, Dictionary<string, decimal> ind) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = Name, Symbol = c.Symbol, Exchange = c.Exchange,
        Action = a, Confidence = conf, Reason = r, Interval = c.Interval, Indicators = ind,
    };
}
