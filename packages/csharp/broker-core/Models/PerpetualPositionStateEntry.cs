using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Perpetual 部位保護狀態持久化（Phase 4 雙向保護鏈用）。
///
/// 解決什麼：`AutoTraderService._perpPositionState` 是 in-memory ConcurrentDictionary。
/// broker 一重啟、所有部位的 SL / peak / partial_exited / be_moved 都歸零，
/// 下一個 cycle 就會用「entry = 當下 mark」「SL = entry - initial_sl_pct」重建 state——
/// 但實際 entry 早已偏離當下 mark、新算的 SL 可能跑到很離譜的位置（最壞情況：
/// 已賺 3% 的多單 broker 重啟後 SL 從新 entry 下方 5% 起算 = 等於放棄已賺利潤、
/// 並且把停損推到比原本還深）。持久化這張表後、重啟 cycle 直接 load 既有 state、
/// peak 跟 SL 都不會被吃掉。
///
/// EntryKey 格式：`{exchange}:{symbol}:{side}` — side 是 long/short
/// 因為 hedge mode 下同 symbol 可同時有多空兩個部位、需要分開追蹤。
/// </summary>
[Table("perp_position_state")]
public class PerpetualPositionStateEntry
{
    [Key(AutoIncrement = false)]
    [Column("entry_key")]
    [MaxLength(140)]
    public string EntryKey { get; set; } = string.Empty;

    /// <summary>
    /// Phase A2.5b PASS 2：擁有者 principal_id。同 (exchange, symbol, side) 不同用戶視為不同部位、
    /// state key 加 owner 前綴避免互相覆蓋。Migration 對既有資料一律標 'prn_dashboard'（admin）。
    /// </summary>
    [Column("owner_principal_id")]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = "prn_dashboard";

    [Column("exchange")]
    [Required]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;

    [Column("symbol")]
    [Required]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;

    [Column("side")]
    [MaxLength(10)]
    public string Side { get; set; } = "long";

    [Column("entry_price")]
    public decimal EntryPrice { get; set; }

    [Column("peak_mark")]
    public decimal PeakMark { get; set; }

    [Column("sl_price")]
    public decimal SlPrice { get; set; }

    [Column("liquidation_price")]
    public decimal LiquidationPrice { get; set; }

    [Column("leverage")]
    public int Leverage { get; set; } = 1;

    [Column("partial_exited")]
    public bool PartialExited { get; set; }

    [Column("be_moved")]
    public bool BeMoved { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
