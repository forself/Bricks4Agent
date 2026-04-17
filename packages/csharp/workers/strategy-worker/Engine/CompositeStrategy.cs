using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 複合策略 — 同時跑多個策略，用加權投票決定最終訊號。
/// </summary>
public class CompositeStrategy : IStrategy
{
    public string Name => "composite";

    private readonly List<(IStrategy Strategy, decimal Weight)> _strategies;

    public CompositeStrategy(List<(IStrategy, decimal)> strategies)
    {
        _strategies = strategies;
    }

    /// <summary>預設組合：SMA Cross + RSI + MACD，等權重。</summary>
    public static CompositeStrategy Default() => new(new List<(IStrategy, decimal)>
    {
        (new SmaCrossStrategy(), 1.0m),
        (new RsiStrategy(),      1.0m),
        (new MacdStrategy(),     1.0m),
    });

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var signals = _strategies
            .Select(s => (Signal: s.Strategy.Evaluate(bars, config), s.Weight))
            .ToList();

        // 加權投票
        decimal buyScore  = 0, sellScore = 0, holdScore = 0;
        var allIndicators = new Dictionary<string, decimal>();

        foreach (var (signal, weight) in signals)
        {
            switch (signal.Action)
            {
                case "buy":  buyScore  += signal.Confidence * weight; break;
                case "sell": sellScore += signal.Confidence * weight; break;
                default:     holdScore += signal.Confidence * weight; break;
            }

            foreach (var kv in signal.Indicators)
                allIndicators[$"{signal.Strategy}.{kv.Key}"] = kv.Value;
        }

        var totalWeight = _strategies.Sum(s => s.Weight);
        buyScore  /= totalWeight;
        sellScore /= totalWeight;
        holdScore /= totalWeight;

        string action;
        decimal confidence;
        if (buyScore > sellScore && buyScore > holdScore)
        {
            action = "buy";
            confidence = buyScore;
        }
        else if (sellScore > buyScore && sellScore > holdScore)
        {
            action = "sell";
            confidence = sellScore;
        }
        else
        {
            action = "hold";
            confidence = holdScore;
        }

        var reasons = signals
            .Select(s => $"[{s.Signal.Strategy}] {s.Signal.Action}({s.Signal.Confidence:P0}): {s.Signal.Reason}")
            .ToList();

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = string.Join(" | ", reasons),
            Interval   = config.Interval,
            Indicators = allIndicators,
        };
    }
}
