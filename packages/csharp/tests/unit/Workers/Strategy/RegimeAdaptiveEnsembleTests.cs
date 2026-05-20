using StrategyWorker.Engine;
using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// Regime-Adaptive Ensemble 合約測試。
/// 用 stub 成員把「加權投票」的數學鎖死（不依賴真實策略的訊號），
/// 再用真實策略做 DefaultFrom 整合 + 截斷決定性檢查。
/// </summary>
public class RegimeAdaptiveEnsembleTests
{
    private sealed class Stub : IStrategy
    {
        private readonly string _name, _action;
        private readonly decimal _conf;
        public Stub(string name, string action, decimal conf) { _name = name; _action = action; _conf = conf; }
        public string Name => _name;
        public Signal Evaluate(List<BarData> bars, StrategyConfig c) => new()
        {
            SignalId = "x", Strategy = _name, Symbol = c.Symbol, Exchange = c.Exchange,
            Action = _action, Confidence = _conf, Interval = c.Interval,
        };
    }

    // 所有 regime 都映到同一組 → 偵測到哪個 regime 都不影響、專測加權投票
    private static RegimeAdaptiveEnsembleStrategy AllRegimes(params (IStrategy, decimal)[] combo)
    {
        var list = combo.ToList();
        var dict = new Dictionary<RegimeDetector.RegimeType, List<(IStrategy, decimal)>>();
        foreach (RegimeDetector.RegimeType t in Enum.GetValues(typeof(RegimeDetector.RegimeType)))
            dict[t] = list;
        return new RegimeAdaptiveEnsembleStrategy(dict, list);
    }

    private static List<BarData> Bars(int n = 60, double drift = 0.001)
    {
        var bars = new List<BarData>(n);
        double c = 100;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            c *= 1 + drift + 0.004 * Math.Sin(i / 3.0);
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i),
                Open = (decimal)c, High = (decimal)(c * 1.005), Low = (decimal)(c * 0.995),
                Close = (decimal)c, Volume = 1_000_000,
            });
        }
        return bars;
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1h" };

    [Fact]
    public void AllBuyMembers_VotesBuy_AtMemberConfidence()
    {
        var s = AllRegimes((new Stub("a", "buy", 0.8m), 1m), (new Stub("b", "buy", 0.8m), 1m));
        var sig = s.Evaluate(Bars(), Cfg());
        sig.Action.Should().Be("buy");
        sig.Confidence.Should().Be(0.8m, "同向票加權平均信心 = 0.8");
    }

    [Fact]
    public void EqualBuySell_Ties_ToHold()
    {
        var s = AllRegimes((new Stub("a", "buy", 0.7m), 1m), (new Stub("b", "sell", 0.7m), 1m));
        s.Evaluate(Bars(), Cfg()).Action.Should().Be("hold", "buy/sell 等權持平 → 棄權");
    }

    [Fact]
    public void HeavierBuyWeight_WinsOverSell()
    {
        var s = AllRegimes((new Stub("a", "buy", 0.6m), 2m), (new Stub("b", "sell", 0.9m), 1m));
        // buyScore = 0.6 * (2/3) = 0.4; sellScore = 0.9 * (1/3) = 0.3 → buy
        s.Evaluate(Bars(), Cfg()).Action.Should().Be("buy");
    }

    [Fact]
    public void AllHold_AbstainsToHold()
    {
        var s = AllRegimes((new Stub("a", "hold", 0m), 1m), (new Stub("b", "hold", 0m), 1m));
        s.Evaluate(Bars(), Cfg()).Action.Should().Be("hold");
    }

    [Fact]
    public void InsufficientBars_ReturnsHold()
    {
        var s = AllRegimes((new Stub("a", "buy", 0.9m), 1m));
        var bars = Bars(30);   // < MinBars 50
        var sig = s.Evaluate(bars, Cfg());
        sig.Action.Should().Be("hold");
        sig.Confidence.Should().Be(0m);
    }

    [Fact]
    public void Signal_CarriesRegimeIndicators()
    {
        var s = AllRegimes((new Stub("a", "buy", 0.8m), 1m));
        var sig = s.Evaluate(Bars(), Cfg());
        sig.Indicators.Should().ContainKey("regime.type");
        sig.Indicators.Should().ContainKey("combo_size");
        sig.Strategy.Should().Be("regime_adaptive");
    }

    [Fact]
    public void DefaultFrom_BuildsAndEvaluatesValidSignal()
    {
        var reg = new Dictionary<string, IStrategy>
        {
            ["sma_cross"]    = new SmaCrossStrategy(),
            ["rsi_oversold"] = new RsiStrategy(),
            ["composite"]    = CompositeStrategy.Default(),
        };
        var s = RegimeAdaptiveEnsembleStrategy.DefaultFrom(reg);

        var sig = s.Evaluate(Bars(80), Cfg());
        sig.Action.Should().BeOneOf("buy", "sell", "hold");
        sig.Indicators.Should().ContainKey("regime.type");
    }

    [Fact]
    public void DefaultFrom_Deterministic_NoLookahead()
    {
        var reg = new Dictionary<string, IStrategy>
        {
            ["sma_cross"]    = new SmaCrossStrategy(),
            ["rsi_oversold"] = new RsiStrategy(),
            ["composite"]    = CompositeStrategy.Default(),
        };
        var s = RegimeAdaptiveEnsembleStrategy.DefaultFrom(reg);
        var full = Bars(120);

        foreach (var t in new[] { 60, 90, 119 })
        {
            var a = s.Evaluate(full.Take(t + 1).ToList(), Cfg()).Action;
            var b = s.Evaluate(full.Take(t + 1).ToList(), Cfg()).Action;
            a.Should().Be(b, $"@{t} 同輸入應同輸出（決定性）");
        }
    }
}
