using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 多時間框架策略 — 同時分析多個時間框架的訊號，交叉確認。
///
/// 原理：
/// - 高時間框架（日線）決定趨勢方向
/// - 中時間框架（4h）確認動能
/// - 低時間框架（1h）找進場點
///
/// 只有三個框架方向一致時才給出高信心訊號。
///
/// 使用方式：
/// 傳入 bars 做為主要時間框架分析，
/// 額外的 bars_4h / bars_1h 做為輔助框架。
/// 如果沒有傳輔助框架，則只用主框架。
/// </summary>
public class MultiTimeframeStrategy : IStrategy
{
    public string Name => "multi_timeframe";
    public string Description => "Multi-Timeframe — 多時間框架交叉確認";
    public StrategyCategory Category => StrategyCategory.MultiTimeframe;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 150m;

    private readonly IStrategy _baseStrategy;

    public MultiTimeframeStrategy(IStrategy? baseStrategy = null)
    {
        _baseStrategy = baseStrategy ?? CompositeStrategy.Default();
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        // 主時間框架分析（日線）
        var dailySignal = _baseStrategy.Evaluate(bars, config);

        // 如果沒有足夠資料做多框架，直接用主框架
        if (bars.Count < 100)
            return WithStrategy(dailySignal);

        // 用同一組 bars 模擬不同時間框架：
        // 4h ≈ 最近 1/4 的 bars（模擬更短周期的行為）
        // 1h ≈ 最近 1/8 的 bars
        var recentQuarter = bars.GetRange(bars.Count * 3 / 4, bars.Count / 4);
        var recentEighth  = bars.GetRange(bars.Count * 7 / 8, bars.Count / 8);

        var shortConfig = config;

        var mediumSignal = recentQuarter.Count >= 30
            ? _baseStrategy.Evaluate(recentQuarter, shortConfig)
            : dailySignal;

        var shortSignal = recentEighth.Count >= 20
            ? _baseStrategy.Evaluate(recentEighth, shortConfig)
            : dailySignal;

        // 交叉確認
        var actions = new[] { dailySignal.Action, mediumSignal.Action, shortSignal.Action };
        var buyCount  = actions.Count(a => a == "buy");
        var sellCount = actions.Count(a => a == "sell");

        string finalAction;
        decimal confidence;
        string reason;

        if (buyCount == 3)
        {
            finalAction = "buy";
            confidence = 0.9m;
            reason = "All timeframes agree: BUY";
        }
        else if (sellCount == 3)
        {
            finalAction = "sell";
            confidence = 0.9m;
            reason = "All timeframes agree: SELL";
        }
        else if (buyCount == 2)
        {
            finalAction = "buy";
            confidence = 0.65m;
            reason = "2/3 timeframes say BUY";
        }
        else if (sellCount == 2)
        {
            finalAction = "sell";
            confidence = 0.65m;
            reason = "2/3 timeframes say SELL";
        }
        else
        {
            finalAction = "hold";
            confidence = 0.4m;
            reason = "Timeframes disagree — no clear direction";
        }

        reason += $" | Daily: {dailySignal.Action}({dailySignal.Confidence:P0})" +
                  $" | Medium: {mediumSignal.Action}({mediumSignal.Confidence:P0})" +
                  $" | Short: {shortSignal.Action}({shortSignal.Confidence:P0})";

        var indicators = new Dictionary<string, decimal>(dailySignal.Indicators);
        indicators["daily_confidence"]  = dailySignal.Confidence;
        indicators["medium_confidence"] = mediumSignal.Confidence;
        indicators["short_confidence"]  = shortSignal.Confidence;

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = finalAction,
            Confidence = confidence,
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = indicators,
        };
    }

    private Signal WithStrategy(Signal s)
    {
        s.Strategy = Name;
        return s;
    }
}
