using QuoteWorker.Models;

namespace QuoteWorker.Indicators;

/// <summary>
/// 技術指標計算引擎。
/// 所有計算基於 OhlcvBar 的 Close 價格。
/// </summary>
public static class TechnicalIndicators
{
    /// <summary>Simple Moving Average</summary>
    public static IndicatorResult SMA(List<OhlcvBar> bars, int period)
    {
        var series = new List<TimestampedValue>();
        for (int i = period - 1; i < bars.Count; i++)
        {
            var sum = 0m;
            for (int j = i - period + 1; j <= i; j++)
                sum += bars[j].Close;
            series.Add(new TimestampedValue { Time = bars[i].OpenTime, Value = Math.Round(sum / period, 4) });
        }

        return new IndicatorResult
        {
            Symbol    = bars.FirstOrDefault()?.Symbol ?? "",
            Indicator = "SMA",
            Interval  = bars.FirstOrDefault()?.Interval ?? "1d",
            Period    = period,
            Timestamp = series.LastOrDefault()?.Time ?? DateTime.UtcNow,
            Value     = series.LastOrDefault()?.Value ?? 0,
            Series    = series,
        };
    }

    /// <summary>Exponential Moving Average</summary>
    public static IndicatorResult EMA(List<OhlcvBar> bars, int period)
    {
        var series = new List<TimestampedValue>();
        if (bars.Count < period) return EmptyResult(bars, "EMA", period);

        // 第一個 EMA = SMA of first `period` bars
        var sum = 0m;
        for (int i = 0; i < period; i++)
            sum += bars[i].Close;
        var ema = sum / period;
        series.Add(new TimestampedValue { Time = bars[period - 1].OpenTime, Value = Math.Round(ema, 4) });

        var multiplier = 2m / (period + 1);
        for (int i = period; i < bars.Count; i++)
        {
            ema = (bars[i].Close - ema) * multiplier + ema;
            series.Add(new TimestampedValue { Time = bars[i].OpenTime, Value = Math.Round(ema, 4) });
        }

        return new IndicatorResult
        {
            Symbol    = bars.FirstOrDefault()?.Symbol ?? "",
            Indicator = "EMA",
            Interval  = bars.FirstOrDefault()?.Interval ?? "1d",
            Period    = period,
            Timestamp = series.LastOrDefault()?.Time ?? DateTime.UtcNow,
            Value     = series.LastOrDefault()?.Value ?? 0,
            Series    = series,
        };
    }

    /// <summary>Relative Strength Index</summary>
    public static IndicatorResult RSI(List<OhlcvBar> bars, int period = 14)
    {
        var series = new List<TimestampedValue>();
        if (bars.Count < period + 1) return EmptyResult(bars, "RSI", period);

        var gains = new decimal[bars.Count];
        var losses = new decimal[bars.Count];
        for (int i = 1; i < bars.Count; i++)
        {
            var diff = bars[i].Close - bars[i - 1].Close;
            gains[i]  = diff > 0 ? diff : 0;
            losses[i] = diff < 0 ? -diff : 0;
        }

        // 初始平均
        var avgGain = 0m;
        var avgLoss = 0m;
        for (int i = 1; i <= period; i++)
        {
            avgGain += gains[i];
            avgLoss += losses[i];
        }
        avgGain /= period;
        avgLoss /= period;

        var rs  = avgLoss == 0 ? 100m : avgGain / avgLoss;
        var rsi = 100m - (100m / (1m + rs));
        series.Add(new TimestampedValue { Time = bars[period].OpenTime, Value = Math.Round(rsi, 2) });

        // Wilder's smoothing
        for (int i = period + 1; i < bars.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
            rs  = avgLoss == 0 ? 100m : avgGain / avgLoss;
            rsi = 100m - (100m / (1m + rs));
            series.Add(new TimestampedValue { Time = bars[i].OpenTime, Value = Math.Round(rsi, 2) });
        }

        return new IndicatorResult
        {
            Symbol    = bars.FirstOrDefault()?.Symbol ?? "",
            Indicator = "RSI",
            Interval  = bars.FirstOrDefault()?.Interval ?? "1d",
            Period    = period,
            Timestamp = series.LastOrDefault()?.Time ?? DateTime.UtcNow,
            Value     = series.LastOrDefault()?.Value ?? 0,
            Series    = series,
        };
    }

