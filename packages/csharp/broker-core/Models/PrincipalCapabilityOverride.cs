using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 個別 principal 的 capability ACL 覆寫。
///
/// 比角色預設規則優先：principal-specific override 比 role-based whitelist 先檢查。
///
/// 用途範例：
///   - 某個 user 例外可以呼叫 trading.order（給黃金客戶）
///   - 某個 admin 被臨時降權（不能呼叫 trading.*）
///
/// 設計：
///   action = "allow" → 把該 capability_pattern 加進 user 的允許名單
///   action = "deny"  → 把該 capability_pattern 從 user 的允許名單剔除（比 role 規則更嚴）
///
/// 順序：deny override > allow override > role rule
/// </summary>
[Table("principal_capability_overrides")]
public class PrincipalCapabilityOverride
{
    [Key, MaxLength(64)]
    [Column("override_id")]
    public string OverrideId { get; set; } = string.Empty;

    [Column("principal_id")]
    [MaxLength(64)]
    [Required]
    public string PrincipalId { get; set; } = string.Empty;

    /// <summary>capability pattern：支援 "trading.order" / "trading.*" / "*"</summary>
    [Column("capability_pattern")]
    [MaxLength(80)]
    [Required]
    public string CapabilityPattern { get; set; } = string.Empty;

    /// <summary>"allow" 或 "deny"</summary>
    [Column("action")]
    [MaxLength(10)]
    [Required]
    public string Action { get; set; } = "allow";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_by")]
    [MaxLength(64)]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("reason")]
    [MaxLength(500)]
    public string? Reason { get; set; }
}
