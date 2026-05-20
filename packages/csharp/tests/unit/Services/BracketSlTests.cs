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

    // ── ResolveBracketTpPct：R:R 模式優先、否則固定 % ──

    [Fact]
    public void TpResolve_RrMode_TpIsRrTimesSl()
    {
        // RR=2、SL 距離 3%（20x 收緊後）→ TP 距離 6%。賺賠比 2:1。
        AutoTraderService.ResolveBracketTpPct(tpRr: 2m, tpPct: 0m, effectiveSlPct: 3m).Should().Be(6m);
    }

    [Fact]
    public void TpResolve_RrMode_ScalesWithLeverageTightenedSl()
    {
        // SL 隨槓桿縮 → TP 自動跟著縮，賺賠比恆定。50x SL=1.2% → RR=2 → TP=2.4%。
        AutoTraderService.ResolveBracketTpPct(tpRr: 2m, tpPct: 99m, effectiveSlPct: 1.2m).Should().Be(2.4m);
    }

    [Fact]
    public void TpResolve_RrTakesPrecedenceOverFixed()
    {
        // 兩個都設 → R:R 贏（更合理）
        AutoTraderService.ResolveBracketTpPct(tpRr: 3m, tpPct: 10m, effectiveSlPct: 2m).Should().Be(6m);
    }

    [Fact]
    public void TpResolve_NoRr_FallsBackToFixed()
    {
        AutoTraderService.ResolveBracketTpPct(tpRr: 0m, tpPct: 10m, effectiveSlPct: 3m).Should().Be(10m);
    }

    [Fact]
    public void TpResolve_NoRrNoFixed_ReturnsZeroOff()
    {
        AutoTraderService.ResolveBracketTpPct(tpRr: 0m, tpPct: 0m, effectiveSlPct: 3m).Should().Be(0m);
    }

    [Fact]
    public void TpResolve_RrButNoSl_FallsBackToFixed()
    {
        // SL 距離無效（0）→ R:R 算不出來、退回固定 %
        AutoTraderService.ResolveBracketTpPct(tpRr: 2m, tpPct: 8m, effectiveSlPct: 0m).Should().Be(8m);
    }

    [Fact]
    public void TpResolve_ComposesWithPrice_20xLong()
    {
        // 端到端：20x long、SL 收緊 3%、RR=2 → TP 6% → entry 100 → TP 價 106
        var slPct = AutoTraderService.LeverageAwareSlPct(5m, 20m);            // 3
        var tpPct = AutoTraderService.ResolveBracketTpPct(2m, 0m, slPct);     // 6
        AutoTraderService.ComputeBracketTpPrice(100m, tpPct, isLong: true).Should().Be(106m);
    }

    // ── RoundPrice：bracket SL/TP 必須對齊 symbol price tick、否則 BingX 拒單 ──

    [Fact]
    public void RoundPrice_XrpTick4_RoundsTo4dp()
    {
        // XRP price precision 4 → 2.370115 必須變 2.3701、否則 BingX 拒
        AutoTraderService.RoundPrice(2.370115m, 4).Should().Be(2.3701m);
    }

    [Fact]
    public void RoundPrice_BtcTick1_RoundsTo1dp()
    {
        // BTC price precision 1 → 92269.989 → 92270.0
        AutoTraderService.RoundPrice(92269.989m, 1).Should().Be(92270.0m);
    }

    [Fact]
    public void RoundPrice_IntegerTick0()
    {
        AutoTraderService.RoundPrice(12345.67m, 0).Should().Be(12346m);
    }

    [Fact]
    public void RoundPrice_NullPrecision_FallsBackTo6dp()
    {
        // 沒 tick 資料（dynamic cache 還沒 refresh）→ 維持原本 6dp 行為
        AutoTraderService.RoundPrice(0.123456789m, null).Should().Be(0.123457m);
    }

    [Fact]
    public void RoundPrice_OutOfRangePrecision_FallsBackTo6dp()
    {
        AutoTraderService.RoundPrice(0.123456789m, 20).Should().Be(0.123457m);
        AutoTraderService.RoundPrice(0.123456789m, -3).Should().Be(0.123457m);
    }

    [Fact]
    public void RoundPrice_ComposesWithBracketSl_Xrp20xShort()
    {
        // 端到端：XRP short 2.4434、20x → SL 3% above = 2.516702 → tick4 → 2.5167
        var slPct = AutoTraderService.LeverageAwareSlPct(5m, 20m);
        var raw = AutoTraderService.ComputeBracketSlPrice(2.4434m, slPct, isLong: false);
        AutoTraderService.RoundPrice(raw!.Value, 4).Should().Be(2.5167m);
    }

    // ── RoundQtyToStep：開倉數量必須對齊 QtyStep、且往下取（不超預算）──

    [Fact]
    public void RoundQty_Step01_FloorsDown()
    {
        AutoTraderService.RoundQtyToStep(3.728m, 0.1m).Should().Be(3.7m);
    }

    [Fact]
    public void RoundQty_Step1_FloorsToInteger()
    {
        AutoTraderService.RoundQtyToStep(3.728m, 1m).Should().Be(3m);
    }

    [Fact]
    public void RoundQty_NeverRoundsUp_StaysWithinBudget()
    {
        // 3.79 step 0.1 → 3.7（不是 3.8）：寧可略小、不超 notional
        AutoTraderService.RoundQtyToStep(3.79m, 0.1m).Should().Be(3.7m);
    }

    [Fact]
    public void RoundQty_SmallStep()
    {
        AutoTraderService.RoundQtyToStep(0.00037m, 0.0001m).Should().Be(0.0003m);
    }

    [Fact]
    public void RoundQty_BelowOneStep_GoesToZero()
    {
        // 小於一個 step → 0，後續 pre-flight MinQty 會擋下
        AutoTraderService.RoundQtyToStep(0.00005m, 0.0001m).Should().Be(0m);
    }

    [Fact]
    public void RoundQty_ZeroOrNegativeStep_ReturnsUnchanged()
    {
        AutoTraderService.RoundQtyToStep(3.728m, 0m).Should().Be(3.728m);
        AutoTraderService.RoundQtyToStep(3.728m, -1m).Should().Be(3.728m);
    }

    [Fact]
    public void RoundQty_AlreadyAligned_Unchanged()
    {
        AutoTraderService.RoundQtyToStep(5m, 1m).Should().Be(5m);
        AutoTraderService.RoundQtyToStep(2.5m, 0.1m).Should().Be(2.5m);
    }
}
