using BaseOrm;

namespace Broker.Models;

/// <summary>
/// H2 — Time-windowed ACL rule。指定一個 capability 的「自動放行時段」、時段外強制走 approval。
///
/// 例：strategy.signal 平日 08:00-22:00（Asia/Taipei）正常 auto、半夜跟假日強制 require approval。
/// 用途：很多 capability 平日忙時可信、半夜出包機率低、但若出問題就不該無人值守自動放行。
/// 把時間維度加進 ACL，補強 Benson 的 PolicyEngine（原本沒時間概念）。
///
/// 規則邏輯：
///   現在時間（按 Timezone 換算） in [StartHour, EndHour) AND weekday in mask
///     → 走原 ACL（capability 預設行為）
///   否則
///     → 強制 require_approval（覆蓋 capability 預設、不論原本是 auto 還是 require_approval）
///
/// 跨日視窗（StartHour=22, EndHour=2）支援：表示「22:00 至隔日 02:00」auto。
/// </summary>
[Table("time_acl_rules")]
public class TimeAclRule
{
    [Key(AutoIncrement = false), MaxLength(64)]
    [Column("rule_id")]
    public string RuleId { get; set; } = string.Empty;

    [Column("capability_id"), MaxLength(80), Required]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>auto window 起始小時（0-23）</summary>
    [Column("start_hour")]
    public int StartHour { get; set; } = 9;

    /// <summary>auto window 結束小時（0-23）；end &lt; start 表示跨日</summary>
    [Column("end_hour")]
    public int EndHour { get; set; } = 17;

    /// <summary>weekday bitmask：bit0=Sun, bit1=Mon ... bit6=Sat。預設 0b0111110 = Mon-Fri</summary>
    [Column("weekday_mask")]
    public int WeekdayMask { get; set; } = 0b0111110;

    /// <summary>時區 IANA 名（"UTC" / "Asia/Taipei"）</summary>
    [Column("timezone"), MaxLength(64)]
    public string Timezone { get; set; } = "UTC";

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("description"), MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Column("created_by"), MaxLength(64)]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
