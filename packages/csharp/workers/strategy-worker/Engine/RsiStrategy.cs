using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// RSI 策略。
/// - RSI &lt; 超賣線 → Buy
/// - RSI &gt; 超買線 → Sell
/// - 否則 → Hold
/// </summary>
public class RsiStrategy : IStrategy
{
    public string Name => "rsi_oversold";

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var period = config.RsiPeriod;
        if (bars.Count < period + 1)
            return HoldSignal(config, "Not enough data for RSI");

        var rsi = CalcRsi(bars, period);
        var currentPrice = bars[^1].Close;

        string action;
        string reason;
        decimal confidence;

        if (rsi < config.RsiOversold)
        {
            action     = "buy";
            reason     = $"RSI({period})={rsi:F1} < {config.RsiOversold} (oversold)";
            confidence = Math.Min(1m, (config.RsiOversold - rsi) / config.RsiOversold + 0.5m);
        }
        else if (rsi > config.RsiOverbought)
        {
            action     = "sell";
            reason     = $"RSI({period})={rsi:F1} > {config.RsiOverbought} (overbought)";
            confidence = Math.Min(1m, (rsi - config.RsiOverbought) / (100 - config.RsiOverbought) + 0.5m);
        }
        else
        {
            action     = "hold";
            reason     = $"RSI({period})={rsi:F1} — neutral zone";
            confidence = 0.5m;
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
                ["rsi"]   = Math.Round(rsi, 2),
                ["price"] = currentPrice,
            }
        };
    }

    private static decimal CalcRsi(List<BarData> bars, int period)
    {
        var gains = 0m;
        var losses = 0m;

        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var diff = bars[i].Close - bars[i - 1].Close;
            if (diff > 0) gains += diff;
            else losses -= diff;
        }

        var avgGain = gains / period;
        var avgLoss = losses / period;

        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private static Signal HoldSignal(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "rsi_oversold", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
