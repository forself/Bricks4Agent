namespace BrokerCore.Models;

/// <summary>主體類型（僅紀錄，不判權）</summary>
public enum ActorType
{
    Human = 0,
    AI = 1,
    System = 2
}

/// <summary>風險等級</summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>任務狀態</summary>
public enum TaskState
{
    Created = 0,
    Assigned = 1,
    Active = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>政策裁決結果</summary>
public enum PolicyDecision
{
    Allow = 0,
    Deny = 1,
    RequireApproval = 2
}

/// <summary>執行請求狀態機</summary>
/// <remarks>
/// received → validated → allowed → dispatched → succeeded
///                     ↘ denied                 ↘ failed
/// </remarks>
public enum ExecutionState
{
    Received = 0,
    Validated = 1,
    Allowed = 2,
    Denied = 3,
    Dispatched = 4,
    Succeeded = 5,
    Failed = 6
}

/// <summary>Session 狀態</summary>
public enum SessionStatus
{
    Active = 0,
    Closed = 1,
    Revoked = 2
}

/// <summary>能力動作類型</summary>
public enum ActionType
{
    Read = 0,
    Write = 1,
    Execute = 2
}

/// <summary>撤權目標類型</summary>
public enum RevocationTargetType
{
    Session = 0,
    Grant = 1,
    Token = 2
}

/// <summary>實體狀態（通用）</summary>
public enum EntityStatus
{
    Active = 0,
    Disabled = 1,
    Deleted = 2
}

/// <summary>授予狀態</summary>
public enum GrantStatus
{
    Active = 0,
    Expired = 1,
    Revoked = 2,
    Exhausted = 3
}

// ── Phase 4: 因果工作流 ──

/// <summary>計畫狀態</summary>
/// <remarks>
/// Draft → Submitted → Running → Completed
///                   ↘ Paused    ↘ Failed
///                               ↘ Cancelled
/// </remarks>
public enum PlanState
{
    Draft = 0,
    Submitted = 1,
    Running = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}

/// <summary>節點狀態</summary>
/// <remarks>
/// Pending → Ready → Running → Succeeded
///                           ↘ Failed
///         ↘ Skipped
///         ↘ Cancelled
/// </remarks>
public enum NodeState
{
    Pending = 0,
    Ready = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Skipped = 5,
    Cancelled = 6
}

/// <summary>邊類型（DAG 因果依賴）</summary>
public enum EdgeType
{
    /// <summary>資料流：上游 output → SharedContext → 下游 input</summary>
    DataFlow = 0,
    /// <summary>控制流：純序列依賴（無資料傳遞）</summary>
    ControlFlow = 1,
    /// <summary>審批閘：需人工審批才放行</summary>
    ApprovalGate = 2
}

/// <summary>檢查點狀態</summary>
public enum CheckpointState
{
    Pending = 0,
    Captured = 1,
    Verified = 2,
    RolledBack = 3
}

// ── Phase 4: 外部觀測 ──

/// <summary>觀測來源</summary>
public enum ObservationSource
{
    /// <summary>系統內部自動觀測</summary>
    Internal = 0,
    /// <summary>外部觀測器報告</summary>
    External = 1,
    /// <summary>Agent/Worker 自我報告</summary>
    SelfReport = 2
}

/// <summary>觀測嚴重度</summary>
public enum ObservationSeverity
{
    Info = 0,
    Warning = 1,
    Alert = 2,
    Critical = 3
}
