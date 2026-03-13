namespace FunctionPool.Models;

/// <summary>
/// Worker 狀態枚舉
/// </summary>
public enum WorkerState
{
    /// <summary>正在連線/註冊中</summary>
    Connecting,

    /// <summary>就緒，可接收分派</summary>
    Ready,

    /// <summary>忙碌中（ActiveTasks >= MaxConcurrent）</summary>
    Busy,

    /// <summary>正在排空（不接受新任務，等待現有任務完成後斷線）</summary>
    Draining,

    /// <summary>已斷線</summary>
    Disconnected
}
