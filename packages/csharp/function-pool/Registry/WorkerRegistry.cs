using System.Collections.Concurrent;
using FunctionPool.Models;
using FunctionPool.Network;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Registry;

/// <summary>
/// Worker 註冊中心 — ConcurrentDictionary 實作
///
/// 內部維護：
/// - _workers: workerId → (WorkerInfo, WorkerConnection) 映射
/// - _capabilityIndex: capabilityId → workerIds 索引
/// - _roundRobin: per-capability Round-Robin 計數器
/// </summary>
public class WorkerRegistry : IWorkerRegistry
{
    private readonly ILogger<WorkerRegistry> _logger;
    private readonly object _indexLock = new();

    // workerId → (WorkerInfo, WorkerConnection)
    private readonly ConcurrentDictionary<string, (WorkerInfo Info, WorkerConnection Connection)> _workers = new();

    // capabilityId → workerIds（需要 _indexLock 保護）
    private readonly Dictionary<string, List<string>> _capabilityIndex = new();

    // capabilityId → Round-Robin 計數器
    private readonly ConcurrentDictionary<string, int> _roundRobin = new();

    public WorkerRegistry(ILogger<WorkerRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// H-9 修復：Register 使用 _indexLock 保護整個操作，消除 TOCTOU 競態
    /// （ContainsKey → Deregister → TryAdd 不再拆分為三個非原子步驟）
    /// </summary>
    public bool Register(WorkerInfo worker, WorkerConnection connection)
    {
        if (string.IsNullOrEmpty(worker.WorkerId))
            return false;

        worker.State = WorkerState.Ready;
        worker.ConnectedAt = DateTime.UtcNow;
        worker.LastHeartbeat = DateTime.UtcNow;

        lock (_indexLock)
        {
            // 原子操作：先移除舊的（若存在）→ 再新增
            if (_workers.TryRemove(worker.WorkerId, out var oldEntry))
            {
                oldEntry.Info.State = WorkerState.Disconnected;

                // 清除舊的能力索引
                foreach (var cap in oldEntry.Info.Capabilities)
                {
                    if (_capabilityIndex.TryGetValue(cap, out var oldList))
                    {
                        oldList.Remove(worker.WorkerId);
                        if (oldList.Count == 0)
                            _capabilityIndex.Remove(cap);
                    }
                }
            }

            // 直接設值（不用 TryAdd，因 lock 內已保證無競態）
            _workers[worker.WorkerId] = (worker, connection);

            // 更新能力索引
            foreach (var cap in worker.Capabilities)
            {
                if (!_capabilityIndex.TryGetValue(cap, out var list))
                {
                    list = new List<string>();
                    _capabilityIndex[cap] = list;
                }
                if (!list.Contains(worker.WorkerId))
                    list.Add(worker.WorkerId);
            }
        }

        _logger.LogInformation(
            "Worker registered: {WorkerId} capabilities=[{Caps}] maxConcurrent={Max} endpoint={Ep}",
            worker.WorkerId, string.Join(", ", worker.Capabilities),
            worker.MaxConcurrent, worker.RemoteEndpoint);

        return true;
    }

    public bool Deregister(string workerId)
    {
        lock (_indexLock)
        {
            if (!_workers.TryRemove(workerId, out var entry))
                return false;

            entry.Info.State = WorkerState.Disconnected;

            // 清除能力索引
            foreach (var cap in entry.Info.Capabilities)
            {
                if (_capabilityIndex.TryGetValue(cap, out var list))
                {
                    list.Remove(workerId);
                    if (list.Count == 0)
                        _capabilityIndex.Remove(cap);
                }
            }
        }

        _logger.LogInformation("Worker deregistered: {WorkerId}", workerId);
        return true;
    }

    public WorkerConnection? GetAvailableWorker(string capabilityId)
    {
        List<string> workerIds;
        lock (_indexLock)
        {
            if (!_capabilityIndex.TryGetValue(capabilityId, out var list) || list.Count == 0)
                return null;
            workerIds = new List<string>(list); // snapshot
        }

        // Round-Robin 選取
        var counter = _roundRobin.AddOrUpdate(capabilityId, 0, (_, v) => v + 1);
        var count = workerIds.Count;

        for (int i = 0; i < count; i++)
        {
            var idx = (counter + i) % count;
            var workerId = workerIds[idx];

            if (_workers.TryGetValue(workerId, out var entry) && entry.Info.IsAvailable)
            {
                return entry.Connection;
            }
        }

        return null;
    }

    public List<WorkerInfo> GetWorkersByCapability(string capabilityId)
    {
        List<string> workerIds;
        lock (_indexLock)
        {
            if (!_capabilityIndex.TryGetValue(capabilityId, out var list))
                return new List<WorkerInfo>();
            workerIds = new List<string>(list);
        }

        // L-6 修復：使用單一 TryGetValue 防止 Where/Select 間的 TOCTOU 競態
        var result = new List<WorkerInfo>();
        foreach (var id in workerIds)
        {
            if (_workers.TryGetValue(id, out var entry))
                result.Add(entry.Info);
        }
        return result;
    }

    public List<WorkerInfo> GetAllWorkers()
    {
        return _workers.Values.Select(w => w.Info).ToList();
    }

    public int GetAvailableCount(string capabilityId)
    {
        return GetWorkersByCapability(capabilityId)
            .Count(w => w.IsAvailable);
    }

    public bool HasAvailableWorker(string capabilityId)
    {
        List<string> workerIds;
        lock (_indexLock)
        {
            if (!_capabilityIndex.TryGetValue(capabilityId, out var list))
                return false;
            workerIds = new List<string>(list);
        }

        return workerIds.Any(id =>
            _workers.TryGetValue(id, out var entry) && entry.Info.IsAvailable);
    }

    public void UpdateHeartbeat(string workerId)
    {
        if (_workers.TryGetValue(workerId, out var entry))
        {
            entry.Info.LastHeartbeat = DateTime.UtcNow;
        }
    }

    public void IncrementActiveTask(string workerId)
    {
        if (_workers.TryGetValue(workerId, out var entry))
        {
            Interlocked.Increment(ref entry.Info.ActiveTasks);
            // 檢查是否超過並發上限 → 標記 Busy
            if (entry.Info.ActiveTasks >= entry.Info.MaxConcurrent)
                entry.Info.State = WorkerState.Busy;
        }
    }

    public void DecrementActiveTask(string workerId)
    {
        if (_workers.TryGetValue(workerId, out var entry))
        {
            var newVal = Interlocked.Decrement(ref entry.Info.ActiveTasks);
            if (newVal < 0)
                Interlocked.Exchange(ref entry.Info.ActiveTasks, 0);

            // 恢復 Ready
            if (entry.Info.State == WorkerState.Busy && entry.Info.ActiveTasks < entry.Info.MaxConcurrent)
                entry.Info.State = WorkerState.Ready;
        }
    }

    public void SetWorkerState(string workerId, WorkerState state)
    {
        if (_workers.TryGetValue(workerId, out var entry))
        {
            entry.Info.State = state;
        }
    }
}
