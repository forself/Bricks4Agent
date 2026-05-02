using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Auto-trader 監控清單持久化（2026-05-02 補完）。
///
/// 解決什麼：原本 AutoTraderService._watchList 是 ConcurrentDictionary 純記憶體，
/// broker 一重啟所有監控的 symbol、策略、quantity 全消失，使用者得手動重設。
/// 現在每筆 watch 進這張表，broker 啟動時 reload 重建記憶體 dict。
///
/// EntryKey 是 "{exchange}:{symbol}" — 跟記憶體 dict 的 key 同格式，遷移無痛。
/// 同 (exchange, symbol) 同時間只能有一筆 watch（同 PK 覆寫）。
///
/// LastSignal / LastConfidence / LastCheck 也存——重啟後 dashboard 還能看到「上次評估
/// 結果」直到下一輪 poll 覆蓋；不存的話 UI 會閃一下 N/A，體驗差。
/// </summary>
[Table("auto_trade_watchlist")]
public class AutoTradeWatchEntry
{
    [Key(AutoIncrement = false)]
    [Column("entry_key")]
    [MaxLength(120)]
    public string EntryKey { get; set; } = string.Empty;

    [Column("symbol")]
    [Required]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;

    [Column("exchange")]
    [Required]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;

    [Column("strategy")]
    [MaxLength(60)]
    public string Strategy { get; set; } = "composite";

    [Column("quantity")]
    public decimal Quantity { get; set; } = 1;

    [Column("active")]
    public bool Active { get; set; } = true;

    [Column("last_signal")]
    [MaxLength(20)]
    public string? LastSignal { get; set; }

    [Column("last_confidence")]
    public decimal LastConfidence { get; set; }

    [Column("last_check")]
    public DateTime? LastCheck { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
