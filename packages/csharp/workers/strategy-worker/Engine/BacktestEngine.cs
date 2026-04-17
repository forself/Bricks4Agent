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
    /// </summary>
    public static BacktestResult Run(
        IStrategy strategy,
        List<BarData> bars,
        StrategyConfig config,
        decimal initialCash = 100_000,
        decimal commission = 0.001m) // 0.1% 手續費
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
        for (int i = lookback; i < bars.Count; i++)
        {
            var windowBars = bars.GetRange(0, i + 1);
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
}
