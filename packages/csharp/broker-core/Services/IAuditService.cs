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
}
