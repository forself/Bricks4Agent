using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 多空(long-short)回測引擎 —— Benson 的 BacktestEngine 是 long-only(buy 開多、sell 只平倉),
/// 這支獨立新增、不動原檔,把訊號語意擴成「多空反手(stop-and-reverse)」:
///   buy(信心≥0.6)  → 目標做多:空單先回補再開多;空手開多;已多維持
///   sell(信心≥0.6) → 目標做空:多單先平再開空;空手開空;已空維持
///   hold / 低信心    → 維持當前部位
///
/// 現金記帳對多空通用:equity = cash + position×price(position 帶正負號)。
///   開多:cash −= qty×price;開空:cash += qty×price(收到賣出價金);兩邊都再扣手續費。
///   → 價漲時多單 equity 升、空單 equity 降,符合直覺。無槓桿(名目 ≤ notionalPct×equity)。
///
/// 無 lookahead:逐根只餵 bars[0..i];部位用「當根收盤」進出。tradeStartIndex 給 walk-forward OOS 用
/// (前段只當指標 warmup、不計交易/績效)。
/// </summary>
public static class LongShortBacktestEngine
{
    public static BacktestEngine.BacktestResult Run(
        IStrategy strategy,
        List<BarData> bars,
        StrategyConfig config,
        decimal initialCash = 100_000,
        decimal commission = 0.001m,
        int tradeStartIndex = 0,
        decimal slippagePct = 0m,
        decimal notionalPct = 0.95m,   // 每次開倉名目佔當前 equity 比例(無槓桿)
        bool confidenceSizing = false) // true:名目再 × signal.Confidence(淨加權 ensemble 用來「分歧縮量」)
    {
        var costRate = commission + slippagePct;
        var result = new BacktestEngine.BacktestResult
        {
            Strategy = strategy.Name,
            Symbol = config.Symbol,
            InitialCash = initialCash,
            TotalBars = bars.Count,
            StartDate = bars.FirstOrDefault()?.OpenTime ?? DateTime.MinValue,
            EndDate = bars.LastOrDefault()?.OpenTime ?? DateTime.MinValue,
        };
        if (bars.Count < 50) return result;

        decimal cash = initialCash;
        decimal position = 0m;       // 帶正負號:>0 多、<0 空
        decimal entryPrice = 0m;
        int entryIndex = -1;
        DateTime entryDate = DateTime.MinValue;
        decimal activeStopPrice = 0m;  // H2-Fib SL 支援:開倉時鎖定的停損價(0=不啟用、靠反向訊號平倉)
        decimal peakEquity = initialCash, maxDrawdown = 0m, prevEquity = initialCash;
        var dailyReturns = new List<decimal>();

        int lookback = Math.Max(Math.Max(config.SmaSlow + 5, 50), tradeStartIndex);

        void CloseAt(int i, decimal px)
        {
            if (position == 0m) return;
            decimal qty = Math.Abs(position);
            decimal exitNotional = qty * px;
            cash += position * px;                       // 多:賣出回現金;空:回補付現(position 負 → cash 減)
            cash -= exitNotional * costRate;
            decimal pnl = position * (px - entryPrice)
                          - qty * entryPrice * costRate
                          - exitNotional * costRate;
            decimal pnlPct = entryPrice > 0m
                ? (position > 0m ? (px - entryPrice) : (entryPrice - px)) / entryPrice * 100m
                : 0m;
            result.Trades.Add(new BacktestEngine.BacktestTrade
            {
                Side = position > 0m ? "long" : "short",
                EntryDate = entryDate, EntryPrice = entryPrice,
                ExitDate = bars[i].OpenTime, ExitPrice = px,
                Quantity = qty, Pnl = Math.Round(pnl, 2), PnlPct = Math.Round(pnlPct, 2),
                HoldBars = entryIndex >= 0 ? i - entryIndex : 0,
            });
            position = 0m; entryPrice = 0m; entryIndex = -1; activeStopPrice = 0m;
        }

        void OpenAt(int i, decimal px, int dir, decimal sizeScale, decimal stop)
        {
            decimal eqNow = cash;                        // 已平倉、position=0 → equity=cash
            decimal notional = eqNow * notionalPct * sizeScale;
            if (notional <= 0m) return;
            decimal qty = notional / px;
            position = dir * qty;
            cash -= position * px;                        // 多:扣現;空:收現金
            cash -= notional * costRate;
            entryPrice = px; entryIndex = i; entryDate = bars[i].OpenTime;
            activeStopPrice = stop;                       // H2-Fib SL:訊號未帶 stop 時=0、不啟用
        }

        for (int i = lookback; i < bars.Count; i++)
        {
            var window = bars.GetRange(0, i + 1);
            var signal = strategy.Evaluate(window, config);
            var price = bars[i].Close;

            // ── H2-Fib SL:盤中觸 SL 即平倉(不發 StopPrice 的策略 activeStopPrice=0、整段 skip)──
            bool stoppedOutThisBar = false;
            if (position != 0m && activeStopPrice > 0m)
            {
                bool slHit = position > 0m
                    ? bars[i].Low  <= activeStopPrice    // long: 跌破 SL
                    : bars[i].High >= activeStopPrice;   // short: 漲破 SL
                if (slHit)
                {
                    CloseAt(i, activeStopPrice);          // 在 SL 價位出場(保守、會清 activeStopPrice)
                    stoppedOutThisBar = true;
                }
            }

            var equity = cash + position * price;
            result.EquityCurve.Add(new BacktestEngine.EquityPoint { Date = bars[i].OpenTime, Value = equity });
            if (prevEquity > 0m) dailyReturns.Add((equity - prevEquity) / prevEquity);
            prevEquity = equity;
            if (equity > peakEquity) peakEquity = equity;
            var dd = peakEquity - equity;
            if (dd > maxDrawdown) maxDrawdown = dd;

            if (stoppedOutThisBar) continue;              // 本根已 SL 出場、不再因訊號開新倉

            int cur = position > 0m ? 1 : position < 0m ? -1 : 0;
            int desired = cur;
            if (signal.Action == "buy" && signal.Confidence >= 0.6m) desired = 1;
            else if (signal.Action == "sell" && signal.Confidence >= 0.6m) desired = -1;
            // hold 或低信心 → desired = cur(維持)

            if (desired != cur)
            {
                CloseAt(i, price);
                if (desired != 0)
                {
                    decimal sizeScale = confidenceSizing ? Math.Clamp(signal.Confidence, 0m, 1m) : 1m;
                    OpenAt(i, price, desired, sizeScale, signal.StopPrice ?? 0m);
                }
            }
        }

        if (position != 0m) CloseAt(bars.Count - 1, bars[^1].Close);

        result.FinalValue = cash;
        result.TotalReturn = Math.Round(cash - initialCash, 2);
        result.TotalReturnPct = Math.Round((cash - initialCash) / initialCash * 100m, 2);
        result.TotalTrades = result.Trades.Count;
        result.WinTrades = result.Trades.Count(t => t.Pnl > 0);
        result.LoseTrades = result.Trades.Count(t => t.Pnl <= 0);
        result.WinRate = result.TotalTrades > 0 ? Math.Round((decimal)result.WinTrades / result.TotalTrades * 100m, 1) : 0m;
        result.MaxDrawdown = Math.Round(maxDrawdown, 2);
        result.MaxDrawdownPct = peakEquity > 0m ? Math.Round(maxDrawdown / peakEquity * 100m, 2) : 0m;
        var wins = result.Trades.Where(t => t.Pnl > 0).ToList();
        var losses = result.Trades.Where(t => t.Pnl <= 0).ToList();
        result.AvgWin = wins.Count > 0 ? Math.Round(wins.Average(t => t.Pnl), 2) : 0m;
        result.AvgLoss = losses.Count > 0 ? Math.Round(losses.Average(t => t.Pnl), 2) : 0m;
        result.ProfitFactor = losses.Sum(t => Math.Abs(t.Pnl)) > 0m
            ? Math.Round(wins.Sum(t => t.Pnl) / losses.Sum(t => Math.Abs(t.Pnl)), 2) : 0m;
        if (dailyReturns.Count > 1)
        {
            var avg = dailyReturns.Average();
            var std = (decimal)Math.Sqrt((double)dailyReturns.Select(r => (r - avg) * (r - avg)).Average());
            result.SharpeRatio = std > 0m ? Math.Round(avg / std * (decimal)Math.Sqrt(252), 2) : 0m;
        }
        return result;
    }

