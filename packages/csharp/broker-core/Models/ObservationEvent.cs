using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 外部觀測事件 —— Correlation Schema
///
/// 完整的關聯欄位：plan_id / node_id / request_id / trace_id / worker_id / principal_id
/// 用於因果鏈追蹤、偏差偵測、對帳（Phase 5 Reconciliation）。
/// </summary>
[Table("observation_events")]
public class ObservationEvent
{
    [Key(AutoIncrement = false)]
    [Column("observation_id")]
    public string ObservationId { get; set; } = string.Empty;

    /// <summary>觀測來源</summary>
    [Column("source")]
    public int SourceValue { get; set; }

    [Ignore]
    public ObservationSource Source
    {
        get => (ObservationSource)SourceValue;
        set => SourceValue = (int)value;
    }

    /// <summary>事件類型（EXECUTION_OBSERVED / STATE_DIVERGENCE / HEARTBEAT_LOST / ...）</summary>
    [Column("event_type")]
    [MaxLength(100)]
    [Required]
    public string EventType { get; set; } = string.Empty;

    // ── Correlation Schema ──

    /// <summary>關聯計畫（若有）</summary>
    [Column("plan_id")]
    public string? PlanId { get; set; }

    /// <summary>關聯節點（若有）</summary>
    [Column("node_id")]
    public string? NodeId { get; set; }

    /// <summary>關聯執行請求（若有）</summary>
    [Column("request_id")]
    public string? RequestId { get; set; }

    /// <summary>追蹤 ID</summary>
    [Column("trace_id")]
    [Required]
    public string TraceId { get; set; } = string.Empty;

    /// <summary>執行的 Worker（若有）</summary>
    [Column("worker_id")]
    public string? WorkerId { get; set; }

    /// <summary>相關主體</summary>
    [Column("principal_id")]
    public string? PrincipalId { get; set; }

    // ── 觀測內容 ──

    /// <summary>觀測到的實際狀態（JSON）</summary>
    [Column("observed_state")]
    public string ObservedState { get; set; } = "{}";

    /// <summary>預期狀態（JSON，用於偏差比對，Phase 5 Reconciliation）</summary>
    [Column("expected_state")]
    public string? ExpectedState { get; set; }

    /// <summary>嚴重度</summary>
    [Column("severity")]
    public int SeverityValue { get; set; }

    [Ignore]
    public ObservationSeverity Severity
    {
        get => (ObservationSeverity)SeverityValue;
        set => SeverityValue = (int)value;
    }

    /// <summary>詳細描述（JSON）</summary>
    [Column("details")]
    public string Details { get; set; } = "{}";

    /// <summary>觀測時間</summary>
    [Column("observed_at")]
    public DateTime ObservedAt { get; set; } = DateTime.UtcNow;
}
