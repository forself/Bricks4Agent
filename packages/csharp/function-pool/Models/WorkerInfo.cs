namespace FunctionPool.Models;

/// <summary>
/// Worker 註冊資訊 + 運行時狀態
/// </summary>
public class WorkerInfo
{
    /// <summary>Worker 唯一識別（如 "wkr_01HXYZ..."）</summary>
    public string WorkerId { get; set; } = string.Empty;

    /// <summary>此 Worker 支援的能力列表（如 ["file.read", "file.list"]）</summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>最大並發任務數</summary>
    public int MaxConcurrent { get; set; } = 4;

    /// <summary>當前執行中任務數（public field 以支援 Interlocked）</summary>
    public int ActiveTasks;

    /// <summary>Worker 狀態</summary>
    public WorkerState State { get; set; } = WorkerState.Connecting;

    /// <summary>連線建立時間</summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最後心跳時間</summary>
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    /// <summary>遠端端點（除錯用）</summary>
    public string? RemoteEndpoint { get; set; }

    /// <summary>Worker 是否可用（Ready 且有並發餘量）</summary>
    public bool IsAvailable => State == WorkerState.Ready && ActiveTasks < MaxConcurrent;
}
