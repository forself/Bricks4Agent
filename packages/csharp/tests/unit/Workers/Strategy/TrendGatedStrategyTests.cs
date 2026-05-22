using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 趨勢過濾裝飾器(risk-off 第一塊):做多只在 close>SMA(trend) 放行、跌勢降級成 hold;
/// 出場/觀望原樣放行;放行時保留 inner 的 TargetPrice/StopPrice。
/// </summary>
public class TrendGatedStrategyTests
{
    /// <summary>固定回傳指定 action 的 stub(帶 TP/SL 驗證是否被保留)。</summary>
    private sealed class FixedStub : IStrategy
    {
        private readonly string _action;
        public FixedStub(string action) { _action = action; }
        public string Name => "stub";
        public int MinBars => 2;
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "x", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = _action, Confidence = 0.7m, Reason = "stub", Interval = config.Interval,
            TargetPrice = 999m, StopPrice = 111m,
        };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };

    /// <summary>60 根:前 59 根固定 basePrice、最後一根 lastClose(控制 vs SMA 的關係)。</summary>
    private static List<BarData> Bars(decimal basePrice, decimal lastClose)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(60);
        for (int i = 0; i < 60; i++)
        {
            var c = i == 59 ? lastClose : basePrice;
            bars.Add(new BarData { OpenTime = t0.AddDays(i), Open = c, High = c, Low = c, Close = c, Volume = 1000m });
        }
        return bars;
    }

    [Fact]
    public void Buy_AboveTrend_PassesThroughWithTpSl()
    {
        // base 100、最後一根 120 → SMA50≈100、price 120 > SMA → 放行
        var s = new TrendGatedStrategy(new FixedStub("buy"), "stub_trend", trendPeriod: 50);
        var r = s.Evaluate(Bars(100m, 120m), Cfg());
        r.Action.Should().Be("buy");
        r.TargetPrice.Should().Be(999m);   // inner 的 TP/SL 要被保留
        r.StopPrice.Should().Be(111m);
    }

    [Fact]
    public void Buy_BelowTrend_DowngradedToHold()
    {
        // base 100、最後一根 80 → price 80 < SMA50≈99.7 → 擋多、降級 hold
        var s = new TrendGatedStrategy(new FixedStub("buy"), "stub_trend", trendPeriod: 50);
        var r = s.Evaluate(Bars(100m, 80m), Cfg());
        r.Action.Should().Be("hold");
        r.Reason.Should().Contain("趨勢過濾擋多");
    }

    [Fact]
    public void Sell_AlwaysPassesThrough_EvenBelowTrend()
    {
        // 出場不受過濾:即使在跌勢也要能平倉
        var s = new TrendGatedStrategy(new FixedStub("sell"), "stub_trend", trendPeriod: 50);
        var r = s.Evaluate(Bars(100m, 80m), Cfg());
        r.Action.Should().Be("sell");
    }

    [Fact]
    public void MinBars_AccountsForTrendPeriod()
    {
        var s = new TrendGatedStrategy(new FixedStub("buy"), "stub_trend", trendPeriod: 50);
        s.MinBars.Should().BeGreaterThanOrEqualTo(51);
    }
}
