using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 回測成本模型(地基):滑價 + 永續資金費都要正確扣到多單 PnL。
///   - 平盤(無價格 PnL)下,加滑價 → 報酬更低。
///   - 平盤 + 正資金費 → 多單付錢 → 報酬更低;負資金費 → 收錢 → 報酬更高。
/// </summary>
public class BacktestCostModelTests
{
    /// <summary>第一次 Evaluate 買、之後 hold → 撐到 auto-close(長期持倉、放大資金費效果)。</summary>
    private sealed class BuyHoldStub : IStrategy
    {
        private bool _bought;
        public string Name => "buyhold";
        public Signal Evaluate(List<BarData> bars, StrategyConfig config)
        {
            var buy = !_bought; _bought = true;
            return new Signal
            {
                SignalId = "x", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
                Action = buy ? "buy" : "hold", Confidence = buy ? 0.7m : 0m,
                Reason = "", Interval = config.Interval,
            };
        }
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };

    /// <summary>120 根平盤(close=100);funding 可選填。</summary>
    private static List<BarData> FlatBars(decimal? funding = null)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(120);
        for (int i = 0; i < 120; i++)
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1000m,
                FundingRate = funding,
            });
        return bars;
    }

    [Fact]
    public void Slippage_ReducesReturn()
    {
        var noSlip = BacktestEngine.Run(new BuyHoldStub(), FlatBars(), Cfg(), 1000m, commission: 0.001m, slippagePct: 0m);
        var slip   = BacktestEngine.Run(new BuyHoldStub(), FlatBars(), Cfg(), 1000m, commission: 0.001m, slippagePct: 0.01m);
        slip.TotalReturnPct.Should().BeLessThan(noSlip.TotalReturnPct);
    }

    [Fact]
    public void PositiveFunding_CostsTheLong()
    {
        var noFund = BacktestEngine.Run(new BuyHoldStub(), FlatBars(0.001m), Cfg(), 1000m, applyFunding: false);
        var fund   = BacktestEngine.Run(new BuyHoldStub(), FlatBars(0.001m), Cfg(), 1000m, applyFunding: true);
        fund.TotalReturnPct.Should().BeLessThan(noFund.TotalReturnPct);   // 多單付正資金費 = 成本
    }

    [Fact]
    public void NegativeFunding_CreditsTheLong()
    {
        var noFund = BacktestEngine.Run(new BuyHoldStub(), FlatBars(-0.001m), Cfg(), 1000m, applyFunding: false);
        var fund   = BacktestEngine.Run(new BuyHoldStub(), FlatBars(-0.001m), Cfg(), 1000m, applyFunding: true);
        fund.TotalReturnPct.Should().BeGreaterThan(noFund.TotalReturnPct);  // 負資金費 = 多單收錢
    }

    [Fact]
    public void NoFundingData_NoEffect_EvenWhenEnabled()
    {
        // bars 無 FundingRate(非 perp)→ 開了 applyFunding 也不該改變結果(優雅降級)
        var off = BacktestEngine.Run(new BuyHoldStub(), FlatBars(null), Cfg(), 1000m, applyFunding: false);
        var on  = BacktestEngine.Run(new BuyHoldStub(), FlatBars(null), Cfg(), 1000m, applyFunding: true);
        on.TotalReturnPct.Should().Be(off.TotalReturnPct);
    }
}
