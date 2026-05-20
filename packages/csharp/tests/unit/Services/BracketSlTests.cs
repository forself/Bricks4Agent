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

    // ── LeverageAwareSlPct：高槓桿時 SL 必須先於強平觸發、否則形同虛設 ──

    [Fact]
    public void LeverageAware_LowLeverage_KeepsConfigured()
    {
        // 10x：強平距離 ≈ 10%、cap = 6%。設定 5% < 6% → 不收緊、用設定值。
        AutoTraderService.LeverageAwareSlPct(5m, 10m).Should().Be(5m);
    }

    [Fact]
    public void LeverageAware_HighLeverage_TightensToInsideLiqDistance()
    {
        // 20x：強平距離 ≈ 5%、cap = 3%。設定 5% > 3% → 收緊到 3%（先於強平觸發）。
        AutoTraderService.LeverageAwareSlPct(5m, 20m).Should().Be(3m);
    }

    [Fact]
    public void LeverageAware_VeryHighLeverage_TightensHard()
    {
        // 125x：強平距離 ≈ 0.8%、cap = 0.48%。
        AutoTraderService.LeverageAwareSlPct(5m, 125m).Should().Be(0.48m);
    }

    [Fact]
    public void LeverageAware_NeverWidensBeyondConfigured()
    {
        // 設定值是上限：低槓桿不會放寬（2x cap=30% 但設定 5% → 仍 5%）。
        AutoTraderService.LeverageAwareSlPct(5m, 2m).Should().Be(5m);
    }

    [Fact]
    public void LeverageAware_NoLeverage_NoTightening()
    {
        // leverage ≤ 1（現貨/無槓桿）→ 原樣回設定值。
        AutoTraderService.LeverageAwareSlPct(5m, 1m).Should().Be(5m);
        AutoTraderService.LeverageAwareSlPct(5m, 0m).Should().Be(5m);
    }

    [Fact]
    public void LeverageAware_ComposesWithBracketPrice_20x()
    {
        // 端到端：20x long、entry 100 → SL pct 收緊成 3% → SL 價 = 97（不是 95）。
        var slPct = AutoTraderService.LeverageAwareSlPct(5m, 20m);
        var sl = AutoTraderService.ComputeBracketSlPrice(100m, slPct, isLong: true);
        sl.Should().Be(97m);
    }

    // ── ComputeBracketTpPrice：TP 方向跟 SL 相反 ──

    [Fact]
    public void Tp_Long_AboveEntry()
    {
        // entry 100, tp 10% → long TP = 110（進場價上方）
        AutoTraderService.ComputeBracketTpPrice(100m, 10m, isLong: true).Should().Be(110m);
    }

    [Fact]
    public void Tp_Short_BelowEntry()
    {
        // entry 100, tp 10% → short TP = 90（進場價下方）
        AutoTraderService.ComputeBracketTpPrice(100m, 10m, isLong: false).Should().Be(90m);
    }

    [Fact]
    public void Tp_ZeroPct_ReturnsNull()
    {
        AutoTraderService.ComputeBracketTpPrice(100m, 0m, isLong: true).Should().BeNull();
    }

    [Fact]
    public void Tp_ZeroEntry_ReturnsNull()
    {
        AutoTraderService.ComputeBracketTpPrice(0m, 10m, isLong: true).Should().BeNull();
    }

    [Fact]
    public void Tp_RoundsTo6Decimals()
    {
        var tp = AutoTraderService.ComputeBracketTpPrice(0.123456789m, 10m, isLong: true);
        tp.Should().NotBeNull();
        var decimals = BitConverter.GetBytes(decimal.GetBits(tp!.Value)[3])[2];
        decimals.Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void Tp_Short_NeverGoesNegative()
    {
        // tpPct ≥ 100 的 short 會把價格打到 0 或負 → 回 null（不送無效 TP）
        AutoTraderService.ComputeBracketTpPrice(100m, 100m, isLong: false).Should().BeNull();
    }
}
