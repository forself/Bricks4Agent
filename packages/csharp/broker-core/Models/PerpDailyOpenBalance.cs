using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 每個 exchange 每天 UTC 開盤時刻的 perp balance 快照。
///
/// 用途：daily loss circuit breaker（r16 max_perp_daily_loss_pct）需要知道
/// 「今天從多少錢開始」、才能算出當日跌幅。broker 重啟不丟、UTC 跨日自動更新。
///
/// 寫入時機（AutoTraderService cycle 開頭）：
///   - 找 (exchange, utc_date=today) 的 row
///   - 沒有 → insert current balance 當作今天的開盤值
///   - 有 → 不動
///
/// utc_date 用 yyyy-MM-dd 格式（不用 DateTime 是因為要按字串比對）。
/// </summary>
[Table("perp_daily_open_balance")]
public class PerpDailyOpenBalance
{
    [Key, MaxLength(80)]
    [Column("key")]
    public string Key { get; set; } = string.Empty;   // exchange:yyyy-MM-dd

    [Column("exchange")]
    [MaxLength(40)]
    [Required]
    public string Exchange { get; set; } = string.Empty;

    [Column("utc_date")]
    [MaxLength(10)]
    [Required]
    public string UtcDate { get; set; } = string.Empty;  // "2026-05-09"

    [Column("balance")]
    public decimal Balance { get; set; }

    [Column("captured_at")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
