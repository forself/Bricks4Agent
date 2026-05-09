using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 追加式稽核服務 —— per-trace hash chain + 只 INSERT 不 UPDATE/DELETE
/// </summary>
public interface IAuditService
{
    /// <summary>記錄稽核事件（同 trace 序列化寫入）</summary>
    AuditEvent RecordEvent(
        string traceId,
        string eventType,
        string? principalId = null,
        string? taskId = null,
        string? sessionId = null,
        string? resourceRef = null,
        string details = "{}");

    /// <summary>查詢某 trace 的完整事件鏈</summary>
    List<AuditEvent> GetTraceEvents(string traceId);

    /// <summary>驗證 trace hash chain 完整性</summary>
    bool VerifyTraceIntegrity(string traceId);

    /// <summary>查詢稽核事件（分頁）</summary>
    List<AuditEvent> QueryEvents(string? eventType = null, string? principalId = null,
        string? taskId = null, int offset = 0, int limit = 50);

    /// <summary>
    /// 列出最近 N 個 trace、做摘要（給 Tracing Dashboard 用）。
    /// 每筆回傳 trace_id + 起訖時間 + 事件數 + 大致狀態（SUCCEEDED/FAILED/IN_PROGRESS）。
    /// 細節請呼叫 GetTraceEvents(traceId)。
    /// </summary>
    List<TraceSummary> ListRecentTraces(string? principalId = null, string? capabilityId = null,
        int offset = 0, int limit = 50);

    /// <summary>
    /// 拓撲圖資料：最近 N 分鐘 (principal, capability) 呼叫次數聚合。
    /// 用於 sankey/桑基圖：principal → capability。
    /// </summary>
    List<TopologyEdge> GetTopology(int sinceMinutes = 60);
}

/// <summary>(principal, capability) 聚合邊——後端拿來畫 sankey。</summary>
public class TopologyEdge
{
    public string Principal { get; set; } = "(unknown)";
    public string Capability { get; set; } = "(unknown)";
    public int CallsTotal { get; set; }
    public int CallsSucceeded { get; set; }
    public int CallsFailed { get; set; }
}

/// <summary>單一 trace 的摘要資訊（dashboard 列表用，不含 hash）。</summary>
public class TraceSummary
{
    public string TraceId { get; set; } = string.Empty;
    public DateTime FirstAt { get; set; }
    public DateTime LastAt { get; set; }
    public long DurationMs { get; set; }
    public int EventCount { get; set; }
    public string? FirstEventType { get; set; }
    public string? LastEventType { get; set; }
    public string? PrincipalId { get; set; }
    public string? TaskId { get; set; }
    /// <summary>從 audit_events.resource_ref 取一筆（dispatcher 事件填的就是 capability_id）。</summary>
    public string? CapabilityId { get; set; }
    /// <summary>SUCCEEDED / FAILED / IN_PROGRESS——取自最後一筆事件的後綴判斷。</summary>
    public string Status { get; set; } = "IN_PROGRESS";
}
