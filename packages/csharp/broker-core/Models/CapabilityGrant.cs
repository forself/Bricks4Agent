using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 能力授予 —— 將能力綁定到特定任務+session+主體
/// 含範圍覆寫、配額追蹤、時效
/// </summary>
[Table("capability_grants")]
public class CapabilityGrant
{
    [Key(AutoIncrement = false)]
    [Column("grant_id")]
    public string GrantId { get; set; } = string.Empty;

    [Column("task_id")]
    [Required]
    public string TaskId { get; set; } = string.Empty;

    [Column("session_id")]
    [Required]
    public string SessionId { get; set; } = string.Empty;

    [Column("principal_id")]
    [Required]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("capability_id")]
    [Required]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>範圍覆寫（JSON，例如 {"paths": ["/workspace/proj-a"]}）</summary>
    [Column("scope_override")]
    public string ScopeOverride { get; set; } = "{}";

    /// <summary>剩餘配額（-1 = 無限制）</summary>
    [Column("remaining_quota")]
    public int RemainingQuota { get; set; } = -1;

    [Column("issued_at")]
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("status")]
    public int StatusValue { get; set; }

    [Ignore]
    public GrantStatus Status
    {
        get => (GrantStatus)StatusValue;
        set => StatusValue = (int)value;
    }
}
