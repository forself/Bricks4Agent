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
    /// 預設僅回傳「至少有 DISPATCH_* / EXECUTION_* 事件」的 trace；
    /// includeHttp=true 才會包進 ASP.NET HTTP middleware 寫的 W3C trace。
    /// </summary>
    List<TraceSummary> ListRecentTraces(string? principalId = null, string? capabilityId = null,
        int offset = 0, int limit = 50, bool includeHttp = false);

    /// <summary>
    /// 拓撲圖資料：最近 N 分鐘 (principal, capability) 呼叫次數聚合。
    /// 用於 sankey/桑基圖：principal → capability。
    /// </summary>
    List<TopologyEdge> GetTopology(int sinceMinutes = 60);

    /// <summary>
    /// 每 capability 的延遲統計（p50 / p95 / p99 / max）+ 分布 bucket。
    /// 從 PoolDispatcher 寫進 audit_events.details 的 duration_ms 欄位反推。
    /// </summary>
    List<CapabilityLatencyStats> GetLatencyStats(int sinceMinutes = 60);
}

/// <summary>單一 capability 的延遲統計 + histogram bucket。</summary>
public class CapabilityLatencyStats
{
    public string CapabilityId { get; set; } = string.Empty;
    public int Calls { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public long P50Ms { get; set; }
    public long P95Ms { get; set; }
    public long P99Ms { get; set; }
    public long MaxMs { get; set; }
    public long AvgMs { get; set; }
    /// <summary>分布 bucket：[(label, count), ...]，固定 7 個 bucket（&lt;10/10-50/50-100/100-500/500-1k/1-5k/&gt;5k ms）。</summary>
    public List<LatencyBucket> Distribution { get; set; } = new();
}

public class LatencyBucket
{
    public string Label { get; set; } = string.Empty;
    public long LowerMs { get; set; }
    public long UpperMs { get; set; }   // long.MaxValue = unbounded
    public int Count { get; set; }
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
