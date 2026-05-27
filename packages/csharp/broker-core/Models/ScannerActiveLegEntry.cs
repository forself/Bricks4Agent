using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Scanner 已開部位狀態持久化(Portfolio Scanner Hybrid Phase 1)。
///
/// 解決什麼:scanner 在 universe 內挑出該開的 (策略, 幣) 配對、實際開倉後變成一筆 active leg。
/// 跟 [[PerpetualPositionStateEntry]] 一樣需要 SL/peak/be_moved 等保護鏈狀態持久化、broker 重啟不會吃掉。
/// 跟核心腿差別:active leg 隨訊號開關、結束就 delete 該列;核心腿 (`auto_trade_watchlist`) 是長期定義、
/// 部位開關不影響該列存在。
///
/// 用 `scanner_id + symbol + opened_at` 三欄當複合 idempotency key、避免同一個 signal_bar 重複下單
/// (見 [[feedback_real_money_idempotency]])。
///
/// 設計來源:[docs/designs/portfolio-scanner-hybrid.md](../../../../docs/designs/portfolio-scanner-hybrid.md)
/// </summary>
[Table("scanner_active_legs")]
public class ScannerActiveLegEntry
{
    [Key(AutoIncrement = false)]
    [Column("id")]
    [MaxLength(80)]
    public string Id { get; set; } = string.Empty;

    /// <summary>來源 scanner [[ScannerLegEntry]].Id。</summary>
    [Column("scanner_id")]
    [Required]
    [MaxLength(60)]
    public string ScannerId { get; set; } = string.Empty;

    [Column("symbol")]
    [Required]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;

    [Column("exchange")]
    [Required]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;

    /// <summary>"long" / "short"(對齊 [[PerpetualPositionStateEntry]].Side)。spot 模式只會是 "long"。</summary>
    [Column("side")]
    [Required]
    [MaxLength(8)]
    public string Side { get; set; } = "long";

    [Column("entry_price")]
    public decimal EntryPrice { get; set; }

    /// <summary>開倉名目 USDT(已扣手續費前)。配重檢查用。</summary>
    [Column("notional")]
    public decimal Notional { get; set; }

    /// <summary>觸發進場的訊號 bar 開盤時間(UTC ms)。冪等鎖第三個欄位:同 scanner+symbol+signal_bar 不重複下單。</summary>
    [Column("signal_bar_ts")]
    public long SignalBarTs { get; set; }

    /// <summary>觸發開倉的訊號名(策略內訊號名、debug 用)。e.g. "PRZ_buy@1d"。</summary>
    [Column("entry_signal")]
    [MaxLength(60)]
    public string EntrySignal { get; set; } = string.Empty;

    /// <summary>開倉信心 0..1。</summary>
    [Column("entry_confidence")]
    public decimal EntryConfidence { get; set; }

    // ── 保護鏈狀態(鏡像 [[PerpetualPositionStateEntry]])──

    /// <summary>持倉期間最佳 mark price(long=最高、short=最低)、peak-trail 用。</summary>
    [Column("peak_mark")]
    public decimal PeakMark { get; set; }

    /// <summary>當前 SL 價格、>0 啟用、=0 不啟用(由 AutoTrader peak-trail 動態 ratchet)。</summary>
    [Column("sl_price")]
    public decimal SlPrice { get; set; }

    /// <summary>TP 價格、>0 啟用(策略給 Signal.TargetPrice 時設、對齊 H16)。</summary>
    [Column("tp_price")]
    public decimal TpPrice { get; set; }

    /// <summary>強平價估算(perp 模式才填、給 risk dashboard 看)。</summary>
    [Column("liquidation_price")]
    public decimal LiquidationPrice { get; set; }

    [Column("leverage")]
    public int Leverage { get; set; } = 1;

    /// <summary>是否已部分減倉(對齊 [[PerpetualPositionStateEntry]].PartialExited)。</summary>
    [Column("partial_exited")]
    public bool PartialExited { get; set; }

    /// <summary>SL 是否已移動到 breakeven。</summary>
    [Column("be_moved")]
    public bool BeMoved { get; set; }

    /// <summary>是否 shadow 開倉(只記不下單)。對齊來源 [[ScannerLegEntry]].Shadow。</summary>
    [Column("shadow")]
    public bool Shadow { get; set; } = false;

    [Column("opened_at")]
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>擁有者(對齊核心腿)。</summary>
    [Column("owner_principal_id")]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = "prn_dashboard";
}
