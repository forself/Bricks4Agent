namespace QuoteWorker.Models;

/// <summary>
/// 永續合約資金費率一筆紀錄（funding event,Binance 多為 8h 一次）。
/// 非價格因子、跟技術指標正交,用於回測 / 融合。
/// </summary>
public class FundingRatePoint
{
    public string Symbol { get; set; } = string.Empty;  // 正規化後（"BTC"、"ETH"…、與 ohlcv 表一致）
    public DateTime FundingTime { get; set; }
    public decimal FundingRate { get; set; }
}
