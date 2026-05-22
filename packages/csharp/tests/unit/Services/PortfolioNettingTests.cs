using Broker.Services;

namespace Unit.Tests.Services;

/// <summary>
/// 組合淨倉純邏輯:buy(達信心)→該 sub 多、sell→歸零、hold/buy 未達信心→維持上次;
/// 目標權重 = 多的 sub / 總 sub。
/// </summary>
public class PortfolioNettingTests
{
    private static Dictionary<string, (string, decimal)> Sig(params (string sub, string act, decimal conf)[] xs)
        => xs.ToDictionary(x => x.sub, x => (x.act, x.conf));

    [Fact]
    public void BothBuy_TargetFull()
    {
        var (states, w) = PortfolioNetting.Step(
            new Dictionary<string, bool>(),
            Sig(("rsi_stoch", "buy", 0.7m), ("mfi", "buy", 0.7m)));
        w.Should().Be(1m);
        states["rsi_stoch"].Should().BeTrue();
        states["mfi"].Should().BeTrue();
    }

    [Fact]
    public void OneBuyOneFlat_TargetHalf()
    {
        var (_, w) = PortfolioNetting.Step(
            new Dictionary<string, bool>(),
            Sig(("rsi_stoch", "buy", 0.7m), ("mfi", "hold", 0m)));
        w.Should().Be(0.5m);
    }

    [Fact]
    public void Sell_ZeroesThatSub()
    {
        // rsi 上次多、這次 sell → 歸零;mfi 維持多 → 目標 0.5
        var (states, w) = PortfolioNetting.Step(
            new Dictionary<string, bool> { ["rsi_stoch"] = true, ["mfi"] = true },
            Sig(("rsi_stoch", "sell", 0m), ("mfi", "hold", 0m)));
        states["rsi_stoch"].Should().BeFalse();
        states["mfi"].Should().BeTrue();
        w.Should().Be(0.5m);
    }

    [Fact]
    public void Hold_MaintainsPrior()
    {
        var (states, w) = PortfolioNetting.Step(
            new Dictionary<string, bool> { ["rsi_stoch"] = true, ["mfi"] = false },
            Sig(("rsi_stoch", "hold", 0m), ("mfi", "hold", 0m)));
        states["rsi_stoch"].Should().BeTrue();
        states["mfi"].Should().BeFalse();
        w.Should().Be(0.5m);
    }

    [Fact]
    public void BuyBelowConfidence_DoesNotEnter()
    {
        var (states, w) = PortfolioNetting.Step(
            new Dictionary<string, bool>(),
            Sig(("rsi_stoch", "buy", 0.4m), ("mfi", "buy", 0.4m)));   // 都未達 0.6
        w.Should().Be(0m);
        states.Values.Should().OnlyContain(v => v == false);
    }

    [Fact]
    public void BuyBelowConfidence_ButAlreadyLong_StaysLong()
    {
        var (states, _) = PortfolioNetting.Step(
            new Dictionary<string, bool> { ["rsi_stoch"] = true },
            Sig(("rsi_stoch", "buy", 0.4m)));
        states["rsi_stoch"].Should().BeTrue();   // 本來多、buy 未達信心 → 續抱
    }
}
