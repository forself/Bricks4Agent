using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 待人工核准的 capability 派發請求。
///
/// 工作流程：
///   1. PoolDispatcher 收到一個被 ApprovalPolicy 標為 require_approval 的 capability
///   2. 寫一筆 status='pending' 進來、回呼叫者 Fail("approval_id=...")
///   3. Admin 在 dashboard 看待審清單、按 approve / reject
///   4. 呼叫者 retry 同一個 trace_id（或重 dispatch）→ PoolDispatcher 看到 approved 直接放行；
///      看到 rejected 回 Fail
///
/// 設計選擇 — 為什麼不阻塞等待：HTTP request 阻塞等 admin 點按鈕在 SaaS 場景不可行。
/// 改成「先回 pending、caller 自己 retry / 訂閱通知」可以撐到任何審核時長。
///
/// 列為 audit 鏈的補強——本表是核准 metadata、底層派發過程的審計仍然在 audit_events。
/// </summary>
[Table("approval_requests")]
public class ApprovalRequest
{
    [Key, MaxLength(64)]
    [Column("approval_id")]
    public string ApprovalId { get; set; } = string.Empty;

    /// <summary>關聯的 trace_id（同一條派發鏈、approve 後 caller 用同 trace_id 重派）</summary>
    [Column("trace_id")]
    [MaxLength(64)]
    [Required]
    public string TraceId { get; set; } = string.Empty;

    [Column("capability_id")]
    [MaxLength(80)]
    [Required]
    public string CapabilityId { get; set; } = string.Empty;

    [Column("route")]
    [MaxLength(80)]
    public string Route { get; set; } = string.Empty;

    /// <summary>原始 payload（JSON），admin 用來判斷批不批</summary>
    [Column("payload")]
    public string Payload { get; set; } = "{}";

    [Column("principal_id")]
    [MaxLength(64)]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("role")]
    [MaxLength(40)]
    public string Role { get; set; } = string.Empty;

    [Column("requested_at")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>"pending" / "approved" / "rejected" / "expired"</summary>
    [Column("status")]
    [MaxLength(20)]
    [Required]
    public string Status { get; set; } = "pending";

    [Column("decided_by")]
    [MaxLength(64)]
    public string? DecidedBy { get; set; }

    [Column("decided_at")]
    public DateTime? DecidedAt { get; set; }

    [Column("decision_reason")]
    [MaxLength(500)]
    public string? DecisionReason { get; set; }
}
