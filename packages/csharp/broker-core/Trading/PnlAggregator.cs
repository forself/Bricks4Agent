namespace BrokerCore.Trading;

/// <summary>
/// 對一組 realized_pnl 數列做基本績效聚合——勝率、利潤因子、平均賺/賠等。
/// 抽出來避免 DailyReportService、TradingEndpoints.pnl-summary、之後 dashboard 端
/// 各寫一遍且各算各的（之前已經有微妙差異）。
///
/// 設計：input 只接 IEnumerable&lt;decimal&gt;（已經是 realized PnL），caller 自己決定
/// 怎麼從 JSON / DB row 抽欄位、要不要過濾 strategy / symbol。
/// </summary>
public static class PnlAggregator
{
    public class Stats
    {
        public int TradeCount    { get; set; }
        public int WinCount      { get; set; }
        public int LoseCount     { get; set; }
        public decimal RealizedPnlSum { get; set; }
        public decimal WinRatePct    { get; set; }
        public decimal AvgWin        { get; set; }
        public decimal AvgLoss       { get; set; }
        public decimal ProfitFactor  { get; set; }
    }

    public static Stats Aggregate(IEnumerable<decimal> realizedPnls)
    {
        int total = 0, wins = 0, loses = 0;
        decimal pnlSum = 0m, winSum = 0m, lossSum = 0m;

        foreach (var pnl in realizedPnls)
        {
            total++;
            pnlSum += pnl;
            if (pnl > 0m)      { wins++;  winSum  += pnl; }
            else if (pnl < 0m) { loses++; lossSum += pnl; }
            // pnl == 0 仍算 total（成交但沒賺賠），不入勝/敗
        }

        return new Stats
        {
            TradeCount     = total,
            WinCount       = wins,
            LoseCount      = loses,
            RealizedPnlSum = Math.Round(pnlSum, 4),
            WinRatePct     = total > 0 ? Math.Round(100m * wins / total, 1) : 0m,
            AvgWin         = wins  > 0 ? Math.Round(winSum  / wins,  4) : 0m,
            AvgLoss        = loses > 0 ? Math.Round(lossSum / loses, 4) : 0m,
            // ProfitFactor：總賺 / |總賠|；無虧損但有獲利 → 99.99 代表「∞」、兩邊都 0 → 0
            ProfitFactor   = lossSum < 0m
                                ? Math.Round(winSum / Math.Abs(lossSum), 3)
                                : (winSum > 0m ? 99.99m : 0m),
        };
    }
}
