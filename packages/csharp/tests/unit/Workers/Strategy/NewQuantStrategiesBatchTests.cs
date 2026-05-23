using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Models;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 2026-05-23 去相關量化批次的 5 支新策略測試:
///   ts_momentum / dual_thrust / tail_reversion / trend_pullback / accel_momentum
///
/// 涵蓋:
///   1. 共通契約 —— 資料不足 hold、輸出合法、buy 信心 ≥ 引擎進場門檻(0.6)、可掃參、決定性。
///   2. No-lookahead / locality —— 砍掉最舊一段歷史(保留足夠 lookback)後,末端訊號與指標
///      必須完全不變(只依賴近 K 根 = 純回看、無未來/全長依賴)。
///   3. 回測整合 —— BacktestEngine.Run / RunWalkForward 能跑出有限且合理的指標與 OOS folds。
///   4. tail_reversion 暴跌路徑 —— 觸發 buy 並帶 defined-risk 的 SL/TP。
///
/// 純合成資料(固定 seed),不打外網、CI 穩定。
/// </summary>
public class NewQuantStrategiesBatchTests
{
    private static IStrategy[] All() => new IStrategy[]
    {
        new TsMomentumStrategy(),
        new ChandelierTrendStrategy(),
        new MaRegimeTrendStrategy(),
        new DualThrustStrategy(),
        new AccelMomentumStrategy(),
    };

    private static StrategyConfig Cfg() => new() { Symbol = "BTC-USDT", Exchange = "binance", Interval = "1d" };

