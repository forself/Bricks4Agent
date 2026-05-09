using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// MACD 策略。
/// - MACD line 上穿 signal line → Buy
/// - MACD line 下穿 signal line → Sell
/// - 否則 → Hold
/// </summary>
public class MacdStrategy : IStrategy
{
    public string Name => "macd_divergence";
    public string Description => "MACD Crossover — MACD 與 Signal 線交叉";
    public StrategyCategory Category => StrategyCategory.Momentum;
    public int MinBars => 36;                  // slow=26 + signal=9 + 1
    public decimal MinCapitalUsdt => 80m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["macd_fast"]   = new() { Type = "int", Default = 12, Min = 8,  Max = 20, Step = 2, Description = "快線週期" },
        ["macd_slow"]   = new() { Type = "int", Default = 26, Min = 20, Max = 40, Step = 2, Description = "慢線週期" },
        ["macd_signal"] = new() { Type = "int", Default = 9,  Min = 5,  Max = 15, Step = 1, Description = "訊號線週期" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var fast   = config.MacdFast;
        var slow   = config.MacdSlow;
        var signal = config.MacdSignal;

        if (bars.Count < slow + signal + 1)
            return HoldSignal(config, "Not enough data for MACD");

        var closes = bars.Select(b => b.Close).ToList();

        var fastEma = CalcEma(closes, fast);
        var slowEma = CalcEma(closes, slow);

        // MACD line = fast EMA - slow EMA（從 slow-1 開始）
        var macdLine = new List<decimal>();
        int offset = slow - fast;
        for (int i = 0; i < slowEma.Count; i++)
        {
            var fIdx = i + offset;
            if (fIdx >= 0 && fIdx < fastEma.Count)
                macdLine.Add(fastEma[fIdx] - slowEma[i]);
        }

        if (macdLine.Count < signal + 1)
            return HoldSignal(config, "Not enough MACD data");

        var signalLine = CalcEma(macdLine, signal);
        if (signalLine.Count < 2)
            return HoldSignal(config, "Not enough signal line data");

        var currMacd   = macdLine[^1];
        var currSignal = signalLine[^1];
        var prevMacd   = macdLine[^2];
        var prevSignal = signalLine.Count >= 2 ? signalLine[^2] : currSignal;
        var histogram  = currMacd - currSignal;

        string action;
        string reason;
        decimal confidence;

        if (prevMacd <= prevSignal && currMacd > currSignal)
        {
            action     = "buy";
            reason     = $"MACD crossed above signal (bullish). Histogram={histogram:F4}";
            confidence = 0.7m;
        }
        else if (prevMacd >= prevSignal && currMacd < currSignal)
        {
            action     = "sell";
            reason     = $"MACD crossed below signal (bearish). Histogram={histogram:F4}";
            confidence = 0.7m;
        }
        else
        {
            action     = "hold";
            reason     = $"No MACD crossover. MACD={currMacd:F4}, Signal={currSignal:F4}, Hist={histogram:F4}";
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
                ["macd"]      = Math.Round(currMacd, 4),
                ["signal"]    = Math.Round(currSignal, 4),
                ["histogram"] = Math.Round(histogram, 4),
                ["price"]     = bars[^1].Close,
            }
        };
    }

    private static List<decimal> CalcEma(List<decimal> values, int period)
    {
        var result = new List<decimal>();
        if (values.Count < period) return result;

        var sum = 0m;
        for (int i = 0; i < period; i++) sum += values[i];
        var ema = sum / period;
        result.Add(ema);

        var mult = 2m / (period + 1);
        for (int i = period; i < values.Count; i++)
        {
            ema = (values[i] - ema) * mult + ema;
            result.Add(ema);
        }
        return result;
    }

    private static Signal HoldSignal(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "macd_divergence", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
