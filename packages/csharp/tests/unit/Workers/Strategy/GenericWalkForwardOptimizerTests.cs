using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 通用 walk-forward 參數優化器的機制測試（grid 展開 + 端到端跑得動 + 結構正確）。
/// 「調參能否救 OOS」是 data-dependent、靠線上實資料驗;這裡只鎖死邏輯不回歸。
/// </summary>
public class GenericWalkForwardOptimizerTests
{
    private static List<BarData> Synthetic(int n = 600, int seed = 7)
    {
        var rng = new Random(seed);
        var bars = new List<BarData>(n);
        double c = 100;
        var t0 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            c *= 1 + 0.0004 + 0.02 * (rng.NextDouble() - 0.5);
            var hi = c * (1 + Math.Abs(rng.NextDouble()) * 0.01);
            var lo = c * (1 - Math.Abs(rng.NextDouble()) * 0.01);
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = (decimal)c, High = (decimal)hi,
                Low = (decimal)lo, Close = (decimal)c, Volume = 1_000_000,
            });
        }
        return bars;
    }

    [Fact]
    public void BuildGrid_SuperTrendSchema_ExpandsCartesian()
    {
        var grid = GenericWalkForwardOptimizer.BuildGrid(new SuperTrendStrategy().ParamSchema);
        // atr_period 7..21 step1 = 15;multiplier 2..5 step0.5 = 7 → 105
        grid.Should().HaveCount(105);
        grid.Should().Contain(d => d.ContainsKey("atr_period") && d.ContainsKey("multiplier"));
    }

    [Fact]
    public void BuildGrid_EmptySchema_ReturnsSingleEmptyCombo()
    {
        var grid = GenericWalkForwardOptimizer.BuildGrid(new Dictionary<string, ParamSpec>());
        grid.Should().HaveCount(1);
        grid[0].Should().BeEmpty();
    }

    [Fact]
    public void Optimize_SuperTrend_RunsAndReturnsSaneStructure()
    {
        var bars = Synthetic(600);
        var cfg = new StrategyConfig { Symbol = "BTC-USDT", Exchange = "bingx", Interval = "1d" };

        var r = GenericWalkForwardOptimizer.Optimize(
            new SuperTrendStrategy(), bars, cfg, trainBars: 300, testBars: 90, cash: 1000m);

        r.Error.Should().BeNull();
        r.GridSize.Should().Be(105);
        r.WindowCount.Should().BeGreaterThan(0);
        // 每個 window 都有選出最佳參數
        r.Windows.Should().OnlyContain(w => w.BestParams.ContainsKey("atr_period") && w.BestParams.ContainsKey("multiplier"));
        // 最常見最佳參數有填
        r.MostCommonBestParams.Should().ContainKey("atr_period").And.ContainKey("multiplier");
        // 參數穩定度 + 白話判語
        r.ParamStability.Should().BeInRange(0m, 1m);
        r.Verdict.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Optimize_NoSchema_ReturnsError()
    {
        // 用測試專用的空 schema stub（不綁定某個真實策略是否有 schema → 之後加 schema 也不破）
        var bars = Synthetic(400);
        var cfg = new StrategyConfig { Symbol = "X", Exchange = "bingx", Interval = "1d" };
        var r = GenericWalkForwardOptimizer.Optimize(new NoSchemaStrategy(), bars, cfg, 200, 60);
        r.Error.Should().NotBeNull();
        r.WindowCount.Should().Be(0);
    }

    // 永遠無 ParamSchema(沿用 IStrategy 預設空 schema),專供「無 schema → 回 error」測試用。
    private sealed class NoSchemaStrategy : IStrategy
    {
        public string Name => "_no_schema_test";
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = Name, Symbol = config.Symbol,
            Exchange = config.Exchange, Action = "hold", Confidence = 0, Interval = config.Interval,
        };
    }

    [Fact]
    public void Optimize_NotEnoughBars_ReturnsError()
    {
        var bars = Synthetic(100);
        var cfg = new StrategyConfig { Symbol = "X", Exchange = "bingx", Interval = "1d" };
        var r = GenericWalkForwardOptimizer.Optimize(new SuperTrendStrategy(), bars, cfg, 300, 90);
        r.Error.Should().NotBeNull();
    }

    // ── verdict 邏輯(用實測 4 幣的真實數字鎖死;舊版會把這些誤判)──
    [Theory]
    // BTC 1d:opt 3.78% 遠輸 def 55% → 該講「用預設」(舊版誤判成 marginal:有改善)
    [InlineData(3.78, 55.13, 0.81, "use-default")]
    // ETH/SOL:調參版 OOS 虧錢 → no-edge(SOL 舊版誤判成 robust:調參有效)
    [InlineData(-6.06, 19.58, 0.7, "no-edge")]
    [InlineData(-19.95, -7.36, 0.7, "no-edge")]
    // LINK:opt 75% 真的勝過 def 38% 且參數穩 → robust
    [InlineData(75.09, 38.44, 0.7, "robust")]
    // opt 勝 def 但參數亂跳 → fragile
    [InlineData(10.0, 5.0, 0.3, "fragile")]
    public void ComputeVerdict_ClassifiesByReturnFirst(double opt, double def, double stab, string expectedPrefix)
    {
        var v = GenericWalkForwardOptimizer.ComputeVerdict((decimal)opt, (decimal)def, (decimal)stab);
        v.Should().StartWith(expectedPrefix);
    }
}
