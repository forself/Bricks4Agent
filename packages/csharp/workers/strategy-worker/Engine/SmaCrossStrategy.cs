using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// SMA 交叉策略。
/// - 快線上穿慢線 → Buy
/// - 快線下穿慢線 → Sell
/// - 否則 → Hold
/// </summary>
public class SmaCrossStrategy : IStrategy
{
    public string Name => "sma_cross";

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var fast = config.SmaFast;
        var slow = config.SmaSlow;

        if (bars.Count < slow + 1)
            return HoldSignal(config, "Not enough data");

        var currentFastSma  = Sma(bars, bars.Count - 1, fast);
        var currentSlowSma  = Sma(bars, bars.Count - 1, slow);
        var prevFastSma     = Sma(bars, bars.Count - 2, fast);
        var prevSlowSma     = Sma(bars, bars.Count - 2, slow);

        var currentPrice = bars[^1].Close;
        string action;
        string reason;
        decimal confidence;

        if (prevFastSma <= prevSlowSma && currentFastSma > currentSlowSma)
        {
            action     = "buy";
            reason     = $"SMA{fast} crossed above SMA{slow} (golden cross)";
            confidence = 0.75m;
        }
        else if (prevFastSma >= prevSlowSma && currentFastSma < currentSlowSma)
        {
            action     = "sell";
            reason     = $"SMA{fast} crossed below SMA{slow} (death cross)";
            confidence = 0.75m;
        }
        else
        {
            action     = "hold";
            reason     = $"No crossover. SMA{fast}={currentFastSma:F2}, SMA{slow}={currentSlowSma:F2}";
            confidence = 0.5m;
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = confidence,
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                [$"sma_{fast}"]  = Math.Round(currentFastSma, 4),
                [$"sma_{slow}"]  = Math.Round(currentSlowSma, 4),
                ["price"]        = currentPrice,
            }
        };
    }

    private static decimal Sma(List<BarData> bars, int endIndex, int period)
    {
        var sum = 0m;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
            sum += bars[i].Close;
        return sum / period;
    }

    private static Signal HoldSignal(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "sma_cross", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
