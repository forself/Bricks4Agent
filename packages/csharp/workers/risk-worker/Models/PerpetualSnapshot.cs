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