    /// <summary>多空版 walk-forward(切窗邏輯同 BacktestEngine.RunWalkForward、改用本引擎 Run)。</summary>
    public static BacktestEngine.WalkForwardResult RunWalkForward(
        IStrategy strategy, List<BarData> bars, StrategyConfig config,
        int trainBars = 250, int testBars = 90, int stride = 60,
        decimal initialCash = 100_000, decimal commission = 0.001m, decimal slippagePct = 0m,
        bool confidenceSizing = false)
    {
        var result = new BacktestEngine.WalkForwardResult
        {
            Strategy = strategy.Name, Symbol = config.Symbol,
            TrainBars = trainBars, TestBars = testBars, Stride = stride,
        };
        var requiredPerFold = trainBars + testBars;
        if (bars.Count < requiredPerFold || trainBars < 50 || testBars < 10 || stride < 1) return result;

        int foldIdx = 0;
        for (int start = 0; start + requiredPerFold <= bars.Count; start += stride)
        {
            var trainSlice = bars.GetRange(start, trainBars);
            var trainBt = Run(strategy, trainSlice, config, initialCash, commission, slippagePct: slippagePct, confidenceSizing: confidenceSizing);
            var testWindow = bars.GetRange(start, trainBars + testBars);
            var testBt = Run(strategy, testWindow, config, initialCash, commission,
                tradeStartIndex: trainBars, slippagePct: slippagePct, confidenceSizing: confidenceSizing);
            result.Folds.Add(new BacktestEngine.WalkForwardFold
            {
                FoldIndex = foldIdx++,
                TrainStart = trainSlice.First().OpenTime, TrainEnd = trainSlice.Last().OpenTime,
                TestStart = bars[start + trainBars].OpenTime, TestEnd = bars[start + trainBars + testBars - 1].OpenTime,
                Train = trainBt, Test = testBt,
            });
        }
        if (result.Folds.Count == 0) return result;

        var testReturns = result.Folds.Select(f => f.Test!.TotalReturnPct).ToList();
        var trainReturns = result.Folds.Select(f => f.Train!.TotalReturnPct).ToList();
        var testSharpes = result.Folds.Select(f => f.Test!.SharpeRatio).ToList();
        result.TotalFolds = result.Folds.Count;
        result.PositiveTestFolds = testReturns.Count(r => r > 0);
        result.AvgTestReturnPct = Math.Round(testReturns.Average(), 2);
        result.MedianTestReturnPct = Math.Round(Median(testReturns), 2);
        result.AvgTestSharpe = Math.Round(testSharpes.Average(), 2);
        result.AvgTestWinRate = Math.Round(result.Folds.Select(f => f.Test!.WinRate).Average(), 2);
        result.WorstTestDdPct = Math.Round(result.Folds.Select(f => f.Test!.MaxDrawdownPct).Max(), 2);
        result.IsOosReturnGap = Math.Round(trainReturns.Average() - testReturns.Average(), 2);
        return result;
    }

