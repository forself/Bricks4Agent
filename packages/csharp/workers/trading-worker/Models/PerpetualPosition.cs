namespace TradingWorker.Models;

/// <summary>
/// 永續合約持倉。
///
/// 跟 spot Position 的差別：
///   - Side: "long" or "short"（spot 只有 long、Position.Side 是 cosmetic）
///   - Leverage / MarginMode / MarginUsed / LiquidationPrice — perp 才有
///   - PnL 計算雙向：
///       long  PnL  = (mark - entry) × qty
///       short PnL  = (entry - mark) × qty
///   - 強平距離（liquidation_distance_pct）給保護鏈做「太靠近強平就主動平倉」用
/// </summary>
public class PerpetualPosition
{
    public string Symbol         { get; set; } = string.Empty;
    public string Exchange       { get; set; } = string.Empty;
    public string Side           { get; set; } = "long";       // "long" | "short"
    public decimal Quantity      { get; set; }                  // 永遠 ≥ 0、方向看 Side
    public decimal AvgEntryPrice { get; set; }
    public decimal MarkPrice     { get; set; }                  // 即時標記價（不是 last trade）
    public decimal UnrealizedPnl { get; set; }
    public decimal UnrealizedPnlPercent { get; set; }
    public int Leverage          { get; set; } = 1;
    public string MarginMode     { get; set; } = "isolated";   // "isolated" | "cross"
    public decimal MarginUsed    { get; set; }                  // 此部位佔用保證金（USDT）
    public decimal LiquidationPrice { get; set; }
    public decimal LiquidationDistancePct { get; set; }         // (mark - liq) / mark × 100，越小越危險
    public DateTime UpdatedAt    { get; set; } = DateTime.UtcNow;
}
