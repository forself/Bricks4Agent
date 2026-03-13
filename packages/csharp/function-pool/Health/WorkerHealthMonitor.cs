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
/// </summary>
public class WorkerHealthMonitor : IDisposable
{
    private readonly IWorkerRegistry _registry;
    private readonly PoolConfig _config;
    private readonly ILogger<WorkerHealthMonitor> _logger;

    private Timer? _timer;
    private volatile bool _disposed;

    public WorkerHealthMonitor(
        IWorkerRegistry registry,
        PoolConfig config,
        ILogger<WorkerHealthMonitor> logger)
    {
        _registry = registry;
        _config = config;
        _logger = logger;
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

    /// <summary>健康檢查 tick</summary>
    private void CheckHealth(object? state)
    {
        if (_disposed) return;

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
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