    /// <summary>
    /// 多空組合層:N 支策略各用 1/N 資金、獨立多空持倉,逐根把權益曲線相加。
    /// 各 sub 用同一 loop 起點(lookback)→ EquityCurve 對齊 index、可逐點相加。
    /// </summary>
    public static BacktestEngine.BacktestResult RunPortfolio(
        List<IStrategy> strategies, List<BarData> bars, StrategyConfig config,
        decimal initialCash = 100_000, decimal commission = 0.001m,
        int tradeStartIndex = 0, decimal slippagePct = 0m, decimal notionalPct = 0.95m)
    {
        var result = new BacktestEngine.BacktestResult
        {
            Strategy = "ls_portfolio", Symbol = config.Symbol, InitialCash = initialCash, TotalBars = bars.Count,
            StartDate = bars.FirstOrDefault()?.OpenTime ?? DateTime.MinValue,
            EndDate = bars.LastOrDefault()?.OpenTime ?? DateTime.MinValue,
        };
        if (strategies == null || strategies.Count == 0 || bars.Count < 50) return result;

        var per = initialCash / strategies.Count;
        var subs = strategies
            .Select(s => Run(s, bars, config, per, commission, tradeStartIndex, slippagePct, notionalPct))
            .ToList();
        int len = subs.Min(b => b.EquityCurve.Count);
        if (len == 0) return result;

        decimal peak = initialCash, maxDd = 0m, prev = initialCash;
        var dailyRet = new List<decimal>();
        for (int t = 0; t < len; t++)
        {
            decimal eq = 0m;
            foreach (var b in subs) eq += b.EquityCurve[t].Value;
            result.EquityCurve.Add(new BacktestEngine.EquityPoint { Date = subs[0].EquityCurve[t].Date, Value = eq });
            if (prev > 0m) dailyRet.Add((eq - prev) / prev);
            prev = eq;
            if (eq > peak) peak = eq;
            var dd = peak - eq; if (dd > maxDd) maxDd = dd;
        }
        foreach (var b in subs) result.Trades.AddRange(b.Trades);

        var finalValue = subs.Sum(b => b.FinalValue);
        result.FinalValue = finalValue;
        result.TotalReturn = Math.Round(finalValue - initialCash, 2);
        result.TotalReturnPct = Math.Round((finalValue - initialCash) / initialCash * 100m, 2);
        result.TotalTrades = result.Trades.Count;
        result.WinTrades = result.Trades.Count(t => t.Pnl > 0);
        result.LoseTrades = result.Trades.Count(t => t.Pnl <= 0);
        result.WinRate = result.TotalTrades > 0 ? Math.Round((decimal)result.WinTrades / result.TotalTrades * 100m, 1) : 0m;
        result.MaxDrawdown = Math.Round(maxDd, 2);
        result.MaxDrawdownPct = peak > 0m ? Math.Round(maxDd / peak * 100m, 2) : 0m;
        if (dailyRet.Count > 1)
        {
            var avg = dailyRet.Average();
            var std = (decimal)Math.Sqrt((double)dailyRet.Select(r => (r - avg) * (r - avg)).Average());
            result.SharpeRatio = std > 0m ? Math.Round(avg / std * (decimal)Math.Sqrt(252), 2) : 0m;
        }
        return result;
    }