    /// <summary>
    /// MACD（Moving Average Convergence Divergence）。
    /// 預設：fast=12, slow=26, signal=9。
    /// </summary>
    public static IndicatorResult MACD(List<OhlcvBar> bars, int fast = 12, int slow = 26, int signal = 9)
    {
        if (bars.Count < slow + signal) return EmptyResult(bars, "MACD", slow);

        // 計算 fast EMA 和 slow EMA
        var fastEma = CalcEmaValues(bars, fast);
        var slowEma = CalcEmaValues(bars, slow);

        // MACD line = fast EMA - slow EMA（從 slow-1 開始有值）
        var macdLine = new List<(DateTime Time, decimal Value)>();
        int startIdx = slow - 1;
        for (int i = startIdx; i < bars.Count; i++)
        {
            var fIdx = i - (fast - 1);
            var sIdx = i - (slow - 1);
            if (fIdx >= 0 && fIdx < fastEma.Count && sIdx >= 0 && sIdx < slowEma.Count)
                macdLine.Add((bars[i].OpenTime, fastEma[fIdx] - slowEma[sIdx]));
        }

        // Signal line = EMA of MACD line
        var series = new List<TimestampedValue>();
        decimal macdLatest = 0, signalLatest = 0, histogramLatest = 0;

        if (macdLine.Count >= signal)
        {
            var signalEma = CalcEmaFromValues(macdLine.Select(m => m.Value).ToList(), signal);
            int signalStart = signal - 1;

            for (int i = signalStart; i < macdLine.Count && (i - signalStart) < signalEma.Count; i++)
            {
                var m = macdLine[i].Value;
                var s = signalEma[i - signalStart];
                series.Add(new TimestampedValue { Time = macdLine[i].Time, Value = Math.Round(m, 4) });
            }

            if (macdLine.Count > 0 && signalEma.Count > 0)
            {
                macdLatest      = macdLine[^1].Value;
                signalLatest    = signalEma[^1];
                histogramLatest = macdLatest - signalLatest;
            }
        }

        return new IndicatorResult
        {
            Symbol    = bars.FirstOrDefault()?.Symbol ?? "",
            Indicator = "MACD",
            Interval  = bars.FirstOrDefault()?.Interval ?? "1d",
            Period    = slow,
            Timestamp = series.LastOrDefault()?.Time ?? DateTime.UtcNow,
            Value     = Math.Round(macdLatest, 4),
            Signal    = Math.Round(signalLatest, 4),
            Histogram = Math.Round(histogramLatest, 4),
            Series    = series,
        };
    }

    // ── 內部輔助 ─────────────────────────────────────────────────────

    private static List<decimal> CalcEmaValues(List<OhlcvBar> bars, int period)
    {
        var result = new List<decimal>();
        if (bars.Count < period) return result;

        var sum = 0m;
        for (int i = 0; i < period; i++) sum += bars[i].Close;
        var ema = sum / period;
        result.Add(ema);

        var mult = 2m / (period + 1);
        for (int i = period; i < bars.Count; i++)
        {
            ema = (bars[i].Close - ema) * mult + ema;
            result.Add(ema);
        }
        return result;
    }

    private static List<decimal> CalcEmaFromValues(List<decimal> values, int period)
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

    private static IndicatorResult EmptyResult(List<OhlcvBar> bars, string name, int period) => new()
    {
        Symbol    = bars.FirstOrDefault()?.Symbol ?? "",
        Indicator = name,
        Interval  = bars.FirstOrDefault()?.Interval ?? "1d",
        Period    = period,
        Timestamp = DateTime.UtcNow,
        Value     = 0,
        Series    = new(),
    };
}
