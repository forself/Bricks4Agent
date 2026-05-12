using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 回測引擎 — 用歷史 K 線模擬策略執行。
/// </summary>
public class BacktestEngine
{
    /// <summary>
    /// 回測結果。
    /// </summary>
    public class BacktestResult
    {
        public string Strategy     { get; set; } = "";
        public string Symbol       { get; set; } = "";
        public decimal InitialCash { get; set; }
        public decimal FinalValue  { get; set; }
        public decimal TotalReturn { get; set; }
        public decimal TotalReturnPct { get; set; }
        public int TotalTrades    { get; set; }
        public int WinTrades      { get; set; }
        public int LoseTrades     { get; set; }
        public decimal WinRate    { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal MaxDrawdownPct { get; set; }
        public decimal SharpeRatio { get; set; }
        public decimal AvgWin     { get; set; }
        public decimal AvgLoss    { get; set; }
        public decimal ProfitFactor { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate   { get; set; }
        public int TotalBars      { get; set; }
        public List<BacktestTrade> Trades { get; set; } = new();
        public List<EquityPoint> EquityCurve { get; set; } = new();
    }

    public class BacktestTrade
    {
        public string Side     { get; set; } = "";
        public DateTime EntryDate { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime ExitDate  { get; set; }
        public decimal ExitPrice  { get; set; }
        public decimal Quantity   { get; set; }
        public decimal Pnl       { get; set; }
        public decimal PnlPct    { get; set; }
        public int HoldBars      { get; set; }
    }

    public class EquityPoint
    {
        public DateTime Date  { get; set; }
        public decimal Value  { get; set; }
    }

    /// <summary>
    /// 執行回測。
    ///
    /// htfBars（選用、Batch HTF backtest 新增）：給支援多時間框架的策略
    /// （目前是 HarmonicStrategy）用。每個 step 引擎會以 LTF[i].OpenTime 為界、
    /// 將 htfBars 切到對應位置塞回 config.HtfBars。沒提供就跟既有單時間框架行為一致。
    /// </summary>
    public static BacktestResult Run(
        IStrategy strategy,
        List<BarData> bars,
        StrategyConfig config,
        decimal initialCash = 100_000,
        decimal commission = 0.001m, // 0.1% 手續費
        List<BarData>? htfBars = null)
    {
        var result = new BacktestResult
        {
            Strategy    = strategy.Name,
            Symbol      = config.Symbol,
            InitialCash = initialCash,
            TotalBars   = bars.Count,
            StartDate   = bars.FirstOrDefault()?.OpenTime ?? DateTime.MinValue,
            EndDate     = bars.LastOrDefault()?.OpenTime ?? DateTime.MinValue,
        };

        if (bars.Count < 50) return result;

        decimal cash = initialCash;
        decimal position = 0;        // 持有數量
        decimal entryPrice = 0;
        DateTime entryDate = DateTime.MinValue;
        decimal peakEquity = initialCash;
        decimal maxDrawdown = 0;
        var dailyReturns = new List<decimal>();
        decimal prevEquity = initialCash;

        // 從第 50 根 bar 開始（確保有足夠歷史做指標計算）
        int lookback = Math.Max(config.SmaSlow + 5, 50);

        // HTF 指標：保留原 config.HtfBars，每步覆寫成截至當前 LTF 時間的 HTF 切片、結束後還原
        var originalHtf = config.HtfBars;
        bool useHtf = htfBars != null && htfBars.Count >= 2;
        int htfPtr = 0;   // htfBars 中最後一個 OpenTime ≤ 當前 LTF 時間的 index

        try
        {
        for (int i = lookback; i < bars.Count; i++)
        {
            var windowBars = bars.GetRange(0, i + 1);

            // 把 HTF 切到對應時間（HTF 通常更稀疏、scan 比 binary search 簡單且 monotonic）
            if (useHtf)
            {
                var ltfTime = bars[i].OpenTime;
                while (htfPtr < htfBars!.Count - 1 && htfBars[htfPtr + 1].OpenTime <= ltfTime)
                    htfPtr++;
                // 至少要 2 根才有意義；少於就傳 null（策略會略過 HTF 邏輯）
                config.HtfBars = htfPtr >= 1
                    ? htfBars.GetRange(0, htfPtr + 1)
                    : null;
            }

            var signal = strategy.Evaluate(windowBars, config);
            var currentPrice = bars[i].Close;
            var equity = cash + position * currentPrice;

            // 紀錄 equity curve
            result.EquityCurve.Add(new EquityPoint { Date = bars[i].OpenTime, Value = equity });

            // 紀錄日報酬
            if (prevEquity > 0)
                dailyReturns.Add((equity - prevEquity) / prevEquity);
            prevEquity = equity;

            // 追蹤 max drawdown
            if (equity > peakEquity) peakEquity = equity;
            var drawdown = peakEquity - equity;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;

            // 交易邏輯
            if (signal.Action == "buy" && signal.Confidence >= 0.6m && position == 0)
            {
                // 開多倉 — 用 90% 資金
                var orderValue = cash * 0.9m;
                var fee = orderValue * commission;
                var qty = (orderValue - fee) / currentPrice;
                position = qty;
                entryPrice = currentPrice;
                entryDate = bars[i].OpenTime;
                cash -= orderValue;
            }
            else if (signal.Action == "sell" && position > 0)
            {
                // 平倉
                var sellValue = position * currentPrice;
                var fee = sellValue * commission;
                cash += sellValue - fee;

                var pnl = (currentPrice - entryPrice) * position - (entryPrice * position * commission) - fee;
                var pnlPct = entryPrice > 0 ? (currentPrice - entryPrice) / entryPrice * 100 : 0;

                result.Trades.Add(new BacktestTrade
                {
                    Side = "long", EntryDate = entryDate, EntryPrice = entryPrice,
                    ExitDate = bars[i].OpenTime, ExitPrice = currentPrice,
                    Quantity = position, Pnl = Math.Round(pnl, 2),
                    PnlPct = Math.Round(pnlPct, 2),
                    HoldBars = i - bars.FindIndex(b => b.OpenTime == entryDate),
                });

                position = 0;
                entryPrice = 0;
            }
        }
        }
        finally
        {
            // 還原 caller 傳進來的原始 HtfBars、避免污染外部 config 物件
            config.HtfBars = originalHtf;
        }

        // 如果還有持倉，用最後收盤價平倉
        if (position > 0)
        {
            var lastPrice = bars[^1].Close;
            var sellValue = position * lastPrice;
            var fee = sellValue * commission;
            cash += sellValue - fee;

            var pnl = (lastPrice - entryPrice) * position - (entryPrice * position * commission) - fee;
            result.Trades.Add(new BacktestTrade
            {
                Side = "long (auto-close)", EntryDate = entryDate, EntryPrice = entryPrice,
                ExitDate = bars[^1].OpenTime, ExitPrice = lastPrice,
                Quantity = position, Pnl = Math.Round(pnl, 2),
                PnlPct = entryPrice > 0 ? Math.Round((lastPrice - entryPrice) / entryPrice * 100, 2) : 0,
            });
            position = 0;
        }

        // 統計
        result.FinalValue     = cash;
        result.TotalReturn    = Math.Round(cash - initialCash, 2);
        result.TotalReturnPct = Math.Round((cash - initialCash) / initialCash * 100, 2);
        result.TotalTrades    = result.Trades.Count;
        result.WinTrades      = result.Trades.Count(t => t.Pnl > 0);
        result.LoseTrades     = result.Trades.Count(t => t.Pnl <= 0);
        result.WinRate        = result.TotalTrades > 0 ? Math.Round((decimal)result.WinTrades / result.TotalTrades * 100, 1) : 0;
        result.MaxDrawdown    = Math.Round(maxDrawdown, 2);
        result.MaxDrawdownPct = peakEquity > 0 ? Math.Round(maxDrawdown / peakEquity * 100, 2) : 0;

        var wins = result.Trades.Where(t => t.Pnl > 0).ToList();
        var losses = result.Trades.Where(t => t.Pnl <= 0).ToList();
        result.AvgWin  = wins.Count > 0 ? Math.Round(wins.Average(t => t.Pnl), 2) : 0;
        result.AvgLoss = losses.Count > 0 ? Math.Round(losses.Average(t => t.Pnl), 2) : 0;
        result.ProfitFactor = losses.Sum(t => Math.Abs(t.Pnl)) > 0
            ? Math.Round(wins.Sum(t => t.Pnl) / losses.Sum(t => Math.Abs(t.Pnl)), 2) : 0;

        // Sharpe Ratio (annualized, assuming daily)
        if (dailyReturns.Count > 1)
        {
            var avg = dailyReturns.Average();
            var std = (decimal)Math.Sqrt((double)dailyReturns.Select(r => (r - avg) * (r - avg)).Average());
            result.SharpeRatio = std > 0 ? Math.Round(avg / std * (decimal)Math.Sqrt(252), 2) : 0;
        }

        return result;
    }

    /// <summary>
    /// Walk-forward 回測 fold——記錄一個 train/test 切窗的結果。
    /// </summary>
    public class WalkForwardFold
    {
        public int FoldIndex { get; set; }
        public DateTime TrainStart { get; set; }
        public DateTime TrainEnd   { get; set; }
        public DateTime TestStart  { get; set; }
        public DateTime TestEnd    { get; set; }
        public BacktestResult? Train { get; set; }  // 訓練窗（in-sample）回測結果
        public BacktestResult? Test  { get; set; }  // 測試窗（out-of-sample）回測結果
    }

    /// <summary>
    /// Walk-forward 整體結果——所有 fold + 聚合 OOS 指標。
    /// </summary>
    public class WalkForwardResult
    {
        public string Strategy   { get; set; } = "";
        public string Symbol     { get; set; } = "";
        public int TrainBars     { get; set; }
        public int TestBars      { get; set; }
        public int Stride        { get; set; }
        public List<WalkForwardFold> Folds { get; set; } = new();

        // 聚合 OOS 指標——每 fold 的 test 結果平均/總和
        public decimal AvgTestReturnPct  { get; set; }
        public decimal MedianTestReturnPct { get; set; }
        public decimal AvgTestSharpe     { get; set; }
        public decimal AvgTestWinRate    { get; set; }
        public decimal WorstTestDdPct    { get; set; }
        public int     PositiveTestFolds { get; set; }
        public int     TotalFolds        { get; set; }

        // In-sample / out-of-sample gap——過擬合的指標。差距大 = 訓練看起來好但測試垃圾
        public decimal IsOosReturnGap    { get; set; }  // (avg train return) - (avg test return)
        public decimal IsOosSharpeGap    { get; set; }
    }

    /// <summary>
    /// Walk-forward 回測——把 bars 切成 [train, test, train, test, ...] 滑動視窗，
    /// 每個 fold 用 train 窗回測算 in-sample 指標，再用 test 窗做 out-of-sample 驗證。
    ///
    /// 標準學術 backtest 嚴謹度高、可看出策略是否過擬合：
    ///   - 如果 train 報酬遠優於 test 報酬 → 過擬合（看歷史撿便宜）
    ///   - 如果 train 跟 test 差不多 → 真有 alpha
    ///
    /// 預設 train=180、test=60、stride=30——對日線約是 9 個月訓練 / 3 個月測試 / 1.5 月間隔。
    /// </summary>
    public static WalkForwardResult RunWalkForward(
        IStrategy strategy,
        List<BarData> bars,
        StrategyConfig config,
        int trainBars = 180,
        int testBars  = 60,
        int stride    = 30,
        decimal initialCash = 100_000,
        decimal commission = 0.001m)
    {
        var result = new WalkForwardResult
        {
            Strategy = strategy.Name,
            Symbol   = config.Symbol,
            TrainBars = trainBars,
            TestBars  = testBars,
            Stride    = stride,
        };

        var requiredPerFold = trainBars + testBars;
        if (bars.Count < requiredPerFold) return result;
        if (trainBars < 50 || testBars < 10 || stride < 1) return result;

        // 從 0 開始，每 stride 切一個 fold；最後一個 fold 必須完整覆蓋 train+test
        int foldIdx = 0;
        for (int start = 0; start + requiredPerFold <= bars.Count; start += stride)
        {
            var trainSlice = bars.GetRange(start, trainBars);
            var testSlice  = bars.GetRange(start + trainBars, testBars);

            // 訓練窗：直接跑 backtest
            var trainBt = Run(strategy, trainSlice, config, initialCash, commission);
            // 測試窗：跑 backtest 但 strategy.Evaluate 內部仍只看自己窗內 bars（pure OOS）
            var testBt  = Run(strategy, testSlice, config, initialCash, commission);

            result.Folds.Add(new WalkForwardFold
            {
                FoldIndex  = foldIdx++,
                TrainStart = trainSlice.First().OpenTime,
                TrainEnd   = trainSlice.Last().OpenTime,
                TestStart  = testSlice.First().OpenTime,
                TestEnd    = testSlice.Last().OpenTime,
                Train      = trainBt,
                Test       = testBt,
            });
        }

        // 聚合 OOS 統計
        if (result.Folds.Count == 0) return result;
        var testReturns = result.Folds.Where(f => f.Test != null).Select(f => f.Test!.TotalReturnPct).ToList();
        var trainReturns = result.Folds.Where(f => f.Train != null).Select(f => f.Train!.TotalReturnPct).ToList();
        var testSharpes  = result.Folds.Where(f => f.Test != null).Select(f => f.Test!.SharpeRatio).ToList();
        var trainSharpes = result.Folds.Where(f => f.Train != null).Select(f => f.Train!.SharpeRatio).ToList();
        var testWins     = result.Folds.Where(f => f.Test != null).Select(f => f.Test!.WinRate).ToList();
        var testDds      = result.Folds.Where(f => f.Test != null).Select(f => f.Test!.MaxDrawdownPct).ToList();

        result.TotalFolds         = result.Folds.Count;
        result.PositiveTestFolds  = testReturns.Count(r => r > 0);
        result.AvgTestReturnPct   = testReturns.Count > 0 ? Math.Round(testReturns.Average(), 2) : 0;
        result.MedianTestReturnPct = testReturns.Count > 0 ? Math.Round(Median(testReturns), 2) : 0;
        result.AvgTestSharpe      = testSharpes.Count > 0 ? Math.Round(testSharpes.Average(), 2) : 0;
        result.AvgTestWinRate     = testWins.Count > 0 ? Math.Round(testWins.Average(), 2) : 0;
        result.WorstTestDdPct     = testDds.Count > 0 ? Math.Round(testDds.Max(), 2) : 0;  // DdPct 是正數、越大越差
        result.IsOosReturnGap     = trainReturns.Count > 0 && testReturns.Count > 0
            ? Math.Round(trainReturns.Average() - testReturns.Average(), 2) : 0;
        result.IsOosSharpeGap     = trainSharpes.Count > 0 && testSharpes.Count > 0
            ? Math.Round(trainSharpes.Average() - testSharpes.Average(), 2) : 0;

        return result;
    }

    private static decimal Median(List<decimal> xs)
    {
        if (xs.Count == 0) return 0;
        var sorted = xs.OrderBy(x => x).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2m;
    }
}