    /// <summary>多空組合的 walk-forward OOS(切窗同 RunWalkForward、每窗用 RunPortfolio)。</summary>
    public static BacktestEngine.WalkForwardResult RunPortfolioWalkForward(
        List<IStrategy> strategies, List<BarData> bars, StrategyConfig config,
        int trainBars = 250, int testBars = 90, int stride = 60,
        decimal initialCash = 100_000, decimal commission = 0.001m, decimal slippagePct = 0m)
    {
        var result = new BacktestEngine.WalkForwardResult
        {
            Strategy = "ls_portfolio", Symbol = config.Symbol,
            TrainBars = trainBars, TestBars = testBars, Stride = stride,
        };
        var requiredPerFold = trainBars + testBars;
        if (bars.Count < requiredPerFold || trainBars < 50 || testBars < 10 || stride < 1) return result;

        int foldIdx = 0;
        for (int start = 0; start + requiredPerFold <= bars.Count; start += stride)
        {
            var trainSlice = bars.GetRange(start, trainBars);
            var trainBt = RunPortfolio(strategies, trainSlice, config, initialCash, commission, slippagePct: slippagePct);
            var testWindow = bars.GetRange(start, trainBars + testBars);
            var testBt = RunPortfolio(strategies, testWindow, config, initialCash, commission,
                tradeStartIndex: trainBars, slippagePct: slippagePct);
            result.Folds.Add(new BacktestEngine.WalkForwardFold
            {
                FoldIndex = foldIdx++,
                TrainStart = trainSlice.First().OpenTime, TrainEnd = trainSlice.Last().OpenTime,
                TestStart = bars[start + trainBars].OpenTime, TestEnd = bars[start + trainBars + testBars - 1].OpenTime,
                Train = trainBt, Test = testBt,
            });
        }
        if (result.Folds.Count == 0) return result;

        var testReturns = result.Folds.Select(f => f.Test!.TotalReturnPct).ToList();
        var trainReturns = result.Folds.Select(f => f.Train!.TotalReturnPct).ToList();
        var testSharpes = result.Folds.Select(f => f.Test!.SharpeRatio).ToList();
        result.TotalFolds = result.Folds.Count;
        result.PositiveTestFolds = testReturns.Count(r => r > 0);
        result.AvgTestReturnPct = Math.Round(testReturns.Average(), 2);
        result.MedianTestReturnPct = Math.Round(Median(testReturns), 2);
        result.AvgTestSharpe = Math.Round(testSharpes.Average(), 2);
        result.WorstTestDdPct = Math.Round(result.Folds.Select(f => f.Test!.MaxDrawdownPct).Max(), 2);
        result.IsOosReturnGap = Math.Round(trainReturns.Average() - testReturns.Average(), 2);
        return result;
    }

    private static decimal Median(List<decimal> xs)
    {
        if (xs.Count == 0) return 0m;
        var s = xs.OrderBy(x => x).ToList(); int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2m;
    }
}
