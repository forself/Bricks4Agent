using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 淨加權曝險 ensemble(NetWeightedEnsembleStrategy)測試:用固定訊號 stub 成員,
/// 驗「淨曝險 = Σ wᵢ·dirᵢ」聚合、權重正規化、一致→高信心方向、分歧→hold。
/// </summary>
public class NetWeightedEnsembleTests
{
    private sealed class FixedStrat : IStrategy
    {
        public string Name { get; }
        private readonly string _action; private readonly decimal _conf;
        public FixedStrat(string name, string action, decimal conf) { Name = name; _action = action; _conf = conf; }
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            Action = _action, Confidence = _conf, Strategy = Name, Symbol = config.Symbol, Interval = config.Interval,
        };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "binance", Interval = "1d" };
    private static List<BarData> Bars() => Enumerable.Range(0, 60)
        .Select(i => new BarData { OpenTime = DateTime.UtcNow.AddDays(i), Open = 100, High = 101, Low = 99, Close = 100, Volume = 1 }).ToList();

    [Fact]
    public void AllAgreeLong_NetBuy_HighConfidence()
    {
        var ens = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        {
            (new FixedStrat("a", "buy", 0.8m), 1m), (new FixedStrat("b", "buy", 0.8m), 1m),
            (new FixedStrat("c", "buy", 0.8m), 1m), (new FixedStrat("d", "buy", 0.8m), 1m),
        });
        var sig = ens.Evaluate(Bars(), Cfg());
        sig.Action.Should().Be("buy");
        sig.Confidence.Should().BeGreaterThanOrEqualTo(0.9m, "全員做多 → 淨曝險=1 → 高信心(滿倉)");
        sig.Indicators["net_exposure"].Should().Be(1m);
    }

    [Fact]
    public void AllAgreeShort_NetSell()
    {
        var ens = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        {
            (new FixedStrat("a", "sell", 0.8m), 1m), (new FixedStrat("b", "sell", 0.8m), 1m),
        });
        var sig = ens.Evaluate(Bars(), Cfg());
        sig.Action.Should().Be("sell");
        sig.Indicators["net_exposure"].Should().Be(-1m);
    }

    [Fact]
    public void EvenDisagreement_NetZero_Holds()
    {
        var ens = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        {
            (new FixedStrat("a", "buy", 0.8m), 1m), (new FixedStrat("b", "buy", 0.8m), 1m),
            (new FixedStrat("c", "sell", 0.8m), 1m), (new FixedStrat("d", "sell", 0.8m), 1m),
        });
        var sig = ens.Evaluate(Bars(), Cfg());
        sig.Action.Should().Be("hold", "2 多 2 空等權 → 淨曝險 0 → 觀望");
        sig.Indicators["net_exposure"].Should().Be(0m);
    }

    [Fact]
    public void WeightedMajority_FollowsHeavierSide()
    {
        // 重倉(0.9)做多 vs 輕倉(0.1)做空 → 淨曝險 +0.8 → buy
        var ens = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        {
            (new FixedStrat("heavy", "buy", 0.8m), 0.9m),
            (new FixedStrat("light", "sell", 0.8m), 0.1m),
        });
        var sig = ens.Evaluate(Bars(), Cfg());
        sig.Action.Should().Be("buy");
        sig.Indicators["net_exposure"].Should().BeApproximately(0.8m, 0.001m);
    }

    [Fact]
    public void LowConfidenceConstituent_DoesNotCountToNet()
    {
        // 成員信心 < 0.6 不計入淨曝險(與引擎進場門檻一致)→ 全部低信心 → 淨 0 → hold
        var ens = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        {
            (new FixedStrat("a", "buy", 0.5m), 1m), (new FixedStrat("b", "buy", 0.55m), 1m),
        });
        var sig = ens.Evaluate(Bars(), Cfg());
        sig.Action.Should().Be("hold");
        sig.Indicators["net_exposure"].Should().Be(0m);
    }

    [Fact]
    public void Deterministic()
    {
        var ens = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
        { (new FixedStrat("a", "buy", 0.8m), 0.6m), (new FixedStrat("b", "sell", 0.8m), 0.4m) }, name: "x");
        var a = ens.Evaluate(Bars(), Cfg()); var b = ens.Evaluate(Bars(), Cfg());
        a.Action.Should().Be(b.Action); a.Confidence.Should().Be(b.Confidence);
        ens.Name.Should().Be("x");
    }
}
