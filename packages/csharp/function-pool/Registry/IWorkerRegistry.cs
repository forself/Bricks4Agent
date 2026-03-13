using FunctionPool.Models;
using FunctionPool.Network;

namespace FunctionPool.Registry;

/// <summary>
/// Worker 註冊中心介面
///
/// 職責：
/// 1. 註冊/註銷 Worker
/// 2. 按 capability_id 查詢可用 Worker
/// 3. Round-Robin 負載均衡
/// 4. 追蹤 Worker 狀態
/// </summary>
public interface IWorkerRegistry
{
    /// <summary>註冊 Worker</summary>
    bool Register(WorkerInfo worker, WorkerConnection connection);

    /// <summary>註銷 Worker</summary>
    bool Deregister(string workerId);

    /// <summary>取得可用 Worker 連線（Round-Robin）</summary>
    WorkerConnection? GetAvailableWorker(string capabilityId);

    /// <summary>取得指定能力的所有 Worker 資訊</summary>
    List<WorkerInfo> GetWorkersByCapability(string capabilityId);

    /// <summary>取得所有已註冊 Worker</summary>
    List<WorkerInfo> GetAllWorkers();

    /// <summary>取得指定能力的可用 Worker 數量</summary>
    int GetAvailableCount(string capabilityId);

    /// <summary>是否有指定能力的可用 Worker</summary>
    bool HasAvailableWorker(string capabilityId);

    /// <summary>更新 Worker 心跳</summary>
    void UpdateHeartbeat(string workerId);

    /// <summary>增加 Worker 活躍任務數</summary>
    void IncrementActiveTask(string workerId);

    /// <summary>減少 Worker 活躍任務數</summary>
    void DecrementActiveTask(string workerId);

    /// <summary>標記 Worker 為指定狀態</summary>
    void SetWorkerState(string workerId, WorkerState state);
}
