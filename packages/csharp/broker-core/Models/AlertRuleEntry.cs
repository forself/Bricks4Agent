using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 告警規則持久化（#2 alert system）。
///
/// 跟既有 PriceAlertService 的差異：
///  - 那邊是純記憶體 + 觸發即刪、這邊是 SQLite 持久化 + 規則永久存在
///  - 那邊只有 above/below 兩種 condition、這邊支援多種：price_above/below /
///    position_pnl_below / portfolio_dd_above
///  - 那邊沒 acknowledge、觸發即消失；這邊規則保留、event 另存一張表（AlertEventEntry）
///    並支援 acknowledge workflow
///
/// CooldownMinutes：避免單一規則連環觸發、每次觸發後 N 分鐘才能再觸發（預設 30）。
/// LastTriggeredAt：用來判斷 cooldown 是否到期。
/// </summary>
[Table("alert_rules")]
public class AlertRuleEntry
{
    [Key(AutoIncrement = false)]
    [Column("id")]
    [MaxLength(40)]
    public string Id { get; set; } = string.Empty;  // "rule-{guid14}"

    [Column("name")]
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>"price_above" | "price_below" | "position_pnl_below" | "portfolio_dd_above"</summary>
    [Column("condition_type")]
    [Required]
    [MaxLength(40)]
    public string ConditionType { get; set; } = "price_above";

    [Column("symbol")]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;  // for price_*/position_pnl_*

    [Column("exchange")]
    [MaxLength(40)]
    public string Exchange { get; set; } = "alpaca";

    /// <summary>price 規則 = 絕對價、pnl 規則 = % 數（負數代表「虧到 X% 觸發」）、dd 規則 = % 數</summary>
    [Column("threshold")]
    public decimal Threshold { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("cooldown_minutes")]
    public int CooldownMinutes { get; set; } = 30;

    [Column("last_triggered_at")]
    public DateTime? LastTriggeredAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
