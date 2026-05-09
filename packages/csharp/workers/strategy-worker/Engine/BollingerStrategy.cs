using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 布林通道均值回歸策略。
///
/// 典型用法是「觸底反彈、觸頂回落」：
///   - 收盤價 ≤ 下軌 AND 前一根也觸到下軌附近 → Buy（回歸中線）
///   - 收盤價 ≥ 上軌 AND 前一根也觸到上軌附近 → Sell
///   - 介於兩軌之間 → Hold
///
/// 加一層過濾：band_width 太窄（squeeze）不建議進場——等爆發方向確定。
/// </summary>
public class BollingerStrategy : IStrategy
{
    public string Name => "bollinger_bands";
    public string Description => "Bollinger Bands — 均值回歸：觸下軌買、觸上軌賣、squeeze 時觀望";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 25;
    public decimal MinCapitalUsdt => 100m;

    private const int Period = 20;
    private const decimal KSigma = 2m;
    private const decimal SqueezeThreshold = 3m;  // bandWidth < 3% 視為 squeeze，不進場

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < Period + 1) return Hold(config, "Not enough data for Bollinger");

        var current = bars[^1];
        var price = current.Close;
        var b = BollingerBands.Compute(bars, price, Period, KSigma);
        if (b == null) return Hold(config, "Failed to compute bands");

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (b.BandWidth < SqueezeThreshold)
        {
            reason = $"Bollinger squeeze (width={b.BandWidth:F2}%) — 波動過低，等方向確認";
        }
        else if (price <= b.Lower)
        {
            action = "buy";
            // 越貼近下軌（%b 越接近 0）信心越高
            confidence = Math.Clamp(0.6m + (0.2m - b.PercentB) * 2m, 0.5m, 0.95m);
            reason = $"Price {price} touched/below Lower {b.Lower} (%b={b.PercentB:F2}) — 均值回歸買進";
        }
        else if (price >= b.Upper)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (b.PercentB - 0.8m) * 2m, 0.5m, 0.95m);
            reason = $"Price {price} touched/above Upper {b.Upper} (%b={b.PercentB:F2}) — 均值回歸賣出";
        }
        else if (b.PercentB > 0.8m)
        {
            reason = $"價格接近上軌 (%b={b.PercentB:F2})，觀察是否續強";
        }
        else if (b.PercentB < 0.2m)
        {
            reason = $"價格接近下軌 (%b={b.PercentB:F2})，觀察是否反彈";
        }
        else
        {
            reason = $"通道中段 (%b={b.PercentB:F2}) — 無訊號";
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
                ["price"] = Math.Round(price, 4),
                ["bb_upper"] = b.Upper,
                ["bb_mid"] = b.Mid,
                ["bb_lower"] = b.Lower,
                ["bb_width_pct"] = b.BandWidth,
                ["bb_percent_b"] = b.PercentB,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "bollinger_bands",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
