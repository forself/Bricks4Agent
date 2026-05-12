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
    public string Description => "Harmonic Patterns — 偵測 Gartley/Butterfly/Bat/Crab 5 點諧波形態";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 60;                  // 需 5 個 pivot ≈ 60 根
    public decimal MinCapitalUsdt => 300m;     // pattern 檢出稀有、需大本金週轉

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

        // Batch C+：形態若已失效（突破 PRZ 區間或觸 SL）就不再給訊號
        if (det.Status == HarmonicPatterns.PatternStatus.Invalidated ||
            det.Status == HarmonicPatterns.PatternStatus.SlHit)
        {
            return Hold(config, $"{det.PatternName} ({det.Direction}) status={det.Status} — 已失效、不進場");
        }

        if (distRatio > MaxDistanceFromD)
        {
            reason = $"Detected {det.PatternName} ({det.Direction}) but price {price} is {distRatio:P1} away from D-point {det.Dp} — waiting";
        }
        else if (det.Direction == "bullish")
        {
            action = "buy";
            confidence = Math.Clamp(det.Confidence * (1m - distRatio * 20m), 0.5m, 0.95m);
            // Batch C++：candle 跟 RSI 各自獨立 +0.10、最多 +0.20、cap 0.95
            if (det.HasCandleConfirmation) confidence = Math.Min(0.95m, confidence + 0.10m);
            if (det.HasRsiDivergence)      confidence = Math.Min(0.95m, confidence + 0.10m);
            reason = BuildReason("Bullish", det, price);
        }
        else if (det.Direction == "bearish")
        {
            action = "sell";
            confidence = Math.Clamp(det.Confidence * (1m - distRatio * 20m), 0.5m, 0.95m);
            if (det.HasCandleConfirmation) confidence = Math.Min(0.95m, confidence + 0.10m);
            if (det.HasRsiDivergence)      confidence = Math.Min(0.95m, confidence + 0.10m);
            reason = BuildReason("Bearish", det, price);
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
                // Batch C+ 新欄位（PRZ + 進場確認 + TP/SL）
                ["prz_low"]   = det.PrzLow,
                ["prz_high"]  = det.PrzHigh,
                ["tp1"]       = det.Tp1,
                ["tp2"]       = det.Tp2,
                ["stop_loss"] = det.StopLoss,
                ["risk_reward"] = det.RiskReward,
                ["status"] = det.Status switch
                {
                    HarmonicPatterns.PatternStatus.Open        => 0m,
                    HarmonicPatterns.PatternStatus.Tp1Hit      => 1m,
                    HarmonicPatterns.PatternStatus.Tp2Hit      => 2m,
                    HarmonicPatterns.PatternStatus.SlHit       => -1m,
                    HarmonicPatterns.PatternStatus.Invalidated => -2m,
                    _ => 0m,
                },
                ["bars_since_d"] = det.BarsSinceD,
                ["has_candle_confirm"] = det.HasCandleConfirmation ? 1m : 0m,
                // Batch C++ 新欄位（RSI 背離）
                ["has_rsi_divergence"] = det.HasRsiDivergence ? 1m : 0m,
                ["rsi_at_b"] = det.RsiAtB,
                ["rsi_at_d"] = det.RsiAtD,
            },
        };
    }

    private static string BuildReason(string sideLabel, HarmonicPatterns.Detection det, decimal price)
    {
        var confirmParts = new List<string>();
        if (det.HasCandleConfirmation) confirmParts.Add($"K：{det.ConfirmationSignals}");
        if (det.HasRsiDivergence)      confirmParts.Add($"RSI 背離 (B={det.RsiAtB:F1} → D={det.RsiAtD:F1})");
        var confirmStr = confirmParts.Count > 0
            ? $" ✓ {string.Join(" + ", confirmParts)}"
            : " (尚無確認訊號)";

        return $"{sideLabel} {det.PatternName} @ D={det.Dp}；現價 {price}。" +
               $" PRZ=[{det.PrzLow}, {det.PrzHigh}]。" +
               $" TP1={det.Tp1} TP2={det.Tp2} SL={det.StopLoss} RR={det.RiskReward}。" +
               confirmStr;
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "harmonic_pattern",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
