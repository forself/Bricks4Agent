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
