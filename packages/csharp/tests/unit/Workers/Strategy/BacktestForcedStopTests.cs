using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 「有停損 vs 無停損」對照(forcedStopPct):rsi_stoch/mfi 是均值回歸、抱到反向訊號的 edge,
/// signal 不帶 stop。forcedStopPct &gt; 0 時、給沒帶 stop 的進場套一個固定 % 停損,讓同策略能跑「有停損」版比較。
///
/// 場景:進場後價格直直跌。
///   - 無停損(0):一路抱、收盤才平 → 吃滿跌幅。
///   - 有停損(5%):跌破 entry×0.95 就出 → 虧損被截斷、報酬反而較好。
/// 這證明工具本身有效;真正「無停損比較好」要靠真實 rsi_stoch/mfi 的 OOS 跑(回測誠實基準)。
/// </summary>
public class BacktestForcedStopTests
{
    private sealed class AlwaysBuy : IStrategy
    {
        public string Name => "ab";
        public Signal Evaluate(List<BarData> bars, StrategyConfig c) => new()
        { SignalId = "x", Strategy = Name, Symbol = c.Symbol, Exchange = c.Exchange,
          Action = "buy", Confidence = 0.7m, Interval = c.Interval };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };

    /// <summary>前 flatBars 根平在 100(warmup + 進場)、之後跌到 crashLevel 並維持。</summary>
    private static List<BarData> FlatThenCrash(int flatBars = 60, int crashBars = 40, decimal crashLevel = 80m)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = new List<BarData>();
        for (int i = 0; i < flatBars; i++)
            list.Add(new BarData { OpenTime = t0.AddDays(i), Open = 100m, High = 100m, Low = 100m, Close = 100m, Volume = 1000m });
        for (int i = 0; i < crashBars; i++)
            list.Add(new BarData { OpenTime = t0.AddDays(flatBars + i), Open = crashLevel, High = crashLevel, Low = crashLevel, Close = crashLevel, Volume = 1000m });
        return list;
    }

    [Fact]
    public void ForcedStop_CutsLossVsNoStop_OnCrash()
    {
        var bars = FlatThenCrash();
        var noStop   = BacktestEngine.Run(new AlwaysBuy(), bars, Cfg(), 1000m);
        var withStop = BacktestEngine.Run(new AlwaysBuy(), bars, Cfg(), 1000m, forcedStopPct: 5m);

        // 跌勢中:有停損截斷虧損 → 報酬優於無停損(無停損吃滿 100→80)。
        withStop.TotalReturnPct.Should().BeGreaterThan(noStop.TotalReturnPct);
        // 有停損版應該出現 SL 出場的成交。
        withStop.Trades.Should().Contain(t => t.Side.Contains("SL"));
        // 無停損版沒有 SL 出場(抱到底、auto-close)。
        noStop.Trades.Should().NotContain(t => t.Side.Contains("SL"));
    }

    [Fact]
    public void ForcedStopZero_IsSameAsDefault()
    {
        var bars = FlatThenCrash();
        var def  = BacktestEngine.Run(new AlwaysBuy(), bars, Cfg(), 1000m);
        var zero = BacktestEngine.Run(new AlwaysBuy(), bars, Cfg(), 1000m, forcedStopPct: 0m);
        zero.TotalReturnPct.Should().Be(def.TotalReturnPct);   // 0 = 不強制 = 向後相容
    }
}
