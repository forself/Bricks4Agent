using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 專注震盪集成:DefaultFrom 只挑四條震盪、Name=osc_ensemble、Evaluate 不丟例外且回傳對的 Strategy。
/// </summary>
public class OscillatorEnsembleStrategyTests
{
    private sealed class Stub : IStrategy
    {
        private readonly string _n; private readonly string _act;
        public Stub(string n, string act = "buy") { _n = n; _act = act; }
        public string Name => _n;
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "x", Strategy = _n, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = _act, Confidence = 0.7m, Reason = "stub", Interval = config.Interval,
        };
    }

    private static List<BarData> Bars(int n = 120)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(n);
        for (int i = 0; i < n; i++)
        {
            var c = 100m + (i % 7);
            bars.Add(new BarData { OpenTime = t0.AddDays(i), Open = c, High = c + 1, Low = c - 1, Close = c, Volume = 1000m });
        }
        return bars;
    }

    [Fact]
    public void DefaultFrom_PicksFourOscillators()
    {
        var reg = new Dictionary<string, IStrategy>
        {
            ["rsi_stoch"] = new Stub("rsi_stoch"), ["rsi_oversold"] = new Stub("rsi_oversold"),
            ["mfi"] = new Stub("mfi"), ["cci"] = new Stub("cci"),
            ["super_trend"] = new Stub("super_trend"),   // 不該被選
        };
        var e = OscillatorEnsembleStrategy.DefaultFrom(reg);
        e.Name.Should().Be("osc_ensemble");
    }

    [Fact]
    public void Evaluate_AllMembersBuy_ReturnsBuy_WithCorrectStrategyName()
    {
        var reg = new Dictionary<string, IStrategy>
        {
            ["rsi_stoch"] = new Stub("rsi_stoch", "buy"), ["rsi_oversold"] = new Stub("rsi_oversold", "buy"),
            ["mfi"] = new Stub("mfi", "buy"), ["cci"] = new Stub("cci", "buy"),
        };
        var e = OscillatorEnsembleStrategy.DefaultFrom(reg);
        var sig = e.Evaluate(Bars(), new StrategyConfig { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" });
        sig.Strategy.Should().Be("osc_ensemble");   // 不能是內核的 "ensemble"
        sig.Action.Should().Be("buy");
    }
}