    /// <summary>帶 drift + 雜訊的合成日線(GBM),固定 seed。</summary>
    private static List<BarData> MakeSynthetic(int n = 400, int seed = 42, double drift = 0.0006, double sigma = 0.02)
    {
        var rng = new Random(seed);
        var t0 = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<BarData>(n);
        double close = 100.0;
        for (int i = 0; i < n; i++)
        {
            double ret = drift + sigma * NextGaussian(rng);
            close *= Math.Exp(ret);
            double hi = Math.Abs(NextGaussian(rng)) * 0.008;
            double lo = Math.Abs(NextGaussian(rng)) * 0.008;
            double op = NextGaussian(rng) * 0.005;
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i),
                Open  = (decimal)(close * (1 + op)),
                High  = (decimal)(close * (1 + hi)),
                Low   = (decimal)(close * (1 - lo)),
                Close = (decimal)close,
                Volume = rng.Next(1_000_000, 5_000_000),
            });
        }
        return bars;
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    // ── 1. 共通契約 ──────────────────────────────────────────────

    [Fact]
    public void AllStrategies_TooFewBars_Hold()
    {
        var tiny = MakeSynthetic(20);
        foreach (var s in All())
            s.Evaluate(tiny, Cfg()).Action.Should().Be("hold", $"{s.Name} 資料不足應 hold");
    }

    [Fact]
    public void AllStrategies_ProduceValidSignal()
    {
        var bars = MakeSynthetic(400);
        foreach (var s in All())
        {
            var sig = s.Evaluate(bars, Cfg());
            sig.Action.Should().BeOneOf("buy", "sell", "hold");
            sig.Confidence.Should().BeInRange(0m, 1m, $"{s.Name} 信心須在 [0,1]");
            sig.Strategy.Should().Be(s.Name);
            sig.Symbol.Should().Be("BTC-USDT");
        }
    }

    [Fact]
    public void AllStrategies_BuyConfidence_MeetsEngineThreshold()
    {
        // 掃多組走勢:只要出現 buy,信心就必須 ≥ 0.6(否則回測引擎根本不會進場)。
        bool sawAnyBuy = false;
        for (int seed = 1; seed <= 40; seed++)
        {
            var bars = MakeSynthetic(450, seed);
            foreach (var s in All())
            {
                var sig = s.Evaluate(bars, Cfg());
                if (sig.Action == "buy")
                {
                    sawAnyBuy = true;
                    sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m, $"{s.Name} buy 信心須達引擎門檻");
                }
            }
        }
        sawAnyBuy.Should().BeTrue("掃 40 組走勢至少應出現一個 buy 訊號(否則策略形同空轉)");
    }

    [Fact]
    public void AllStrategies_AreOptimizable()
    {
        new TsMomentumStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "mom_lookback", "mom_vol_window", "mom_entry_z", "mom_vol_ceiling" });
        new ChandelierTrendStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "ch_lookback", "ch_atr_period", "ch_atr_filter" });
        new MaRegimeTrendStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "mar_ma_period", "mar_slope_lookback" });
        new DualThrustStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "dt_lookback", "dt_trend_sma", "dt_k1", "dt_k2" });
        new AccelMomentumStrategy().ParamSchema.Keys
            .Should().Contain(new[] { "accel_roc_period", "accel_gap", "accel_trend_sma" });
    }

    [Fact]
    public void AllStrategies_Deterministic()
    {
        var bars = MakeSynthetic(350);
        foreach (var s in All())
        {
            var a = s.Evaluate(bars, Cfg());
            var b = s.Evaluate(bars, Cfg());
            a.Action.Should().Be(b.Action, $"{s.Name} 同輸入應同輸出");
            a.Confidence.Should().Be(b.Confidence, $"{s.Name} 信心應決定性");
        }
    }

    // ── 2. No-lookahead / locality ───────────────────────────────
    // 砍掉最舊一段歷史(保留遠超 lookback 的根數),末端訊號 + 指標必須完全不變。
    // 純回看 + 視窗化指標才會滿足此不變式;若誤用絕對 index / 全長 / 未來資料就會破。

    [Theory]
    [InlineData(48)]
    [InlineData(96)]
    public void AllStrategies_DropOldHistory_IsInvariant(int dropFront)
    {
        var full = MakeSynthetic(500, 7);
        var trimmed = full.Skip(dropFront).ToList();   // 砍最舊 dropFront 根、末端視窗逐根相同
        foreach (var s in All())
        {
            var a = s.Evaluate(full, Cfg());
            var b = s.Evaluate(trimmed, Cfg());
            b.Action.Should().Be(a.Action, $"{s.Name} 砍舊歷史後末端訊號變了 → 有全長/未來依賴");
            b.Confidence.Should().Be(a.Confidence, $"{s.Name} 砍舊歷史後信心變了 → 非純回看");
            foreach (var kv in a.Indicators)
                b.Indicators[kv.Key].Should().Be(kv.Value, $"{s.Name}.{kv.Key} 截斷不一致 → look-ahead 疑慮");
        }
    }

    // ── 3. 回測整合 ──────────────────────────────────────────────

    [Fact]
    public void AllStrategies_Backtest_ProducesSaneMetrics()
    {
        var bars = MakeSynthetic(700);
        var cfg = Cfg();
        foreach (var s in All())
        {
            var r = BacktestEngine.Run(s, bars, cfg);
            r.EquityCurve.Should().NotBeEmpty($"{s.Name} 應有權益曲線");
            r.TotalReturnPct.Should().BeGreaterThan(-100m, $"{s.Name} long-only 不該虧超過本金");
            r.WinRate.Should().BeInRange(0m, 100m, $"{s.Name} 勝率域");
            r.MaxDrawdownPct.Should().BeInRange(0m, 100m, $"{s.Name} 回撤域");
        }
    }

    [Fact]
    public void AllStrategies_WalkForward_ProducesFolds()
    {
        var bars = MakeSynthetic(850);
        var cfg = Cfg();
        foreach (var s in All())
        {
            var wf = BacktestEngine.RunWalkForward(s, bars, cfg, 250, 90, 60);
            wf.Folds.Count.Should().BeGreaterThan(0, $"{s.Name} 應切出 OOS folds");
            wf.TotalFolds.Should().Be(wf.Folds.Count);
        }
    }

    // ── 4. 新趨勢策略的分支路徑 ──────────────────────────────────

    [Fact]
    public void ChandelierTrend_Breakout_FiresBuy()
    {
        // 緩漲後最後一根創新高 → 突破前 N 根高點 → buy。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal px = 100m;
        for (int i = 0; i < 80; i++)
        {
            px *= 1.004m;   // 穩定緩漲;High=Close(無上影)→ 收盤乾淨突破前高
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = px * 0.999m, High = px, Low = px * 0.99m,
                Close = px, Volume = 1_000_000m,
            });
        }
        var sig = new ChandelierTrendStrategy().Evaluate(bars, Cfg());
        sig.Action.Should().Be("buy", "持續創高應觸發突破進場");
        sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m);
        sig.Indicators.Should().ContainKey("prior_high");
    }

    [Fact]
    public void MaRegimeTrend_Downtrend_GoesShort()
    {
        // 長期下跌 → close < SMA 且均線下彎 → 多空對稱應發出做空(sell, 信心≥0.6)。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal px = 300m;
        for (int i = 0; i < 120; i++)
        {
            px *= 0.99m;   // 每根 −1%
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = px, High = px * 1.005m, Low = px * 0.995m,
                Close = px, Volume = 1_000_000m,
            });
        }
        var sig = new MaRegimeTrendStrategy().Evaluate(bars, Cfg());
        sig.Action.Should().Be("sell", "空頭(跌破下彎均線)應做空");
        sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m, "做空訊號信心須達引擎進場門檻");
    }

    [Fact]
    public void MaRegimeTrend_Uptrend_Buys()
    {
        // 穩定上升 → close > SMA 且均線上彎 → buy。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal px = 100m;
        for (int i = 0; i < 120; i++)
        {
            px *= 1.008m;
            bars.Add(new BarData
            {
                OpenTime = t0.AddDays(i), Open = px * 0.999m, High = px * 1.004m, Low = px * 0.996m,
                Close = px, Volume = 1_000_000m,
            });
        }
        var sig = new MaRegimeTrendStrategy().Evaluate(bars, Cfg());
        sig.Action.Should().Be("buy", "站上上彎均線應做多");
        sig.Confidence.Should().BeGreaterThanOrEqualTo(0.6m);
    }

    // ── 5. 多空(long-short)引擎 ─────────────────────────────────

    [Fact]
    public void LongShort_Run_ProducesShortTrades_AndSaneMetrics()
    {
        // 先漲後跌 → 趨勢策略應先做多、後反手做空 → 交易裡要出現 short。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal px = 100m;
        for (int i = 0; i < 150; i++) { px *= 1.01m; Add(bars, t0, i, px); }   // 漲
        for (int i = 150; i < 320; i++) { px *= 0.99m; Add(bars, t0, i, px); } // 跌

        var r = LongShortBacktestEngine.Run(new MaRegimeTrendStrategy(), bars, Cfg());
        r.EquityCurve.Should().NotBeEmpty();
        r.Trades.Should().Contain(t => t.Side == "short", "先漲後跌應反手做空");
        r.Trades.Should().Contain(t => t.Side == "long", "上升段應做多");
        r.WinRate.Should().BeInRange(0m, 100m);
        r.MaxDrawdownPct.Should().BeInRange(0m, 100m);
    }

    [Fact]
    public void LongShort_ProfitsInDowntrend()
    {
        // 持續下跌:long-only 只能空手(0%),多空版做空應為正報酬(這就是「多空都能跑」的價值)。
        var bars = new List<BarData>();
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal px = 500m;
        for (int i = 0; i < 250; i++) { px *= 0.99m; Add(bars, t0, i, px); }

        var ls = LongShortBacktestEngine.Run(new MaRegimeTrendStrategy(), bars, Cfg());
        ls.TotalReturnPct.Should().BeGreaterThan(0m, "多空版在空頭應靠做空獲利");
    }

    [Fact]
    public void LongShort_WalkForward_ProducesFolds()
    {
        var bars = MakeSynthetic(850);
        foreach (var s in All())
        {
            var wf = LongShortBacktestEngine.RunWalkForward(s, bars, Cfg(), 250, 90, 60);
            wf.Folds.Count.Should().BeGreaterThan(0, $"{s.Name} 多空 OOS 應切出 folds");
        }
    }

    private static void Add(List<BarData> bars, DateTime t0, int i, decimal px) => bars.Add(new BarData
    {
        OpenTime = t0.AddDays(i), Open = px, High = px * 1.005m, Low = px * 0.995m,
        Close = px, Volume = 1_000_000m,
    });
}
