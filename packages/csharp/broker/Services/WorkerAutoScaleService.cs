using System.Collections.Concurrent;
using FunctionPool.Container;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 容器自動擴縮服務（Auto-scale）。
///
/// 每 30s tick 一次：
///   1. 從 IWorkerRegistry + IContainerManager join 得到「每個 worker_type 的 utilization 樣本」
///      utilization = sum(active_tasks) / sum(max_concurrent)
///   2. 維護每 worker_type 最近 5 個樣本（slide window）
///   3. Scale-up：若最近 2 個樣本的 utilization 都 ≥ 0.8 且 container 數 &lt; MaxContainersPerType
///      → SpawnWorkerAsync(workerType)
///   4. Scale-down：若最近 5 個樣本 utilization 全為 0 且 container 數 &gt; 1
///      → StopWorkerAsync(oldest)
///
/// 設計細節：
/// - 不擴 broker 自己（只動 workers）
/// - SpawnWorkerAsync 一次只開一個、避免雪崩；下個 tick 看情況再加
/// - 每 worker_type 設冷卻期（spawn / stop 後 60s 不再動）避免 thrashing
/// - 沒設 ContainerConfig 的環境（NoOp manager）service 仍會啟動但 tick 是 no-op
/// </summary>
public class WorkerAutoScaleService : BackgroundService
{
    private readonly IWorkerRegistry _registry;
    private readonly IContainerManager _containerMgr;
    private readonly ContainerConfig? _containerConfig;
    private readonly ILogger<WorkerAutoScaleService> _logger;

    private readonly ConcurrentDictionary<string, AutoScaleState> _state = new();
    private readonly List<ScaleDecision> _decisions = new();
    private readonly object _decisionsLock = new();

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CooldownAfterAction = TimeSpan.FromSeconds(60);
    private const int SampleWindowSize = 5;
    private const double ScaleUpThreshold = 0.80;     // 80% utilization
    private const int ScaleUpRequireSamples = 2;      // 連 2 樣本超門檻才擴
    private const int MaxRecentDecisions = 50;

    public WorkerAutoScaleService(
        IWorkerRegistry registry,
        IContainerManager containerMgr,
        ContainerConfig containerConfig,
        ILogger<WorkerAutoScaleService> logger)
    {
        _registry = registry;
        _containerMgr = containerMgr;
        _containerConfig = containerConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WorkerAutoScale started, interval={Sec}s, scale-up threshold={Pct:P0}, max per type={Max}",
            TickInterval.TotalSeconds, ScaleUpThreshold, _containerConfig?.MaxContainersPerType ?? 0);

        // 等 broker 整個起來、避免 startup race
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Auto-scale tick failed"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (_containerConfig == null || _containerConfig.WorkerImages.Count == 0) return;
        if (!await _containerMgr.IsRuntimeAvailableAsync(ct)) return;

        var now = DateTime.UtcNow;
        var workers = _registry.GetAllWorkers();
        var containers = await _containerMgr.ListManagedAsync(ct);

        // 用 ManagedContainer 的 WorkerId → WorkerType 對照、把 WorkerInfo 也補上 type
        var workerTypeById = containers.ToDictionary(c => c.WorkerId, c => c.WorkerType);

        // 依 worker_type 分組計 utilization
        var grouped = workers
            .Where(w => workerTypeById.ContainsKey(w.WorkerId))
            .GroupBy(w => workerTypeById[w.WorkerId])
            .ToDictionary(g => g.Key, g => new
            {
                Workers = g.ToList(),
                ContainerCount = containers.Count(c => c.WorkerType == g.Key && c.State == ContainerState.Running),
                ActiveSum = g.Sum(w => w.ActiveTasks),
                MaxSum = g.Sum(w => w.MaxConcurrent),
            });

