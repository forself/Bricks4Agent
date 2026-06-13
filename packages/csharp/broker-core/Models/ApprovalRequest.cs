using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 升權審批請求(§18.2)—— 當 PolicyEngine 對某執行請求回 RequireApproval 時建立。
/// 持有被擱置的 execution request,等管理員(信任錨)核准/駁回。
/// </summary>
[Table("approval_requests")]
public class ApprovalRequest
{
    [Key(AutoIncrement = false)]
    [Column("approval_id")]
    public string ApprovalId { get; set; } = string.Empty;

    /// <summary>被擱置的執行請求 ID</summary>
    [Column("request_id")]
    [Required]
    public string RequestId { get; set; } = string.Empty;

    [Column("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [Column("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [Column("principal_id")]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("capability_id")]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>需要審批的原因(policy reason)</summary>
    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("status")]
    public int StatusValue { get; set; }

    [Ignore]
    public ApprovalStatus Status
    {
        get => (ApprovalStatus)StatusValue;
        set => StatusValue = (int)value;
    }

    /// <summary>審批層級:User(使用者本人)或 Admin(管理員,全域)</summary>
    [Column("approver_tier")]
    public int ApproverTierValue { get; set; }

    [Ignore]
    public ApproverTier ApproverTier
    {
        get => (ApproverTier)ApproverTierValue;
        set => ApproverTierValue = (int)value;
    }

    /// <summary>User 層時,有權核准者(即發起此動作的使用者 principal id)</summary>
    [Column("owner_principal_id")]
    public string OwnerPrincipalId { get; set; } = string.Empty;

    /// <summary>審批者(管理員)principal id;未決為空</summary>
    [Column("approver_id")]
    public string ApproverId { get; set; } = string.Empty;

    /// <summary>核准/駁回理由</summary>
    [Column("decision_reason")]
    public string DecisionReason { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>裁決時間;未決為 MinValue</summary>
    [Column("decided_at")]
    public DateTime DecidedAt { get; set; } = DateTime.MinValue;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("trace_id")]
    public string TraceId { get; set; } = string.Empty;
}
