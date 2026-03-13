using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 計畫服務 —— Plan/Node/Edge CRUD + DAG 驗證
///
/// 職責：
/// 1. Plan 建立/查詢/狀態更新
/// 2. Node 新增/查詢/狀態更新
/// 3. Edge 新增/查詢
/// 4. DAG 驗證（Kahn's algorithm：環路偵測 + 拓撲排序）
/// 5. 查詢就緒節點（所有入邊 fromNode 已 Succeeded）
/// 6. Checkpoint 管理
/// </summary>
public interface IPlanService
{
    // ── Plan CRUD ──

    /// <summary>建立計畫（Draft 狀態）</summary>
    Plan CreatePlan(string taskId, string submittedBy, string title, string? description);

    /// <summary>查詢計畫</summary>
    Plan? GetPlan(string planId);

    /// <summary>更新計畫狀態</summary>
    bool UpdatePlanState(string planId, PlanState newState);

    // ── Node 管理 ──

    /// <summary>新增節點</summary>
    PlanNode AddNode(string planId, string capabilityId, string intent,
                     string requestPayload, string? outputContextKey, int maxRetries);

    /// <summary>查詢計畫的所有節點（按 ordinal 排序）</summary>
    List<PlanNode> GetNodes(string planId);

    /// <summary>更新節點狀態</summary>
    bool UpdateNodeState(string nodeId, NodeState state, string? requestId);

    /// <summary>遞增節點重試計數</summary>
    bool IncrementRetryCount(string nodeId);

    // ── Edge 管理 ──

    /// <summary>新增邊</summary>
    PlanEdge AddEdge(string planId, string fromNodeId, string toNodeId,
                     EdgeType edgeType, string? contextKey, string? condition);

    /// <summary>查詢計畫的所有邊</summary>
    List<PlanEdge> GetEdges(string planId);

    /// <summary>查詢指定節點的入邊</summary>
    List<PlanEdge> GetIncomingEdges(string nodeId);

    // ── DAG 驗證 ──

    /// <summary>
    /// 驗證 DAG（Kahn's algorithm）：
    /// - 環路偵測
    /// - 計算拓撲排序序號（Ordinal）
    /// - 回傳 (true, null) 或 (false, error)
    /// </summary>
    (bool IsValid, string? Error) ValidateDag(string planId);

    // ── 就緒查詢 ──

    /// <summary>查詢就緒節點（所有入邊的 fromNode 都已 Succeeded）</summary>
    List<PlanNode> GetReadyNodes(string planId);

    // ── Checkpoint ──

    /// <summary>建立檢查點</summary>
    Checkpoint CreateCheckpoint(string planId, string nodeId, string snapshotRef);

    /// <summary>查詢計畫的所有檢查點</summary>
    List<Checkpoint> GetCheckpoints(string planId);
}
