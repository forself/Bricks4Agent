using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 告警觸發事件紀錄（#2 alert system）——一條規則觸發一次就寫一筆，
/// 透過 AcknowledgedAt 是否為 null 區分「未處理 / 已處理」。
///
/// 跟 AlertRuleEntry 是 1-to-many：同一條規則 cooldown 過後可再觸發、各自留紀錄。
/// </summary>
[Table("alert_events")]
public class AlertEventEntry
{
    [Key(AutoIncrement = false)]
    [Column("id")]
    [MaxLength(40)]
    public string Id { get; set; } = string.Empty;  // "evt-{guid14}"

    [Column("rule_id")]
    [Required]
    [MaxLength(40)]
    public string RuleId { get; set; } = string.Empty;

    [Column("rule_name")]
    [MaxLength(120)]
    public string RuleName { get; set; } = string.Empty;

    [Column("condition_type")]
    [Required]
    [MaxLength(40)]
    public string ConditionType { get; set; } = string.Empty;

    [Column("symbol")]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;

    [Column("exchange")]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;

    [Column("threshold")]
    public decimal Threshold { get; set; }

    /// <summary>觸發當下實際觀察值（price / pnl% / dd%）</summary>
    [Column("observed_value")]
    public decimal ObservedValue { get; set; }

    [Column("message")]
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [Column("triggered_at")]
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    [Column("acknowledged_at")]
    public DateTime? AcknowledgedAt { get; set; }
}
