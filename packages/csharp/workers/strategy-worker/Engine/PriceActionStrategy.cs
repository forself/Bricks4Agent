using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;
using PaDetection = StrategyWorker.Engine.Indicators.PriceActionPatterns.Detection;
using PaDirection = StrategyWorker.Engine.Indicators.PriceActionPatterns.Direction;

namespace StrategyWorker.Engine;

/// <summary>
/// Price Action 形態學策略——把 6 種 K 線型態彙整成 buy/sell/hold。
///
/// 規則：
///   1. 跑 `PriceActionPatterns.DetectAll`、取最近 RecentWindowBars 根內的訊號
///   2. 對 Bullish / Bearish 分別加總 confidence（neutral 跳過）
///   3. score = (bullish_sum - bearish_sum) / max(1, total_sum)
///      → score ∈ [-1, 1]
///   4. score > 0.4  → buy
///      score < -0.4 → sell
///      其餘          → hold
///
/// 信心度：abs(score) clamp 到 [0.5, 0.95] 線性映射。
///
/// 設計對標：朋友 ai-quant-starter2/app/services/price_action_engine.py。
/// 不直接對應任何 strategy_selector 函式、本策略是把 PA 訊號集合彙整成決策。
/// </summary>
public class PriceActionStrategy : IStrategy
{
    public string Name => "price_action";
    public string Description => "Price Action 形態學 — 6 種 K 線型態加權";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 30;
    public decimal MinCapitalUsdt => 50m;

    private const int RecentWindowBars = 5;
    private const decimal ActionThreshold = 0.4m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["recent_window"] = new() { Type = "int", Default = 5, Min = 1, Max = 20, Step = 1, Description = "計分視窗（近 N 根）" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars == null || bars.Count < MinBars)
            return Hold(config, "Not enough data for PriceAction");

        var detections = PriceActionPatterns.DetectAll(bars, RecentWindowBars);
        if (detections.Count == 0)
            return Hold(config, $"近 {RecentWindowBars} 根無 PA 訊號");

        decimal bullSum = 0m, bearSum = 0m;
        var typesSeen = new List<string>();
        foreach (var d in detections)
        {
            typesSeen.Add($"{d.Type}({d.Direction})");
            if (d.Direction == PaDirection.Bullish) bullSum += d.Confidence;
            else if (d.Direction == PaDirection.Bearish) bearSum += d.Confidence;
            // Neutral 不計分、僅作 context
        }

        var total = bullSum + bearSum;
        var score = total == 0m ? 0m : (bullSum - bearSum) / total;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (score >= ActionThreshold)
        {
            action = "buy";
            confidence = Math.Clamp(0.5m + score / 2m, 0.5m, 0.95m);
            reason = $"PA 訊號偏多 (score={score:F2}, bull={bullSum:F2} bear={bearSum:F2}) 訊號: {string.Join(", ", typesSeen.Take(4))}";
        }
        else if (score <= -ActionThreshold)
        {
            action = "sell";
            confidence = Math.Clamp(0.5m - score / 2m, 0.5m, 0.95m);
            reason = $"PA 訊號偏空 (score={score:F2}, bull={bullSum:F2} bear={bearSum:F2}) 訊號: {string.Join(", ", typesSeen.Take(4))}";
        }
        else
        {
            reason = $"PA 訊號中性 (score={score:F2}, bull={bullSum:F2} bear={bearSum:F2}) 訊號: {string.Join(", ", typesSeen.Take(4))}";
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
                ["pa_score"]      = Math.Round(score, 4),
                ["pa_bull_sum"]   = Math.Round(bullSum, 4),
                ["pa_bear_sum"]   = Math.Round(bearSum, 4),
                ["pa_count"]      = detections.Count,
                ["pa_latest_bar"] = detections[0].BarIndex,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "price_action",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
