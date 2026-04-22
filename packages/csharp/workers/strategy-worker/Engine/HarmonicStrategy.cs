using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 諧波形態策略。
///
/// 核心邏輯：
///   - 偵測最近 5 個 pivot 是否構成 Gartley / Butterfly / Bat / Crab
///   - bullish 形態 + D 點離當前價格接近 → Buy（預期 D 點反轉往上）
///   - bearish 形態 + D 點接近當前 → Sell
///   - 無形態匹配 → Hold
///
/// 信心度來自兩部分：
///   (a) 形態 Fibonacci 比率匹配程度（HarmonicPatterns.Confidence）
///   (b) D 點距離當前價格的接近程度（太遠 = 還沒到進場點）
///
/// 停損建議位：bullish → X 之下；bearish → X 之上（策略本身不執行停損，交給 risk-worker）
/// </summary>
public class HarmonicStrategy : IStrategy
{
    public string Name => "harmonic_pattern";

    private const int PivotWindow = 3;
    private const decimal MaxDistanceFromD = 0.02m;  // 當前價格距離 D 點 > 2% 視為還沒到位

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < 30)
            return Hold(config, "Not enough data for harmonic detection (need ≥ 30 bars)");

        var det = HarmonicPatterns.Detect(bars, PivotWindow);
        var price = bars[^1].Close;

        if (det.PatternName == "none" || det.PatternName == "")
            return Hold(config, "No harmonic pattern detected in last 5 pivots");

        // D 點接近度
        var distRatio = det.Dp == 0 ? 1m : Math.Abs(price - det.Dp) / det.Dp;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (distRatio > MaxDistanceFromD)
        {
            reason = $"Detected {det.PatternName} ({det.Direction}) but price {price} is {distRatio:P1} away from D-point {det.Dp} — waiting";
        }
        else if (det.Direction == "bullish")
        {
            action = "buy";
            confidence = Math.Clamp(det.Confidence * (1m - distRatio * 20m), 0.5m, 0.95m);
            reason = $"Bullish {det.PatternName} complete @ D={det.Dp}; current {price}. Ratios: AB={det.AbRatio} BC={det.BcRatio} CD={det.CdRatio} AD={det.AdRatio}. Stop below X={det.Xp}";
        }
        else if (det.Direction == "bearish")
        {
            action = "sell";
            confidence = Math.Clamp(det.Confidence * (1m - distRatio * 20m), 0.5m, 0.95m);
            reason = $"Bearish {det.PatternName} complete @ D={det.Dp}; current {price}. Ratios: AB={det.AbRatio} BC={det.BcRatio} CD={det.CdRatio} AD={det.AdRatio}. Stop above X={det.Xp}";
        }
        else
        {
            reason = $"{det.PatternName} detected but direction unclear";
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
                ["pattern_confidence"] = det.Confidence,
                ["pattern_direction"] = det.Direction == "bullish" ? 1m : det.Direction == "bearish" ? -1m : 0m,
                ["X"] = det.Xp,
                ["A"] = det.Ap,
                ["B"] = det.Bp,
                ["C"] = det.Cp,
                ["D"] = det.Dp,
                ["ab_ratio"] = det.AbRatio,
                ["bc_ratio"] = det.BcRatio,
                ["cd_ratio"] = det.CdRatio,
                ["ad_ratio"] = det.AdRatio,
                ["distance_from_d_pct"] = Math.Round(distRatio * 100m, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "harmonic_pattern",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
