namespace QuoteWorker.Models;

/// <summary>
/// 散戶多空比一筆紀錄(Binance global account L/S ratio,通常 5min~1d 粒度)。
/// 2026-05-28 Q2 retail_ls_contrarian 結構性 alpha 來源(IS+OOS t=-2.89/-2.25 雙確認)。
/// 跟 funding 路徑平行,QuoteOhlcvHandler 對齊後 emit retail_long_short_ratio。
/// </summary>
public class RetailLsRatioPoint
{
    public string Symbol { get; set; } = string.Empty;  // 正規化後(BTCUSDT 等)
    public DateTime SampleTime { get; set; }
    public decimal LsRatio { get; set; }  // long_account / short_account; > 1 = 散戶看多
}
