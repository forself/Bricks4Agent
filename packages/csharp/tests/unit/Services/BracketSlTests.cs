using Broker.Services;
using FluentAssertions;

namespace Unit.Tests.Services;

/// <summary>
/// 鎖住 C — AutoTrader bracket SL 價格計算契約（exchange-side stop_loss、broker downtime 保護）。
///
/// 公式：
///   long  → entry × (1 − slPct/100)  SL 在進場價下方
///   short → entry × (1 + slPct/100)  SL 在進場價上方
///
/// 為什麼這條 test 重要：算錯方向 = SL 放在錯誤一側、不但沒保護、market order 一進場就觸發
/// 立刻平倉（long SL 放上方會秒平）。real money、方向絕對不能錯。
/// </summary>
public class BracketSlTests
{
    [Fact]
    public void Long_SlBelowEntry()
    {
        // entry 100, sl 5% → long SL = 95
        var sl = AutoTraderService.ComputeBracketSlPrice(100m, 5m, isLong: true);
        sl.Should().Be(95m);
    }

    [Fact]
    public void Short_SlAboveEntry()
    {
        // entry 100, sl 5% → short SL = 105
        var sl = AutoTraderService.ComputeBracketSlPrice(100m, 5m, isLong: false);
        sl.Should().Be(105m);
    }

    [Fact]
    public void Long_SlAlwaysLowerThanEntry()
    {
        var entry = 2.4434m;  // XRP-ish
        var sl = AutoTraderService.ComputeBracketSlPrice(entry, 5m, isLong: true);
        sl.Should().NotBeNull();
        sl!.Value.Should().BeLessThan(entry, "long SL 一定在進場價下方、否則秒平");
    }

    [Fact]
    public void Short_SlAlwaysHigherThanEntry()
    {
        var entry = 2.4434m;
        var sl = AutoTraderService.ComputeBracketSlPrice(entry, 5m, isLong: false);
        sl.Should().NotBeNull();
        sl!.Value.Should().BeGreaterThan(entry, "short SL 一定在進場價上方、否則秒平");
    }

    [Fact]
    public void ZeroEntry_ReturnsNull()
    {
        AutoTraderService.ComputeBracketSlPrice(0m, 5m, isLong: true).Should().BeNull();
    }

    [Fact]
    public void ZeroSlPct_ReturnsNull()
    {
        // slPct=0 = 不該帶 SL（無意義的 0 距離）
        AutoTraderService.ComputeBracketSlPrice(100m, 0m, isLong: true).Should().BeNull();
    }

    [Fact]
    public void NegativeEntry_ReturnsNull()
    {
        AutoTraderService.ComputeBracketSlPrice(-50m, 5m, isLong: false).Should().BeNull();
    }

    [Fact]
    public void RoundsTo6Decimals()
    {
        // entry 0.123456789, sl 5% long → 0.123456789 × 0.95 = 0.11728... round 6
        var sl = AutoTraderService.ComputeBracketSlPrice(0.123456789m, 5m, isLong: true);
        sl.Should().NotBeNull();
        // 確認小數位 ≤ 6（避免 BingX 拒絕過長精度）
        var decimals = BitConverter.GetBytes(decimal.GetBits(sl!.Value)[3])[2];
        decimals.Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void TightSl_OnePct()
    {
        // 1% SL（高槓桿小本金可能想用更緊）
        AutoTraderService.ComputeBracketSlPrice(100m, 1m, isLong: true).Should().Be(99m);
        AutoTraderService.ComputeBracketSlPrice(100m, 1m, isLong: false).Should().Be(101m);
    }
}
