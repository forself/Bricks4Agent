using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 宣告式執行請求 —— 核心 PEP 裁決對象
/// 含明確狀態機：received → validated → allowed → dispatched → succeeded/failed
/// UNIQUE CONSTRAINT: (task_id, idempotency_key) 防重複執行
/// </summary>
[Table("execution_requests")]
public class ExecutionRequest
{
    [Key(AutoIncrement = false)]
    [Column("request_id")]
    public string RequestId { get; set; } = string.Empty;

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

    /// <summary>使用者意圖描述</summary>
    [Column("intent")]
    [MaxLength(1000)]
    public string Intent { get; set; } = string.Empty;

    /// <summary>請求 payload（JSON）</summary>
    [Column("request_payload")]
    public string RequestPayload { get; set; } = "{}";

    [Column("execution_state")]
    public int ExecutionStateValue { get; set; }

    [Ignore]
    public ExecutionState ExecutionState
    {
        get => (ExecutionState)ExecutionStateValue;
        set => ExecutionStateValue = (int)value;
    }

    [Column("policy_decision")]
    public int? PolicyDecisionValue { get; set; }

    [Ignore]
    public PolicyDecision? PolicyDecision
    {
        get => PolicyDecisionValue.HasValue ? (PolicyDecision)PolicyDecisionValue.Value : null;
        set => PolicyDecisionValue = value.HasValue ? (int)value.Value : null;
    }

    /// <summary>政策裁決原因</summary>
    [Column("policy_reason")]
    [MaxLength(2000)]
    public string? PolicyReason { get; set; }

    /// <summary>執行結果 payload（JSON）</summary>
    [Column("result_payload")]
    public string? ResultPayload { get; set; }

    /// <summary>證據引用（稽核鏈指向）</summary>
    [Column("evidence_ref")]
    [MaxLength(500)]
    public string? EvidenceRef { get; set; }

    [Column("trace_id")]
    [Required]
    public string TraceId { get; set; } = string.Empty;

    /// <summary>冪等鍵（同 task_id + idempotency_key 不重複執行）</summary>
    [Column("idempotency_key")]
    [MaxLength(200)]
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
