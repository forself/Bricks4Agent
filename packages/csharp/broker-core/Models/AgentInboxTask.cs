using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Agent Inbox 任務 — MVP-1（2026-05-01）
///
/// 解決什麼：MVP-0 階段 agent spawn 起來只能跑啟動時的 hard-coded prompt，
/// 派一個任務給它需要重起容器。MVP-1 改成：dashboard / 任何呼叫者 push 一筆任務
/// 進這張表 → agent 容器 poll 出來 → 跑 LLM → 回填結果，整個過程不重啟容器。
///
/// 狀態流：pending → processing → done | failed。
/// 每筆任務一行，不做 SharedContextEntry 那種版本化（任務不可變、結果單向）。
/// </summary>
[Table("agent_inbox_tasks")]
public class AgentInboxTask
{
    [Key(AutoIncrement = false)]
    [Column("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [Column("agent_id")]
    [Required]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>每個 agent 各自單調遞增的序號，方便 dashboard 顯示「第幾號任務」</summary>
    [Column("seq")]
    public int Seq { get; set; }

    [Column("prompt")]
    [Required]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>pending | processing | done | failed</summary>
    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [Column("reply")]
    public string? Reply { get; set; }

    [Column("error")]
    public string? Error { get; set; }

    /// <summary>誰送進這筆任務（dashboard / 容器名 / external service）</summary>
    [Column("requested_by")]
    [MaxLength(100)]
    public string? RequestedBy { get; set; }

    /// <summary>冪等鍵（選填）：同 (agent_id, idempotency_key) 重複 push 回既有任務、不重建。
    /// 防 client 重試 / broker 重啟接手時重複建任務。UNIQUE index 見 BrokerDbInitializer。</summary>
    [Column("idempotency_key")]
    [MaxLength(120)]
    public string? IdempotencyKey { get; set; }

    [Column("model")]
    [MaxLength(80)]
    public string? Model { get; set; }

    [Column("eval_tokens")]
    public int? EvalTokens { get; set; }

    [Column("latency_ms")]
    public int? LatencyMs { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}
