using FunctionPool.Container;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Diagnostics;

/// <summary>
/// Diagnostic scan engine — checks runtime, containers, logs, and worker heartbeats.
///
/// Design notes:
/// - Read-only: no state mutation, safe to call concurrently
/// - Log scanning is limited to the last 100 lines and capped at 10 issues per container
///   to prevent flooding the report with repetitive noise
/// - Patterns are checked longest-first so that "exception" beats "error" when both match
/// </summary>
public class DiagnosticsService : IDiagnosticsService
{
    private readonly IContainerManager _containerMgr;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<DiagnosticsService> _logger;

    // Workers with no heartbeat for longer than this are flagged
    private static readonly TimeSpan StaleHeartbeatWarn  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleHeartbeatError = TimeSpan.FromSeconds(60);

    // Containers stuck in Stopping beyond this threshold are flagged
    private static readonly TimeSpan StuckStoppingThreshold = TimeSpan.FromMinutes(2);

    private const int LogTailLines       = 100;
    private const int MaxLogIssuesPerCt  = 10;
    private const int MaxMessageLength   = 260;

    // (substring, severity) — checked in this order; first match wins per line
    private static readonly (string Token, IssueSeverity Sev)[] LogPatterns =
    [
        ("exception",  IssueSeverity.Critical),
        ("fatal",      IssueSeverity.Critical),
        (" error ",    IssueSeverity.Error),
        ("[error]",    IssueSeverity.Error),
        ("[err]",      IssueSeverity.Error),
        ("unhandled",  IssueSeverity.Error),
        ("failed",     IssueSeverity.Error),
        ("failure",    IssueSeverity.Error),
        (" warn ",     IssueSeverity.Warning),
        ("[warn]",     IssueSeverity.Warning),
        ("[warning]",  IssueSeverity.Warning),
        ("warning:",   IssueSeverity.Warning),
    ];

    public DiagnosticsService(
        IContainerManager containerMgr,
        IWorkerRegistry registry,
        ILogger<DiagnosticsService> logger)
    {
        _containerMgr = containerMgr;
        _registry     = registry;
        _logger       = logger;
    }

    public async Task<DiagnosticReport> ScanAsync(CancellationToken ct = default)
    {
        var issues = new List<DiagnosticIssue>();
        var now    = DateTime.UtcNow;

        // ── 1. Container runtime ─────────────────────────────────────
        var runtimeOk = await _containerMgr.IsRuntimeAvailableAsync(ct);
        if (!runtimeOk)
        {
            issues.Add(Issue(IssueSeverity.Critical, "Runtime", "container-runtime",
                "Container runtime (Docker/Podman) is unavailable or not responding"));
        }

        // ── 2. Container state + log scan ────────────────────────────
        var containers = await _containerMgr.ListManagedAsync(ct);

        foreach (var c in containers)
        {
            switch (c.State)
            {
                case ContainerState.Failed:
                    issues.Add(Issue(IssueSeverity.Error, "Container", c.ContainerId,
                        $"Container {c.WorkerType}/{c.ContainerId} is in Failed state (spawned {c.SpawnedAt:HH:mm:ss UTC})"));
                    break;

                case ContainerState.Stopping
                    when (now - c.SpawnedAt) > StuckStoppingThreshold:
                    issues.Add(Issue(IssueSeverity.Warning, "Container", c.ContainerId,
                        $"Container {c.WorkerType}/{c.ContainerId} has been stopping for >{StuckStoppingThreshold.TotalMinutes:0} min"));
                    break;
            }

            if (c.State is ContainerState.Running or ContainerState.Starting)
            {
                var logIssues = await ScanLogsAsync(c.ContainerId, c.WorkerType, ct);
                issues.AddRange(logIssues);
            }
        }

        // ── 3. Worker heartbeat checks ────────────────────────────────
        var workers = _registry.GetAllWorkers();

        foreach (var w in workers)
        {
            var staleness = now - w.LastHeartbeat;

            if (staleness >= StaleHeartbeatError)
            {
                issues.Add(Issue(IssueSeverity.Error, "Worker", w.WorkerId,
                    $"Worker {w.WorkerId} heartbeat lost ({(int)staleness.TotalSeconds}s, state: {w.State})"));
            }
            else if (staleness >= StaleHeartbeatWarn)
            {
                issues.Add(Issue(IssueSeverity.Warning, "Worker", w.WorkerId,
                    $"Worker {w.WorkerId} heartbeat stale ({(int)staleness.TotalSeconds}s, state: {w.State})"));
            }

            if (w.State == WorkerState.Disconnected)
            {
                issues.Add(Issue(IssueSeverity.Info, "Worker", w.WorkerId,
                    $"Worker {w.WorkerId} is disconnected (may be draining)"));
            }
        }

        // Sort: Critical first, then Error, Warning, Info
        issues.Sort((a, b) => a.Severity.CompareTo(b.Severity));

        _logger.LogDebug(
            "Diagnostic scan complete: {Total} issues ({C} critical, {E} error, {W} warning)",
            issues.Count,
            issues.Count(i => i.Severity == IssueSeverity.Critical),
            issues.Count(i => i.Severity == IssueSeverity.Error),
            issues.Count(i => i.Severity == IssueSeverity.Warning));

        return new DiagnosticReport
        {
            ScannedAt          = now,
            RuntimeAvailable   = runtimeOk,
            TotalContainers    = containers.Count,
            RunningContainers  = containers.Count(c => c.State == ContainerState.Running),
            TotalWorkers       = workers.Count,
            ReadyWorkers       = workers.Count(w => w.State == WorkerState.Ready),
            Issues             = issues,
        };
    }

    private async Task<List<DiagnosticIssue>> ScanLogsAsync(
        string containerId, string workerType, CancellationToken ct)
    {
        var issues = new List<DiagnosticIssue>();
        try
        {
            var logs = await _containerMgr.GetLogsAsync(containerId, LogTailLines, ct);
            foreach (var raw in logs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (issues.Count >= MaxLogIssuesPerCt) break;

                var line = raw.Trim();
                if (line.Length < 10) continue;

                IssueSeverity? sev = null;
                foreach (var (token, severity) in LogPatterns)
                {
                    if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        sev = severity;
                        break;
                    }
                }
                if (sev is null) continue;

                var msg = line.Length > MaxMessageLength
                    ? line[..MaxMessageLength] + "…"
                    : line;

                issues.Add(Issue(sev.Value, "ContainerLog", containerId,
                    $"[{workerType}] {msg}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not scan logs for {Container}: {Msg}", containerId, ex.Message);
        }
        return issues;
    }

    private static DiagnosticIssue Issue(
        IssueSeverity sev, string category, string entityId, string message)
        => new()
        {
            Severity   = sev,
            Category   = category,
            EntityId   = entityId,
            Message    = message,
            DetectedAt = DateTime.UtcNow,
        };
}
