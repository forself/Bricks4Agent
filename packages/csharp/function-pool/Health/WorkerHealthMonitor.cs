using System.Text.Json;
using BrokerCore;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Health;

/// <summary>
/// Worker 健康監控器
///
/// 職責：
/// 1. 定時掃描所有 Worker 的心跳時間
/// 2. 超過 HeartbeatTimeout 未收到 PING → 標記 Disconnected
/// 3. 從 Registry 移除已斷線 Worker
/// 4. 發射 WORKER_HEARTBEAT_LOST 觀測事件（Phase 4）
/// </summary>
public class WorkerHealthMonitor : IDisposable
{
    private readonly IWorkerRegistry _registry;
    private readonly PoolConfig _config;
    private readonly ILogger<WorkerHealthMonitor> _logger;
    private readonly IObservationService? _observationService;

    private Timer? _timer;
    private volatile bool _disposed;
    // H-11 修復：防止 timer callback 重入（CheckHealth 跑超過 interval 時避免重疊）
    private int _checkHealthRunning;

    public WorkerHealthMonitor(
        IWorkerRegistry registry,
        PoolConfig config,
        ILogger<WorkerHealthMonitor> logger,
        IObservationService? observationService = null)
    {
        _registry = registry;
        _config = config;
        _logger = logger;
        _observationService = observationService;
    }

    /// <summary>啟動健康監控</summary>
    public void Start()
    {
        _timer = new Timer(
            CheckHealth,
            null,
            _config.HealthCheckInterval,
            _config.HealthCheckInterval);

        _logger.LogInformation(
            "Worker health monitor started (interval={Interval}s, timeout={Timeout}s)",
            _config.HealthCheckInterval.TotalSeconds,
            _config.HeartbeatTimeout.TotalSeconds);
    }

    /// <summary>停止健康監控</summary>
    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>健康檢查 tick（H-11 修復：Interlocked 防重入）</summary>
    private void CheckHealth(object? state)
    {
        if (_disposed) return;

        // H-11：若上一次 CheckHealth 仍在執行，跳過本次（防止 timer overlap）
        if (Interlocked.CompareExchange(ref _checkHealthRunning, 1, 0) != 0)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var workers = _registry.GetAllWorkers();
            var timedOut = 0;

            foreach (var worker in workers)
            {
                if (worker.State == WorkerState.Disconnected)
                    continue;

                var elapsed = now - worker.LastHeartbeat;
                if (elapsed > _config.HeartbeatTimeout)
                {
                    _logger.LogWarning(
                        "Worker {WorkerId} heartbeat timeout ({Elapsed:F1}s > {Threshold:F1}s). " +
                        "Marking as disconnected.",
                        worker.WorkerId, elapsed.TotalSeconds,
                        _config.HeartbeatTimeout.TotalSeconds);

                    _registry.Deregister(worker.WorkerId);
                    timedOut++;

                    // Phase 4: 發射 WORKER_HEARTBEAT_LOST 觀測事件
                    _observationService?.Record(new ObservationEvent
                    {
                        ObservationId = IdGen.New("obs"),
                        Source = ObservationSource.Internal,
                        EventType = "WORKER_HEARTBEAT_LOST",
                        TraceId = IdGen.New("trace"),
                        WorkerId = worker.WorkerId,
                        ObservedState = JsonSerializer.Serialize(new
                        {
                            workerState = "Disconnected",
                            lastHeartbeat = worker.LastHeartbeat,
                            elapsedSeconds = elapsed.TotalSeconds,
                            capabilities = worker.Capabilities
                        }),
                        ExpectedState = JsonSerializer.Serialize(new
                        {
                            workerState = "Ready",
                            maxHeartbeatIntervalSeconds = _config.HeartbeatTimeout.TotalSeconds
                        }),
                        Severity = ObservationSeverity.Warning,
                        Details = JsonSerializer.Serialize(new
                        {
                            workerId = worker.WorkerId,
                            reason = $"Heartbeat timeout ({elapsed.TotalSeconds:F1}s > {_config.HeartbeatTimeout.TotalSeconds:F1}s)"
                        }),
                        ObservedAt = DateTime.UtcNow
                    });
                }
            }

            if (timedOut > 0)
            {
                _logger.LogInformation(
                    "Health check: {TimedOut} worker(s) timed out, {Remaining} remaining",
                    timedOut, workers.Count - timedOut);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in worker health check");
        }
        finally
        {
            Interlocked.Exchange(ref _checkHealthRunning, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
