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

    /// <summary>70 根線性趨勢:step>0 = 上升(SMA 斜率向上)、step&lt;0 = 下降。</summary>
    private static List<BarData> TrendBars(decimal step, decimal start = 100m)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(70);
        for (int i = 0; i < 70; i++)
        {
            var c = start + i * step;
            bars.Add(new BarData { OpenTime = t0.AddDays(i), Open = c, High = c, Low = c, Close = c, Volume = 1000m });
        }
        return bars;
    }

    [Fact]
    public void Buy_RisingTrend_PassesThroughWithTpSl()
    {
        // SMA50 斜率向上 → 放行 buy、保留 inner TP/SL
        var s = new TrendGatedStrategy(new FixedStub("buy"), "stub_trend", trendPeriod: 50, slopeLookback: 10);
        var r = s.Evaluate(TrendBars(step: 1m), Cfg());
        r.Action.Should().Be("buy");
        r.TargetPrice.Should().Be(999m);
        r.StopPrice.Should().Be(111m);
    }

    [Fact]
    public void Buy_FallingTrend_DowngradedToHold()
    {
        // SMA50 斜率向下 → 擋多、降級 hold(跌勢不接刀)
        var s = new TrendGatedStrategy(new FixedStub("buy"), "stub_trend", trendPeriod: 50, slopeLookback: 10);
        var r = s.Evaluate(TrendBars(step: -1m), Cfg());
        r.Action.Should().Be("hold");
        r.Reason.Should().Contain("趨勢過濾擋多");
    }

    [Fact]
    public void Sell_AlwaysPassesThrough_EvenInDowntrend()
    {
        // 出場不受過濾:即使跌勢也要能平倉
        var s = new TrendGatedStrategy(new FixedStub("sell"), "stub_trend", trendPeriod: 50, slopeLookback: 10);
        var r = s.Evaluate(TrendBars(step: -1m), Cfg());
        r.Action.Should().Be("sell");
    }

    [Fact]
    public void MinBars_AccountsForTrendAndSlope()
    {
        var s = new TrendGatedStrategy(new FixedStub("buy"), "stub_trend", trendPeriod: 50, slopeLookback: 10);
        s.MinBars.Should().BeGreaterThanOrEqualTo(61);
    }
}
