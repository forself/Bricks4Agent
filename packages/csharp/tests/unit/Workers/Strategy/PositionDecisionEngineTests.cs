using FluentAssertions;
using StrategyWorker.Engine;
using Xunit;
using static StrategyWorker.Engine.PositionDecisionEngine;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// PositionDecisionEngine(ADD/HOLD/TRIM/EXIT 持倉決策)單元測試。
/// 驗核心決策表 + 鐵則(虧損不加碼)+ 做空反向 + 修正升降階 + 信心/目標價契約。
/// </summary>
public class PositionDecisionEngineTests
{
    private static Input Pos(decimal cost, decimal current, string sig = "neutral",
        decimal score = 0m, string risk = "medium", string side = "long")
        => new()
        {
            Symbol = "X", CostBasis = cost, Quantity = 1m, CurrentPrice = current,
            TechnicalSignal = sig, TechnicalScore = score, RiskLevel = risk, Side = side,
        };

    [Fact]
    public void BigProfit_StrongSignal_Holds()
    {
        var r = Decide(Pos(100m, 125m, "bullish", 0.5m)); // +25%, strong
        r.PnlPct.Should().Be(25m);
        r.SignalStrength.Should().Be("strong");
        r.Decision.Should().Be(Decision.Hold);
    }

    [Fact]
    public void BigLoss_WeakSignal_Exits()
    {
        var r = Decide(Pos(100m, 80m, "bearish", -0.5m)); // -20%, weak
        r.Decision.Should().Be(Decision.Exit);
    }

    [Fact]
    public void Loss_EvenWithStrongSignal_NeverAddsDown()
    {
        // -3%(浮損)+ 強多訊號:鐵則「虧損不加碼」→ 不可 ADD
        var r = Decide(Pos(100m, 97m, "bullish", 0.5m));
        r.Decision.Should().NotBe(Decision.Add);
        r.Decision.Should().Be(Decision.Hold);
    }

    [Fact]
    public void Short_PriceDrop_IsProfit()
    {
        var r = Decide(Pos(100m, 80m, side: "short")); // 做空、價跌 = 賺
        r.PnlPct.Should().Be(20m);
        r.Pnl.Should().Be(20m);
    }

    [Fact]
    public void StrongMtfConsensus_StepsDecisionUp()
    {
        // base = +4% neutral → HOLD;三時框一致偏多(modifier +2)→ 升階為 ADD
        var input = new Input
        {
            Symbol = "X", CostBasis = 100m, Quantity = 1m, CurrentPrice = 104m,
            TechnicalSignal = "neutral", RiskLevel = "medium",
            MtfBullish = 3, MtfBearish = 0, MtfTotal = 3,
        };
        var r = Decide(input);
        r.BaseDecision.Should().Be(Decision.Hold);
        r.ModifierTotal.Should().Be(2);
        r.Decision.Should().Be(Decision.Add);
    }

    [Fact]
    public void Confidence_AlwaysWithin0To100()
    {
        var r = Decide(Pos(100m, 250m, "bullish", 0.9m, risk: "high")); // 極端值也要夾在範圍內
        r.Confidence.Should().BeInRange(0, 100);
    }

    [Fact]
    public void LongTargets_StopLossBelowCost_TakeProfitAbove()
    {
        var r = Decide(Pos(100m, 110m)); // long、無 ATR → SL=cost*0.92、TP=current*1.05
        r.StopLoss.Should().BeLessThan(100m);
        r.TakeProfit1.Should().BeGreaterThan(110m);
    }
}
