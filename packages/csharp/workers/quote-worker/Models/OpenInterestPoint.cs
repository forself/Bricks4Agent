namespace QuoteWorker.Models;

/// <summary>
/// 永續未平倉量一筆紀錄(sum_open_interest_value, USDT 名目)。
/// 2026-05-29 Q2 oi_contrarian alpha 來源(strat-validate 可用 / full 16% / Sharpe 0.41、跟 retail_ls corr -0.18 去相關)。
/// 跟 funding / retail_ls 路徑平行,QuoteOhlcvHandler 對齊後 emit open_interest。
/// </summary>
public class OpenInterestPoint
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime SampleTime { get; set; }
    public decimal OiValue { get; set; }   // sum_open_interest_value (USDT 名目)
}
