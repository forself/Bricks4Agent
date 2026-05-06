using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 鎖住 hold-不稀釋 投票邏輯：
///   - 一票 buy + 兩票 hold → BUY（不再被 hold 0.33 蓋過）
///   - buy/sell 票數相同 → HOLD（不出單）
///   - 全員 hold → HOLD @ 0.3（明確的中性訊號，不是某策略的 0.5）
///   - confidence = 同向票的平均信心，而非 score / totalWeight
/// </summary>
public class CompositeStrategyTests
{
    /// <summary>把 action+confidence 包成 IStrategy stub，省去算 K 線指標。</summary>
    private sealed class StubStrategy : IStrategy
    {
        private readonly string _name;
        private readonly string _action;
        private readonly decimal _confidence;
        public StubStrategy(string name, string action, decimal confidence)
        { _name = name; _action = action; _confidence = confidence; }

        public string Name => _name;
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = _name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = _action, Confidence = _confidence, Reason = $"stub:{_action}",
            Interval = config.Interval,
        };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "AAPL", Exchange = "alpaca", Interval = "1d" };
    private static List<BarData> Bars() => new() { new BarData { Close = 100m } };

    private static CompositeStrategy Build(params (string action, decimal conf)[] votes)
    {
        var list = votes.Select((v, i) =>
            ((IStrategy)new StubStrategy($"s{i}", v.action, v.conf), 1.0m)).ToList();
        return new CompositeStrategy(list);
    }

    [Fact]
    public void OneBuy_TwoHold_ReturnsBuy_NotDilutedByHold()
    {
        // 重點：原本邏輯下 buyScore = 0.7/3 = 0.23、holdScore = (0.5+0.5)/3 = 0.33 → hold 贏
        // 修好後：hold 棄權、buyScore = 0.7、buyWeight = 1 → BUY @ 0.7
        var composite = Build(("buy", 0.7m), ("hold", 0.5m), ("hold", 0.5m));

        var signal = composite.Evaluate(Bars(), Cfg());

        signal.Action.Should().Be("buy", "hold votes shouldn't drown out a single decisive buy");
        signal.Confidence.Should().Be(0.7m);
    }

    [Fact]
    public void TwoBuy_OneSell_ReturnsBuy_AverageOfBuyConfidences()
    {
        var composite = Build(("buy", 0.6m), ("buy", 0.7m), ("sell", 0.5m));

        var signal = composite.Evaluate(Bars(), Cfg());

        signal.Action.Should().Be("buy", "two buy votes outscore one sell");
        signal.Confidence.Should().Be(0.65m, "should be (0.6+0.7)/2, not (0.6+0.7)/3");
    }

    [Fact]
    public void OneBuy_OneSell_OneHold_ReturnsHold_NoTieBreaker()
    {
        // 1 buy@0.7 vs 1 sell@0.7 → 持平 → 不出單
        var composite = Build(("buy", 0.7m), ("sell", 0.7m), ("hold", 0.5m));

        var signal = composite.Evaluate(Bars(), Cfg());

        signal.Action.Should().Be("hold", "tied buy/sell scores should not pick a side");
        signal.Confidence.Should().Be(0.3m);
    }

    [Fact]
    public void StrongerSell_BeatsWeakerBuy_EvenWithFewerVotes()
    {
        // 1 buy@0.5 vs 1 sell@0.9 → sellScore=0.9 > buyScore=0.5 → sell 贏
        var composite = Build(("buy", 0.5m), ("sell", 0.9m), ("hold", 0.5m));

        var signal = composite.Evaluate(Bars(), Cfg());

        signal.Action.Should().Be("sell");
        signal.Confidence.Should().Be(0.9m);
    }

    [Fact]
    public void AllHold_ReturnsHoldWithLowConfidence()
    {
        var composite = Build(("hold", 0.5m), ("hold", 0.5m), ("hold", 0.5m));

        var signal = composite.Evaluate(Bars(), Cfg());

        signal.Action.Should().Be("hold");
        signal.Confidence.Should().Be(0.3m, "all-neutral should have explicit low confidence, not 0.5");
    }

    [Fact]
    public void WeightedVotes_HighWeightBuyOverridesLowWeightSell()
    {
        // 顯式不等權：buy 權重 2、sell 權重 1
        var composite = new CompositeStrategy(new List<(IStrategy, decimal)>
        {
            (new StubStrategy("b", "buy", 0.6m), 2.0m),
            (new StubStrategy("s", "sell", 0.7m), 1.0m),
        });

        var signal = composite.Evaluate(Bars(), Cfg());

        // buyScore = 0.6 * 2 = 1.2、sellScore = 0.7 * 1 = 0.7 → BUY
        // 平均信心 = 1.2 / 2 (buyWeight) = 0.6
        signal.Action.Should().Be("buy");
        signal.Confidence.Should().Be(0.6m);
    }

    [Fact]
    public void IndicatorsAreNamespacedAndPreserved()
    {
        // 確認 indicators 還是會合併（避免 hold-fix 把 indicators 一起砍掉）
        var stubWithInd = new StubWithIndicators("rsi_oversold", "buy", 0.7m,
            new() { ["rsi"] = 28m, ["price"] = 150m });
        var composite = new CompositeStrategy(new List<(IStrategy, decimal)>
        {
            (stubWithInd, 1.0m),
            (new StubStrategy("h", "hold", 0.5m), 1.0m),
        });

        var signal = composite.Evaluate(Bars(), Cfg());

        signal.Indicators.Should().ContainKey("rsi_oversold.rsi");
        signal.Indicators["rsi_oversold.rsi"].Should().Be(28m);
        signal.Indicators["rsi_oversold.price"].Should().Be(150m);
    }

    /// <summary>StubStrategy 的變體，附帶 indicators 給 IndicatorsAreNamespacedAndPreserved 用。</summary>
    private sealed class StubWithIndicators : IStrategy
    {
        private readonly string _name, _action;
        private readonly decimal _confidence;
        private readonly Dictionary<string, decimal> _indicators;
        public StubWithIndicators(string name, string action, decimal conf, Dictionary<string, decimal> ind)
        { _name = name; _action = action; _confidence = conf; _indicators = ind; }
        public string Name => _name;
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = _name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = _action, Confidence = _confidence, Reason = $"stub:{_action}",
            Interval = config.Interval, Indicators = _indicators,
        };
    }
}
