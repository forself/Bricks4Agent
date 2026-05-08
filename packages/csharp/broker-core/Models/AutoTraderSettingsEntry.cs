using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Auto-trader 全域設定持久化（singleton 列、PK = "main"）。
///
/// 解決什麼：原本 `_enabled` / `_intervalSeconds` 是 in-memory，broker 重啟後
/// AutoTrader 永遠回到 disabled / 300s。每次 deploy 都要手動再 enable 一次、
/// 容易忘、遺漏即等於停止監控。現在這兩欄持久化、重啟後自動恢復先前狀態。
///
/// 不持久化 ConfigSnapshot 內的其他欄（min_confidence / max_portfolio_dd_pct /
/// protection_config）——那些是 env 注入、改了會重啟、不該被 DB 蓋掉。
/// </summary>
[Table("auto_trader_settings")]
public class AutoTraderSettingsEntry
{
    [Key(AutoIncrement = false)]
    [Column("singleton_key")]
    [MaxLength(20)]
    public string SingletonKey { get; set; } = "main";

    [Column("enabled")]
    public bool Enabled { get; set; }

    [Column("interval_seconds")]
    public int IntervalSeconds { get; set; } = 300;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
