using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 外部觀測服務 —— 觀測事件記錄 + Correlation 查詢
///
/// 觀測事件與 AuditEvent 不同：
/// - AuditEvent：系統內部操作的追加式記錄（不可變 hash chain）
/// - ObservationEvent：外部/內部觀測的實際狀態（用於偏差偵測 + Reconciliation）
///
/// Correlation Schema：plan_id + node_id + request_id + trace_id + worker_id
/// 確保在因果鏈中的每個觀測點都能追溯完整上下文。
/// </summary>
public interface IObservationService
{
    /// <summary>記錄觀測事件</summary>
    ObservationEvent Record(ObservationEvent observation);

    /// <summary>查詢特定 trace 的所有觀測（L-4 修復：加入 LIMIT 分頁）</summary>
    List<ObservationEvent> GetByTrace(string traceId, int limit = 200, int offset = 0);

    /// <summary>查詢特定 plan 的所有觀測（L-4 修復：加入 LIMIT 分頁）</summary>
    List<ObservationEvent> GetByPlan(string planId, int limit = 200, int offset = 0);

    /// <summary>查詢特定 node 的觀測（L-4 修復：加入 LIMIT 分頁）</summary>
    List<ObservationEvent> GetByNode(string nodeId, int limit = 200, int offset = 0);

    /// <summary>查詢指定時間範圍內的高嚴重度觀測（L-4 修復：加入 LIMIT 分頁）</summary>
    List<ObservationEvent> GetAlerts(DateTime since, ObservationSeverity minSeverity, int limit = 200, int offset = 0);
}
