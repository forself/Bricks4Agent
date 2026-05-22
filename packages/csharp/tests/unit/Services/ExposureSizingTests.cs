using Broker.Services;

namespace Unit.Tests.Services;

/// <summary>
/// 全倉曝險比例 sizing 純算法(ComputeExposureSizing)——真錢下單量的邊界:
/// notional = exposurePct% × 帳戶總資金,夾在組合曝險預算與保證金硬上限之內。
/// </summary>
public class ExposureSizingTests
{
    // 預設:balance 1000、mark 100、無已開倉、exposure 100%、無組合上限、5x
    private static AutoTraderService.ExposureSizingResult Run(
        decimal balance = 1000m, decimal mark = 100m, decimal existing = 0m,
        decimal exposurePct = 100m, decimal maxPortfolioPct = 0m, decimal lev = 5m)
        => AutoTraderService.ComputeExposureSizing(balance, mark, existing, exposurePct, maxPortfolioPct, lev);

    [Fact]
    public void Basic_NotionalEqualsExposurePctOfBalance()
    {
        // exposure 100% × 1000 = notional 1000 → qty = 1000/100 = 10
        var r = Run();
        r.Applicable.Should().BeTrue();
        r.BudgetExhausted.Should().BeFalse();
        r.MarginClamped.Should().BeFalse();
        r.AllowedNotional.Should().Be(1000m);
        r.Qty.Should().Be(10m);
    }

    [Fact]
    public void ExposurePct_ScalesNotional()
    {
        // exposure 50% × 1000 = 500 → qty 5
        Run(exposurePct: 50m).Qty.Should().Be(5m);
        // exposure 200% × 1000 = 2000、5x marginCap = 1000×5×0.95 = 4750 → 不夾、qty 20
        var r = Run(exposurePct: 200m);
        r.AllowedNotional.Should().Be(2000m);
        r.MarginClamped.Should().BeFalse();
        r.Qty.Should().Be(20m);
    }

    [Fact]
    public void PortfolioBudget_CapsAgainstExistingNotional()
    {
        // 組合上限 200% × 1000 = 2000;已開倉 1600 → 剩 400;per-trade 100%×1000=1000 → 取 min = 400
        var r = Run(existing: 1600m, exposurePct: 100m, maxPortfolioPct: 200m);
        r.BudgetExhausted.Should().BeFalse();
        r.AllowedNotional.Should().Be(400m);
        r.Qty.Should().Be(4m);
        r.PortfolioMaxNotional.Should().Be(2000m);
    }

    [Fact]
    public void PortfolioBudget_Exhausted_WhenExistingMeetsMax()
    {
        // 已開倉 2000 ≥ 組合上限 2000 → 預算用完、skip
        var r = Run(existing: 2000m, exposurePct: 100m, maxPortfolioPct: 200m);
        r.Applicable.Should().BeTrue();
        r.BudgetExhausted.Should().BeTrue();
        r.Qty.Should().Be(0m);
    }

    [Fact]
    public void NoPortfolioCap_AllowsFullPerTrade()
    {
        // maxPortfolioPct=0 → 不限組合;已開倉再多也不影響 per-trade
        var r = Run(existing: 9999m, exposurePct: 100m, maxPortfolioPct: 0m);
        r.BudgetExhausted.Should().BeFalse();
        r.AllowedNotional.Should().Be(1000m);
    }

    [Fact]
    public void MarginCap_ClampsWhenExposureExceedsLeverage()
    {
        // exposure 600% × 1000 = 6000、但 5x marginCap = 1000×5×0.95 = 4750 → clamp 到 4750
        var r = Run(exposurePct: 600m, lev: 5m);
        r.MarginClamped.Should().BeTrue();
        r.AllowedNotional.Should().Be(4750m);
        r.Qty.Should().Be(47.5m);
    }

    [Fact]
    public void MarginCap_UsesAtLeast1x_WhenLeverageZero()
    {
        // leverage 0 → Math.Max(lev,1)=1 → marginCap = 1000×1×0.95 = 950;exposure 100% notional 1000 → clamp 950
        var r = Run(exposurePct: 100m, lev: 0m);
        r.MarginClamped.Should().BeTrue();
        r.AllowedNotional.Should().Be(950m);
    }

    [Fact]
    public void NotApplicable_WhenBalanceOrMarkInvalid()
    {
        Run(balance: 0m).Applicable.Should().BeFalse();
        Run(balance: -10m).Applicable.Should().BeFalse();
        Run(mark: 0m).Applicable.Should().BeFalse();
        Run(mark: -1m).Applicable.Should().BeFalse();
    }

    [Fact]
    public void RealisticLiveConfig_BtcEth_TwoLegBook()
    {
        // live:exposure 100%、組合上限 200%、5x。第一腿(無已開倉)→ notional 100% balance
        var leg1 = Run(balance: 500m, mark: 60000m, existing: 0m, exposurePct: 100m, maxPortfolioPct: 200m, lev: 5m);
        leg1.AllowedNotional.Should().Be(500m);          // 第一腿吃滿單倉額度
        // 第二腿(已開 500)→ 剩 200%×500−500 = 500,per-trade 也 500 → 取 500
        var leg2 = Run(balance: 500m, mark: 3000m, existing: 500m, exposurePct: 100m, maxPortfolioPct: 200m, lev: 5m);
        leg2.AllowedNotional.Should().Be(500m);
        // 第三腿(已開 1000 = 組合上限)→ 用完、skip
        var leg3 = Run(balance: 500m, mark: 3000m, existing: 1000m, exposurePct: 100m, maxPortfolioPct: 200m, lev: 5m);
        leg3.BudgetExhausted.Should().BeTrue();
    }
}
