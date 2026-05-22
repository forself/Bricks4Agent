using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 組合層回測(RunPortfolio):N 條各 1/N 資金、權益相加。
///   - 單一成員 ≈ 直接 Run(同一條)。
///   - [buy, hold] 報酬 ≈ 0.5 × buy-only(hold 那半不動)。
/// </summary>
public class BacktestPortfolioTests
{
    private sealed class AlwaysBuy : IStrategy
    {
        public string Name => "ab";
        public Signal Evaluate(List<BarData> bars, StrategyConfig c) => new()
        { SignalId = "x", Strategy = Name, Symbol = c.Symbol, Exchange = c.Exchange,
          Action = "buy", Confidence = 0.7m, Interval = c.Interval };
    }
    private sealed class AlwaysHold : IStrategy
    {
        public string Name => "ah";
        public Signal Evaluate(List<BarData> bars, StrategyConfig c) => new()
        { SignalId = "x", Strategy = Name, Symbol = c.Symbol, Exchange = c.Exchange,
          Action = "hold", Confidence = 0m, Interval = c.Interval };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };
    private static List<BarData> LinearUp(int n = 200)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, n).Select(i =>
        {
            var c = 100m + i * 0.5m;
            return new BarData { OpenTime = t0.AddDays(i), Open = c, High = c, Low = c, Close = c, Volume = 1000m };
        }).ToList();
    }

    [Fact]
    public void SingleMember_MatchesPlainRun()
    {
        var bars = LinearUp();
        var solo = BacktestEngine.Run(new AlwaysBuy(), bars, Cfg(), 1000m);
        var port = BacktestEngine.RunPortfolio(new List<IStrategy> { new AlwaysBuy() }, bars, Cfg(), 1000m);
        port.TotalReturnPct.Should().BeApproximately(solo.TotalReturnPct, 0.5m);
    }

    [Fact]
    public void BuyPlusHold_IsAboutHalfOfBuyOnly()
    {
        var bars = LinearUp();
        var buyOnly = BacktestEngine.Run(new AlwaysBuy(), bars, Cfg(), 1000m);
        var port = BacktestEngine.RunPortfolio(
            new List<IStrategy> { new AlwaysBuy(), new AlwaysHold() }, bars, Cfg(), 1000m);
        // hold 那半不動 → 組合報酬約為 buy-only 的一半
        port.TotalReturnPct.Should().BeApproximately(buyOnly.TotalReturnPct / 2m, buyOnly.TotalReturnPct * 0.15m + 0.5m);
        port.TotalReturnPct.Should().BeLessThan(buyOnly.TotalReturnPct);
    }

    [Fact]
    public void PortfolioWalkForward_ProducesFolds()
    {
        var bars = LinearUp(700);
        var r = BacktestEngine.RunPortfolioWalkForward(
            new List<IStrategy> { new AlwaysBuy(), new AlwaysHold() }, bars, Cfg(),
            trainBars: 365, testBars: 90, stride: 90);
        r.TotalFolds.Should().BeGreaterThan(0);
        r.Folds.Should().OnlyContain(f => f.Test != null);
    }
}
