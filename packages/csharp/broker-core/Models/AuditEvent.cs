using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 追加式稽核事件 —— per-trace hash chain 防篡改
/// 只 INSERT 不 UPDATE/DELETE
/// UNIQUE CONSTRAINT: (trace_id, trace_seq)
/// </summary>
[Table("audit_events")]
public class AuditEvent
{
    /// <summary>自動遞增 ID（全域排序用）</summary>
    [Key(AutoIncrement = true)]
    [Column("event_id")]
    public long EventId { get; set; }

    [Column("trace_id")]
    [Required]
    public string TraceId { get; set; } = string.Empty;

    /// <summary>同 trace 內的序號（從 0 遞增）</summary>
    [Column("trace_seq")]
    public int TraceSeq { get; set; }

    /// <summary>事件類型（TASK_CREATED, SESSION_REGISTERED, EXECUTION_ALLOWED, KILL_SWITCH 等）</summary>
    [Column("event_type")]
    [MaxLength(100)]
    [Required]
    public string EventType { get; set; } = string.Empty;

    [Column("principal_id")]
    public string? PrincipalId { get; set; }

    [Column("task_id")]
    public string? TaskId { get; set; }

    [Column("session_id")]
    public string? SessionId { get; set; }

    /// <summary>資源引用（被操作的對象）</summary>
    [Column("resource_ref")]
    [MaxLength(500)]
    public string? ResourceRef { get; set; }

    /// <summary>原始 payload 的 SHA-256 摘要</summary>
    [Column("payload_digest")]
    [MaxLength(64)]
    public string PayloadDigest { get; set; } = string.Empty;

    /// <summary>前一筆事件（同 trace）的 hash（首筆 = "GENESIS"）</summary>
    [Column("previous_event_hash")]
    [MaxLength(64)]
    public string PreviousEventHash { get; set; } = "GENESIS";

    /// <summary>本筆事件的 hash = SHA256(previous_hash + serialized_data)</summary>
    [Column("event_hash")]
    [MaxLength(64)]
    [Required]
    public string EventHash { get; set; } = string.Empty;

    /// <summary>詳細資訊（JSON）</summary>
    [Column("details")]
    public string Details { get; set; } = "{}";

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
