using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 鎖住 ensemble 策略的核心契約：
///   - hold 視為棄權、不再稀釋 buy/sell（與 CompositeStrategy fix 同步）
///   - confidence = 同向票權重總和的平均信心
///   - 全員 hold → hold @ 0.3
///   - buy/sell 持平 → hold（不出單）
///   - indicators 帶 weight.* / vote.* / agreement_ratio 給下游判斷分歧
/// 用 stub strategies 跳過真實指標計算；BacktestEngine 對 stub 算 Sharpe 會 fallback
/// 到等權，所以 weights 對所有 stub 一樣，可以單純驗投票邏輯。
/// </summary>
public class WeightedEnsembleStrategyTests
{
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

    // 100 根「完全平盤」K 線 — 刻意讓 BacktestEngine 對所有 stub 都算出 Sharpe 0
    // （無價差→無 PnL→Sharpe 0 或 NaN→clamp 到 0），totalWeight=0 觸發等權 fallback
    // （ensemble 的設計：全員虧錢時退回等權，不讓 ensemble 完全靜音）。
    // 這樣 weights 對所有 stub 都是 1m，可以單純驗投票邏輯不被 Sharpe 偏置干擾。
    private static List<BarData> MakeBars(int count = 100)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count).Select(i => new BarData
        {
            OpenTime = t0.AddDays(i),
            Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1000m,
        }).ToList();
    }

    [Fact]
    public void OneBuy_TwoHold_ReturnsBuy_NotDilutedByHold()
    {
        // ensemble fallback to equal weights when Sharpe = 0 → 每個 stub 拿 1/3 weight
        // buyScore = 0.7 * (1/3) ≈ 0.233、buyWeight = 1/3
        // → confidence = 0.233 / (1/3) = 0.7 ✓
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "buy",  0.7m),
            new StubStrategy("s2", "hold", 0.5m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        signal.Action.Should().Be("buy");
        signal.Confidence.Should().Be(0.7m);
    }

    [Fact]
    public void AllHold_ReturnsHold_With_0_3_Confidence()
    {
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "hold", 0.5m),
            new StubStrategy("s2", "hold", 0.5m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        signal.Action.Should().Be("hold");
        signal.Confidence.Should().Be(0.3m, "all-neutral should give explicit low-conf hold sentinel");
    }

    [Fact]
    public void BuySellTied_ReturnsHold_NoTieBreaker()
    {
        // 用 echo stub：每個成員回的 action 跟內容都一樣 → BacktestEngine 對它們算出同樣
        // 的 Sharpe → totalWeight 同樣比例 → 確保 buy 跟 sell 拿到相同的 wNorm。
        // 用兩對 mirror（buy×2, sell×2）讓持平更可靠。
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("a", "buy",  0.7m),
            new StubStrategy("b", "buy",  0.7m),
            new StubStrategy("c", "sell", 0.7m),
            new StubStrategy("d", "sell", 0.7m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        // 平盤 K 線：所有 stub 跑出 Sharpe = 0 → fallback 到等權；2 buy@0.7 vs 2 sell@0.7 → 同分 → hold
        signal.Action.Should().Be("hold", "tied buy/sell scores should not pick a side");
        signal.Confidence.Should().Be(0.3m);
    }

    [Fact]
    public void StrongerSell_BeatsWeakerBuy()
    {
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "buy",  0.5m),
            new StubStrategy("s2", "sell", 0.9m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        signal.Action.Should().Be("sell");
        signal.Confidence.Should().Be(0.9m);
    }

    [Fact]
    public void IndicatorsIncludeWeightVoteAndAgreementRatio()
    {
        var ensemble = new WeightedEnsembleStrategy(new List<IStrategy>
        {
            new StubStrategy("s1", "buy", 0.7m),
            new StubStrategy("s2", "buy", 0.6m),
            new StubStrategy("s3", "hold", 0.5m),
        });

        var signal = ensemble.Evaluate(MakeBars(), Cfg());

        // 每個 constituent 都有 weight.X 跟 vote.X
        signal.Indicators.Should().ContainKey("weight.s1");
        signal.Indicators.Should().ContainKey("weight.s2");
        signal.Indicators.Should().ContainKey("weight.s3");
        signal.Indicators["vote.s1"].Should().Be(1m, "buy 投票顯示為 +1");
        signal.Indicators["vote.s2"].Should().Be(1m);
        signal.Indicators["vote.s3"].Should().Be(0m, "hold 投票顯示為 0");

        // agreement_ratio：3 個成員裡有 2 個跟最終 action(buy) 同向 → 2/3
        signal.Indicators.Should().ContainKey("agreement_ratio");
        signal.Indicators["agreement_ratio"].Should().BeApproximately(0.6667m, 0.001m);

        // buy_score / sell_score 仍會輸出（hold_score 已移除）
        signal.Indicators.Should().ContainKey("buy_score");
        signal.Indicators.Should().ContainKey("sell_score");
        signal.Indicators.Should().NotContainKey("hold_score");
    }

    [Fact]
    public void ThrowsOnEmptyConstituents()
    {
        var act = () => new WeightedEnsembleStrategy(new List<IStrategy>());
        act.Should().Throw<ArgumentException>();
    }
}
