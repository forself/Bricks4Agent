using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// AutoSelectStrategy 把 RegimeDetector 偵測出的 regime 當 key、查 regimeMap、
/// 委派給對應的 IStrategy。測試重點是「對的 regime 路由到對的成員」+ reason/indicators
/// 帶上 regime 資訊讓下游可看。
/// </summary>
public class AutoSelectStrategyTests
{
    /// <summary>追蹤被叫到幾次、回傳什麼訊號。</summary>
    private sealed class SpyStrategy : IStrategy
    {
        public string Name { get; }
        public int CallCount { get; private set; }
        private readonly string _action;
        private readonly decimal _confidence;
        public SpyStrategy(string name, string action = "buy", decimal confidence = 0.7m)
        { Name = name; _action = action; _confidence = confidence; }
        public Signal Evaluate(List<BarData> bars, StrategyConfig config)
        {
            CallCount++;
            return new Signal
            {
                SignalId = "spy", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
                Action = _action, Confidence = _confidence, Reason = $"spy:{Name}", Interval = config.Interval,
            };
        }
    }

    private static StrategyConfig Cfg() => new() { Symbol = "AAPL", Exchange = "alpaca", Interval = "1d" };

    private static List<BarData> StrongUptrendBars()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, 70).Select(i => new BarData
        {
            OpenTime = t0.AddDays(i), Close = 100m + i * 0.5m,
            Open = 100m + i * 0.5m, High = 100m + i * 0.5m, Low = 100m + i * 0.5m, Volume = 1000m,
        }).ToList();
    }

    [Fact]
    public void TrendingUp_RoutesToVegasTunnelSpy()
    {
        var trendSpy = new SpyStrategy("vegas_tunnel");
        var rangeSpy = new SpyStrategy("rsi_oversold");
        var fallback = new SpyStrategy("composite");
        var auto = new AutoSelectStrategy(
            new Dictionary<RegimeDetector.RegimeType, IStrategy>
            {
                [RegimeDetector.RegimeType.TrendingUp] = trendSpy,
                [RegimeDetector.RegimeType.RangeBound] = rangeSpy,
            },
            fallback);

        var sig = auto.Evaluate(StrongUptrendBars(), Cfg());

        trendSpy.CallCount.Should().Be(1);
        rangeSpy.CallCount.Should().Be(0);
        fallback.CallCount.Should().Be(0);
        sig.Strategy.Should().Be("auto_select");
        sig.Reason.Should().Contain("regime:TrendingUp").And.Contain("→ vegas_tunnel");
        sig.Indicators.Should().ContainKey("regime.type");
        sig.Indicators["regime.type"].Should().Be((decimal)(int)RegimeDetector.RegimeType.TrendingUp);
        sig.Indicators.Should().ContainKey("regime.sma50_slope");
    }

    [Fact]
    public void RegimeMissingFromMap_FallsBackToFallback()
    {
        // map 裡沒有 TrendingUp 對照 → 走 fallback
        var fallback = new SpyStrategy("composite");
        var auto = new AutoSelectStrategy(
            new Dictionary<RegimeDetector.RegimeType, IStrategy>
            {
                // 故意不放 TrendingUp
                [RegimeDetector.RegimeType.RangeBound] = new SpyStrategy("rsi_oversold"),
            },
            fallback);

        var sig = auto.Evaluate(StrongUptrendBars(), Cfg());

        fallback.CallCount.Should().Be(1);
        sig.Reason.Should().Contain("→ composite");
    }

    [Fact]
    public void DefaultFrom_BuildsMappingFromRegistered()
    {
        var registered = new Dictionary<string, IStrategy>
        {
            ["composite"]       = new SpyStrategy("composite"),
            ["vegas_tunnel"]    = new SpyStrategy("vegas_tunnel"),
            ["sma_cross"]       = new SpyStrategy("sma_cross"),
            ["rsi_oversold"]    = new SpyStrategy("rsi_oversold"),
            ["bollinger_bands"] = new SpyStrategy("bollinger_bands"),
            ["multi_timeframe"] = new SpyStrategy("multi_timeframe"),
        };
        var auto = AutoSelectStrategy.DefaultFrom(registered);

        var sig = auto.Evaluate(StrongUptrendBars(), Cfg());

        // 上升趨勢 → vegas_tunnel
        sig.Reason.Should().Contain("vegas_tunnel");
        ((SpyStrategy)registered["vegas_tunnel"]).CallCount.Should().Be(1);
    }

    [Fact]
    public void DefaultFrom_MissingMember_FallsBackToComposite()
    {
        // 刻意不註冊 vegas_tunnel → TrendingUp 應該 fallback 到 composite
        var registered = new Dictionary<string, IStrategy>
        {
            ["composite"] = new SpyStrategy("composite"),
        };
        var auto = AutoSelectStrategy.DefaultFrom(registered);

        var sig = auto.Evaluate(StrongUptrendBars(), Cfg());

        sig.Reason.Should().Contain("→ composite");
        ((SpyStrategy)registered["composite"]).CallCount.Should().Be(1);
    }

    [Fact]
    public void DefaultFrom_NoComposite_Throws()
    {
        var registered = new Dictionary<string, IStrategy>
        {
            ["sma_cross"] = new SpyStrategy("sma_cross"),
        };

        var act = () => AutoSelectStrategy.DefaultFrom(registered);

        act.Should().Throw<ArgumentException>("composite is required as fallback");
    }

    [Fact]
    public void EmptyRegimeMap_Throws()
    {
        var act = () => new AutoSelectStrategy(
            new Dictionary<RegimeDetector.RegimeType, IStrategy>(),
            new SpyStrategy("composite"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NullFallback_Throws()
    {
        var act = () => new AutoSelectStrategy(
            new Dictionary<RegimeDetector.RegimeType, IStrategy>
            {
                [RegimeDetector.RegimeType.RangeBound] = new SpyStrategy("rsi_oversold"),
            },
            null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
