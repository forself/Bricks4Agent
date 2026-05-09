using System.Collections.Concurrent;
using FunctionPool.Container;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// Worker container 自動重啟服務。
///
/// Tick 每 30 秒掃一次 IContainerManager.ListManagedAsync()、把 state = Stopped / Failed
/// 的 container 用 exponential backoff 嘗試 StartWorkerAsync 重啟。
///
/// Backoff 階梯（attempts → wait）：
///   1 → 0s（立刻）
///   2 → 30s
///   3 → 2min
///   4 → 10min
///   5 → 30min
///   ≥6 → 放棄（標記 unrecoverable、不再重試直到 admin reset）
///
/// 一個 container 持續 Running 超過 5 分鐘 → reset 嘗試計數，下次再掛能從 attempt 1 開始。
///
/// 不處理「broker 主動斷線重連」這種 application-level flapping（trading-worker 的情況）——
/// 那種 container 仍然 Running、不在這個 service 的處理範圍。要解那個是 #broker-side
/// 的 disconnect reason logging + heartbeat protocol 校正。
/// </summary>
public class WorkerAutoRestartService : BackgroundService
{
    private readonly IContainerManager _containerMgr;
    private readonly ILogger<WorkerAutoRestartService> _logger;
    private readonly ConcurrentDictionary<string, RestartState> _state = new();

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ResetAfter   = TimeSpan.FromMinutes(5);

    /// <summary>Backoff 階梯（attempt 1-based）。≥ MaxAttempts → 放棄。</summary>
    private static readonly TimeSpan[] BackoffSteps = new[]
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
    };
    private const int MaxAttempts = 5;

    public WorkerAutoRestartService(
        IContainerManager containerMgr,
        ILogger<WorkerAutoRestartService> logger)
    {
        _containerMgr = containerMgr;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkerAutoRestart started, interval={Sec}s, max attempts={Max}",
            TickInterval.TotalSeconds, MaxAttempts);

        // 等 broker 整個 host 起來、避免 startup race（worker 自己重連 + restart service 同時搶）
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Auto-restart tick failed"); }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!await _containerMgr.IsRuntimeAvailableAsync(ct)) return;

        var containers = await _containerMgr.ListManagedAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var c in containers)
        {
            if (c.State == ContainerState.Running)
            {
                // Running 持續夠久 → reset 嘗試計數，給未來再掛時 fresh start
                if (_state.TryGetValue(c.ContainerId, out var s)
                    && now - s.LastAttemptAt > ResetAfter
                    && s.Attempts > 0)
                {
                    _logger.LogInformation(
                        "Container {Id} ({Type}) stable for {Min:F1}min, resetting restart counter (was {N})",
                        c.ContainerId[..12], c.WorkerType, (now - s.LastAttemptAt).TotalMinutes, s.Attempts);
                    _state.TryRemove(c.ContainerId, out _);
                }
                continue;
            }

            // 只處理 Stopped / Failed
            if (c.State != ContainerState.Stopped && c.State != ContainerState.Failed) continue;

            var st = _state.GetOrAdd(c.ContainerId, _ => new RestartState());

            if (st.Unrecoverable)
            {
                // 已放棄、繼續 skip 直到 admin reset
                continue;
            }

            // 計算下次 attempt 應該等到什麼時候
            var nextAttempt = st.Attempts == 0
                ? now  // 第一次立即
                : st.LastAttemptAt + BackoffSteps[Math.Min(st.Attempts, BackoffSteps.Length - 1)];

            if (now < nextAttempt) continue;  // 還在 backoff 期內

            if (st.Attempts >= MaxAttempts)
            {
                _logger.LogError(
                    "Container {Id} ({Type}) reached max restart attempts ({Max}). Marking unrecoverable.",
                    c.ContainerId[..12], c.WorkerType, MaxAttempts);
                st.Unrecoverable = true;
                st.LastError = $"Exceeded {MaxAttempts} restart attempts.";
                continue;
            }

            // 動手
            st.Attempts++;
            st.LastAttemptAt = now;
            _logger.LogWarning(
                "Auto-restarting container {Id} ({Type}) attempt {N}/{Max}, state was {State}",
                c.ContainerId[..12], c.WorkerType, st.Attempts, MaxAttempts, c.State);

            try
            {
                await _containerMgr.StartWorkerAsync(c.ContainerId, ct);
                st.LastError = null;
                _logger.LogInformation("Container {Id} restart attempt {N} succeeded",
                    c.ContainerId[..12], st.Attempts);
            }
            catch (Exception ex)
            {
                st.LastError = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                _logger.LogWarning(ex, "Container {Id} restart attempt {N} failed",
                    c.ContainerId[..12], st.Attempts);
            }
        }
    }

    /// <summary>取得目前所有 container 的 restart 狀態快照（給 admin endpoint 用）。</summary>
    public IReadOnlyList<RestartStateSnapshot> GetSnapshots()
        => _state.Select(kv => new RestartStateSnapshot
        {
            ContainerId    = kv.Key,
            Attempts       = kv.Value.Attempts,
            LastAttemptAt  = kv.Value.LastAttemptAt,
            Unrecoverable  = kv.Value.Unrecoverable,
            LastError      = kv.Value.LastError,
        }).ToList();

    /// <summary>Admin 手動清掉某 container 的 backoff（也清 unrecoverable flag）。</summary>
    public bool Reset(string containerId)
        => _state.TryRemove(containerId, out _);

    private class RestartState
    {
        public int Attempts { get; set; }
        public DateTime LastAttemptAt { get; set; }
        public bool Unrecoverable { get; set; }
        public string? LastError { get; set; }
    }
}

public class RestartStateSnapshot
{
    public string ContainerId { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime LastAttemptAt { get; set; }
    public bool Unrecoverable { get; set; }
    public string? LastError { get; set; }
}
