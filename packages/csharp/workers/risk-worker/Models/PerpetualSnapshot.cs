namespace RiskWorker.Models;

/// <summary>
/// 永續合約曝險快照——給 perp 系列規則用（max_leverage / max_total_notional /
/// max_liquidation_distance）。跟 spot 的 PortfolioSnapshot 故意分開：兩種帳戶
/// 體系不共用、概念也不一樣（perp 有 leverage / liquidation 但 spot 沒有）。
/// </summary>
public class PerpetualSnapshot
{
    public decimal Balance         { get; set; }   // USDT 保證金餘額
    public decimal AvailableMargin { get; set; }   // 可用保證金（已開倉占用後剩多少）
    public List<PerpetualPositionInfo> Positions { get; set; } = new();

    /// <summary>
    /// 今天（UTC 跨日）累計 PnL %（balance - today_open_balance）/ today_open_balance × 100。
    /// 負值表示今天賠錢。給 r16 max_perp_daily_loss_pct 用。
    /// 由 AutoTraderService 從 perp_daily_open_balance 表算好後填進來；caller 不填 → 0（不會誤觸熔斷）。
    /// </summary>
    public decimal DayPnlPct { get; set; }
}

public class PerpetualPositionInfo
{
    public string Symbol        { get; set; } = string.Empty;
    public string Exchange      { get; set; } = string.Empty;
    public string PositionSide  { get; set; } = string.Empty; // "long" / "short"
    public decimal Quantity     { get; set; }
    public decimal MarkPrice    { get; set; }
    public decimal Notional     { get; set; }                 // qty × mark_price (USDT)
    public int Leverage         { get; set; }
    public decimal LiquidationDistancePct { get; set; }
}