        foreach (var (type, info) in grouped)
        {
            var util = info.MaxSum == 0 ? 0.0 : (double)info.ActiveSum / info.MaxSum;
            var st = _state.GetOrAdd(type, _ => new AutoScaleState());

            // slide window
            st.Samples.Add(new UtilizationSample { At = now, Utilization = util,
                ActiveTasks = info.ActiveSum, MaxConcurrent = info.MaxSum,
                ContainerCount = info.ContainerCount });
            while (st.Samples.Count > SampleWindowSize) st.Samples.RemoveAt(0);

            // 冷卻期內不動作
            if (now - st.LastActionAt < CooldownAfterAction) continue;

            // Scale-up 判斷：最近 N 個樣本 utilization 全部超過閾值
            var recentHigh = st.Samples
                .TakeLast(ScaleUpRequireSamples)
                .Count(s => s.Utilization >= ScaleUpThreshold);
            var maxPerType = _containerConfig.MaxContainersPerType;

            if (recentHigh >= ScaleUpRequireSamples && info.ContainerCount < maxPerType)
            {
                await TryScaleUp(type, info.ContainerCount, util, ct);
                st.LastActionAt = now;
                continue;
            }

            // Scale-down 判斷：window 全滿、utilization 都 = 0、且 >1 個 container
            if (st.Samples.Count == SampleWindowSize
                && info.ContainerCount > 1
                && st.Samples.All(s => s.Utilization == 0))
            {
                await TryScaleDown(type, containers, ct);
                st.LastActionAt = now;
            }
        }
    }

    private async Task TryScaleUp(string workerType, int currentCount, double util, CancellationToken ct)
    {
        var newId = $"{workerType}-auto-{Guid.NewGuid():N}".Substring(0, Math.Min(40, workerType.Length + 12));
        try
        {
            var containerId = await _containerMgr.SpawnWorkerAsync(workerType, newId, null, ct);
            _logger.LogInformation(
                "Auto-scale UP: spawned {Type} (id={Cid}, current={Cur}, util={Util:P0})",
                workerType, containerId[..Math.Min(12, containerId.Length)], currentCount, util);
            RecordDecision(new ScaleDecision
            {
                At = DateTime.UtcNow,
                WorkerType = workerType,
                Action = "scale_up",
                Utilization = Math.Round(util, 3),
                ContainerCountBefore = currentCount,
                Result = "spawned: " + containerId[..Math.Min(12, containerId.Length)],
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-scale UP failed for {Type}", workerType);
            RecordDecision(new ScaleDecision
            {
                At = DateTime.UtcNow,
                WorkerType = workerType,
                Action = "scale_up",
                Utilization = Math.Round(util, 3),
                ContainerCountBefore = currentCount,
                Result = "error: " + (ex.Message.Length > 100 ? ex.Message[..100] : ex.Message),
            });
        }
    }

    private async Task TryScaleDown(string workerType, IReadOnlyList<ManagedContainer> containers, CancellationToken ct)
    {
        // 挑「最舊」的 Running container 停掉、保留新的（新的可能比較健康）
        var oldest = containers
            .Where(c => c.WorkerType == workerType && c.State == ContainerState.Running)
            .OrderBy(c => c.SpawnedAt)
            .FirstOrDefault();
        if (oldest == null) return;

        try
        {
            await _containerMgr.StopWorkerAsync(oldest.ContainerId, ct);
            _logger.LogInformation(
                "Auto-scale DOWN: stopped {Type} (id={Cid}, idle for window)",
                workerType, oldest.ContainerId[..Math.Min(12, oldest.ContainerId.Length)]);
            RecordDecision(new ScaleDecision
            {
                At = DateTime.UtcNow,
                WorkerType = workerType,
                Action = "scale_down",
                Utilization = 0,
                ContainerCountBefore = containers.Count(c => c.WorkerType == workerType && c.State == ContainerState.Running),
                Result = "stopped: " + oldest.ContainerId[..Math.Min(12, oldest.ContainerId.Length)],
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-scale DOWN failed for {Type}", workerType);
        }
    }

    private void RecordDecision(ScaleDecision d)
    {
        lock (_decisionsLock)
        {
            _decisions.Insert(0, d);
            if (_decisions.Count > MaxRecentDecisions) _decisions.RemoveRange(MaxRecentDecisions, _decisions.Count - MaxRecentDecisions);
        }
    }

    /// <summary>取得目前各 worker_type 的 sample window + 最近決策（給 admin endpoint）。</summary>
    public AutoScaleSnapshot GetSnapshot()
    {
        lock (_decisionsLock)
        {
            return new AutoScaleSnapshot
            {
                Enabled = _containerConfig != null && _containerConfig.WorkerImages.Count > 0,
                MaxPerType = _containerConfig?.MaxContainersPerType ?? 0,
                ScaleUpThreshold = ScaleUpThreshold,
                CooldownSeconds = (int)CooldownAfterAction.TotalSeconds,
                State = _state.ToDictionary(
                    kv => kv.Key,
                    kv => new AutoScaleStateSnapshot
                    {
                        WorkerType = kv.Key,
                        Samples = kv.Value.Samples.ToList(),
                        LastActionAt = kv.Value.LastActionAt,
                    }),
                RecentDecisions = _decisions.Take(MaxRecentDecisions).ToList(),
            };
        }
    }

    private class AutoScaleState
    {
        public List<UtilizationSample> Samples { get; } = new();
        public DateTime LastActionAt { get; set; } = DateTime.MinValue;
    }
}

public class UtilizationSample
{
    public DateTime At { get; set; }
    public double Utilization { get; set; }
    public int ActiveTasks { get; set; }
    public int MaxConcurrent { get; set; }
    public int ContainerCount { get; set; }
}

public class ScaleDecision
{
    public DateTime At { get; set; }
    public string WorkerType { get; set; } = "";
    public string Action { get; set; } = "";   // "scale_up" / "scale_down"
    public double Utilization { get; set; }
    public int ContainerCountBefore { get; set; }
    public string Result { get; set; } = "";
}

public class AutoScaleSnapshot
{
    public bool Enabled { get; set; }
    public int MaxPerType { get; set; }
    public double ScaleUpThreshold { get; set; }
    public int CooldownSeconds { get; set; }
    public Dictionary<string, AutoScaleStateSnapshot> State { get; set; } = new();
    public List<ScaleDecision> RecentDecisions { get; set; } = new();
}

public class AutoScaleStateSnapshot
{
    public string WorkerType { get; set; } = "";
    public List<UtilizationSample> Samples { get; set; } = new();
    public DateTime LastActionAt { get; set; }
}
