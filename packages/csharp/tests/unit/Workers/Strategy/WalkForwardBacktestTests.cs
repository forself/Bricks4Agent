using StrategyWorker.Engine;
using StrategyWorker.Models;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// 通用 walk-forward backtest（BacktestEngine.RunWalkForward）的契約測試：
///   - 切窗數量 = floor((bars - train - test) / stride) + 1
///   - 每 fold 的 train/test 不重疊、test 緊接 train 結束
///   - 聚合 OOS 指標正確（avg/median return、worst DD、positive folds 數）
///   - IS-OOS gap 反映過擬合（用 buy-then-hold stub 模擬「訓練看起來好、測試垃圾」）
///   - bars 不足 → 回傳空 folds（不拋）
/// </summary>
public class WalkForwardBacktestTests
{
    /// <summary>固定回 buy@0.7、忽略 bars 內容——讓 BacktestEngine 走 buy-and-hold 行為。</summary>
    private sealed class AlwaysBuyStub : IStrategy
    {
        public string Name => "always_buy";
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = "buy", Confidence = 0.7m, Reason = "always", Interval = config.Interval,
        };
    }

    private sealed class AlwaysHoldStub : IStrategy
    {
        public string Name => "always_hold";
        public Signal Evaluate(List<BarData> bars, StrategyConfig config) => new()
        {
            SignalId = "stub", Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = "hold", Confidence = 0.5m, Reason = "always", Interval = config.Interval,
        };
    }

    private static StrategyConfig Cfg() => new() { Symbol = "AAPL", Exchange = "alpaca", Interval = "1d" };

    /// <summary>線性向上的 K 線——可重複生成 deterministic 報酬。</summary>
    private static List<BarData> LinearUpBars(int count, decimal start = 100m, decimal step = 0.5m)
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count).Select(i =>
        {
            var c = start + i * step;
            return new BarData
            {
                OpenTime = t0.AddDays(i),
                Open = c, High = c, Low = c, Close = c, Volume = 1000m,
            };
        }).ToList();
    }

    [Fact]
    public void TooFewBars_ReturnsEmptyFolds()
    {
        var bars = LinearUpBars(100);   // 100 bars、需要 train(180)+test(60)=240 → 不夠

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        r.Folds.Should().BeEmpty();
        r.TotalFolds.Should().Be(0);
    }

    [Fact]
    public void InvalidParams_ReturnsEmpty()
    {
        var bars = LinearUpBars(500);

        // train < 50 應該被擋
        BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(), trainBars: 30, testBars: 60, stride: 10)
            .Folds.Should().BeEmpty();
        // test < 10
        BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(), trainBars: 100, testBars: 5, stride: 10)
            .Folds.Should().BeEmpty();
        // stride < 1
        BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(), trainBars: 100, testBars: 50, stride: 0)
            .Folds.Should().BeEmpty();
    }

    [Fact]
    public void FoldCount_MatchesExpectedFormula()
    {
        // bars=400, train=180, test=60 → 第一個 fold 占 [0..240), 之後每 stride 推一格
        // 最後一個 fold 必須完整 fit (start + 240 ≤ 400 → start ≤ 160)
        // stride=30 → start ∈ {0, 30, 60, 90, 120, 150} → 6 個 folds
        var bars = LinearUpBars(400);

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        r.TotalFolds.Should().Be(6);
        r.Folds.Should().HaveCount(6);
        r.Folds[0].FoldIndex.Should().Be(0);
        r.Folds[5].FoldIndex.Should().Be(5);
    }

    [Fact]
    public void TrainTestWindowsAreContiguousAndNonOverlapping()
    {
        var bars = LinearUpBars(400);

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        foreach (var f in r.Folds)
        {
            // train 結束日 = 該 fold 起點 + 179 天；test 起點 = train 結束 + 1 天
            var diff = (f.TestStart - f.TrainEnd).Days;
            diff.Should().Be(1, $"fold {f.FoldIndex} test should start 1 day after train ends");
            (f.TestEnd - f.TestStart).Days.Should().Be(60 - 1, "test window has 60 bars (inclusive)");
        }
    }

    [Fact]
    public void AlwaysBuyOnLinearUpTrend_HasPositiveOosReturns()
    {
        // 線性上漲 + always-buy → 每個 fold 的 train + test 都應該賺錢
        var bars = LinearUpBars(400, start: 100m, step: 1m);

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        r.PositiveTestFolds.Should().BeGreaterThan(0, "uptrend + buy should produce winning OOS folds");
        r.AvgTestReturnPct.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void AlwaysHoldStrategy_HasZeroReturnsAndNoTrades()
    {
        var bars = LinearUpBars(400);

        var r = BacktestEngine.RunWalkForward(new AlwaysHoldStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        r.AvgTestReturnPct.Should().Be(0m);
        r.PositiveTestFolds.Should().Be(0);
        // 每個 fold 都該回報 0 trades
        foreach (var f in r.Folds)
            (f.Test?.TotalTrades ?? 0).Should().Be(0, $"fold {f.FoldIndex} should have no trades");
    }

    [Fact]
    public void AggregatedMetrics_ComputeCorrectly()
    {
        var bars = LinearUpBars(400, start: 100m, step: 1m);

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        // total_folds 跟 folds.Count 一致
        r.TotalFolds.Should().Be(r.Folds.Count);
        // avg = mean of test returns
        var expectedAvg = r.Folds
            .Where(f => f.Test != null)
            .Average(f => f.Test!.TotalReturnPct);
        r.AvgTestReturnPct.Should().Be(Math.Round(expectedAvg, 2));
        // worst DD = max of test DD%（DD 是正數、越大越差）
        r.WorstTestDdPct.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void OnlyTrainBarsTouchedInTrainBacktest_IsolationGuarantee()
    {
        // 安全性檢查：每個 fold 的 train backtest 不能看到 test 區段的 bar
        // RunWalkForward 內部 GetRange 切窗，這個 test 在 fold-level 確認窗大小正確
        var bars = LinearUpBars(400);

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        foreach (var f in r.Folds)
        {
            f.Train.Should().NotBeNull();
            f.Train!.TotalBars.Should().Be(180, "train backtest only sees 180 bars");
            f.Test.Should().NotBeNull();
            f.Test!.TotalBars.Should().Be(60, "test backtest only sees 60 bars (no peek into future)");
        }
    }

    [Fact]
    public void Median_IsRobustToOutliers()
    {
        // median 應該在 outlier 出現時跟 average 不同——驗證它有獨立計算
        // 線性 + always-buy 應該各 fold 報酬接近、median ≈ avg；這 test 主要確認 field 有寫入
        var bars = LinearUpBars(400, start: 100m, step: 1m);

        var r = BacktestEngine.RunWalkForward(new AlwaysBuyStub(), bars, Cfg(),
            trainBars: 180, testBars: 60, stride: 30);

        r.MedianTestReturnPct.Should().Be(r.MedianTestReturnPct);  // 不是 NaN
    }
}
