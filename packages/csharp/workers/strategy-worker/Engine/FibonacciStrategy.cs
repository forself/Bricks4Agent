using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 斐波那契回撤策略（Fibonacci Retracement Strategy）
///
/// 核心邏輯：
///   1. 從最近 N 根 K 線找出擺動高點 H 和低點 L
///   2. 用 SMA 判斷當前趨勢（close > SMA → 上升；&lt; SMA → 下降）
///   3. 上升趨勢中，若價格回落到 0.382–0.618 黃金區 + 有反彈跡象 → 買進
///   4. 下降趨勢中，若價格反彈到黃金區 + 有拒絕跡象 → 賣出
///
/// 這個策略示範了「指標（FibonacciLevels）」與「策略（FibonacciStrategy）」的分層：
///   - FibonacciLevels 是純數學工具，任何策略都能用
///   - FibonacciStrategy 是消費該工具、加上趨勢判斷與進場邏輯的決策層
///
/// 未來想加諧波（Gartley/Butterfly/...）時，同樣模式：新增 HarmonicDetector 指標 +
/// HarmonicPatternStrategy 策略。
/// </summary>
public class FibonacciStrategy : IStrategy
{
    public string Name => "fibonacci_retracement";
    public string Description => "Fibonacci Retracement — 擺動高低點 0.382-0.618 黃金區順勢回撤進場";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 52;                  // SwingLookback=50 + 2
    public decimal MinCapitalUsdt => 200m;     // 黃金區進場、止損偏遠、單筆風險較大

    private const int SwingLookback = 50;   // 從最近 50 根 K 線找擺動
    private const int TrendSmaPeriod = 50;  // 用 SMA-50 判斷趨勢

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        // 進場區上下界(回撤 ratio)。預設 0.382–0.707(用戶 AnthonyLee 修正版:0.707 也算有效進場)。
        ["fib_zone_low"]  = new() { Type = "decimal", Default = 0.382m, Choices = new object[] { 0.236m, 0.382m, 0.5m },           Description = "進場區下界(回撤 ratio)" },
        ["fib_zone_high"] = new() { Type = "decimal", Default = 0.707m, Choices = new object[] { 0.618m, 0.707m, 0.786m, 0.886m }, Description = "進場區上界(0.707 為修正版預設)" },
    };

    /// <summary>出場目標的擴展位:牛(TrendingUp)→2.24、熊(TrendingDown)→1.33、其他→1.618。</summary>
    public static decimal ExitExtensionForRegime(RegimeDetector.RegimeType regime) => regime switch
    {
        RegimeDetector.RegimeType.TrendingUp   => 2.24m,
        RegimeDetector.RegimeType.TrendingDown => 1.33m,
        _ => 1.618m,
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < Math.Max(SwingLookback, TrendSmaPeriod) + 2)
            return HoldSignal(config, "Not enough data (need 52+ bars)");

        var current = bars[^1];
        var prev = bars[^2];
        var price = current.Close;

        // 1. 趨勢判斷
        var sma = CalcSma(bars, TrendSmaPeriod);
        var direction = price > sma ? "up" : "down";
        var trendStrength = sma == 0 ? 0m : Math.Abs((price - sma) / sma);  // 偏離 SMA 的程度

        // 2. 擺動 + Fib 水平
        var (high, _, low, _) = FibonacciLevels.FindSwing(bars, SwingLookback);
        if (high <= low)
            return HoldSignal(config, $"Invalid swing: high={high} low={low}");

        var levels = FibonacciLevels.Levels(high, low, direction);
        var retRatio = FibonacciLevels.RetracementRatio(price, high, low);
        decimal zoneLow  = config.GetParam("fib_zone_low", 0.382m);
        decimal zoneHigh = config.GetParam("fib_zone_high", 0.707m);
        var inZone = retRatio >= zoneLow && retRatio <= zoneHigh;

        // 擴展位停利 + 擺動低點停損（只在多頭進場時鎖定，交給回測引擎 / 真實下單觸發）
        var extensions = FibonacciLevels.ExtensionLevels(high, low, direction);
        decimal? tpPrice = null, slPrice = null;

        // 出場目標依市況自適應(用戶設計):牛市常衝 2.24、熊市常停 1.33、其他取 1.618
        var regime = RegimeDetector.Detect(bars).Type;
        var tpExt = ExitExtensionForRegime(regime);

        // 3. 訊號判斷
        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (inZone)
        {
            var bouncing = current.Close > prev.Close;  // 當前 K 收盤高於前一根 = 反彈
            var rejecting = current.Close < prev.Close;  // 當前 K 收盤低於前一根 = 拒絕

            if (direction == "up" && bouncing)
            {
                action = "buy";
                // 離 0.5 越近、趨勢越強 → confidence 越高
                var zoneFit = 1m - Math.Abs(retRatio - 0.5m) * 4m;   // 在 0.5 時 fit=1，在 0.382/0.618 時 fit=0.47
                confidence = Math.Clamp(0.55m + zoneFit * 0.2m + Math.Min(trendStrength * 5m, 0.2m), 0.5m, 0.95m);
                reason = $"Uptrend + price pullback to Fib {retRatio:P0} zone + bounce (close > prev)";
                // TP = 依市況選的擴展位(牛 2.24 / 熊 1.33 / 其他 1.618);SL = 擺動低點(結構失效點)
                tpPrice = extensions.TryGetValue(tpExt, out var extTp) ? extTp
                        : (extensions.TryGetValue(1.618m, out var ext161) ? ext161 : null);
                slPrice = Math.Round(low, 4);
                reason += $" · TP {tpExt}({regime})";
            }
            else if (direction == "down" && rejecting)
            {
                action = "sell";
                var zoneFit = 1m - Math.Abs(retRatio - 0.5m) * 4m;
                confidence = Math.Clamp(0.55m + zoneFit * 0.2m + Math.Min(trendStrength * 5m, 0.2m), 0.5m, 0.95m);
                reason = $"Downtrend + price rally to Fib {retRatio:P0} zone + rejection (close < prev)";
            }
            else
            {
                reason = $"In Fib golden zone ({retRatio:P0}) but no bounce/rejection yet — waiting confirmation";
            }
        }
        else if (retRatio < zoneLow)
        {
            reason = $"Below entry zone (ratio={retRatio:P0}) — too deep, waiting";
        }
        else
        {
            reason = $"Above golden zone (ratio={retRatio:P0}) — extended, waiting pullback";
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
            TargetPrice = tpPrice,
            StopPrice = slPrice,
            Indicators = new()
            {
                ["price"] = Math.Round(price, 4),
                ["swing_high"] = Math.Round(high, 4),
                ["swing_low"] = Math.Round(low, 4),
                ["fib_382"] = levels.TryGetValue(0.382m, out var f382) ? f382 : 0m,
                ["fib_500"] = levels.TryGetValue(0.500m, out var f500) ? f500 : 0m,
                ["fib_618"] = levels.TryGetValue(0.618m, out var f618) ? f618 : 0m,
                ["retracement_ratio"] = retRatio,
                ["trend_sma50"] = Math.Round(sma, 4),
                ["trend_direction"] = direction == "up" ? 1m : -1m,
            }
        };
    }

    private static decimal CalcSma(List<BarData> bars, int period)
    {
        if (bars.Count < period) return 0m;
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) sum += bars[i].Close;
        return sum / period;
    }

    private static Signal HoldSignal(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "fibonacci_retracement",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
