namespace FunctionPool.Diagnostics;

/// <summary>Severity of a detected diagnostic issue</summary>
public enum IssueSeverity
{
    Critical,  // System cannot function (runtime down, fatal crash)
    Error,     // Container failed, exception in logs, heartbeat lost
    Warning,   // Stale heartbeat, long-stopping container, log warnings
    Info,      // Informational (disconnected worker during graceful drain)
}

/// <summary>
/// A single problem detected during a diagnostic scan.
/// Produced by <see cref="DiagnosticsService"/> and returned in a <see cref="DiagnosticReport"/>.
/// </summary>
public class DiagnosticIssue
{
    /// <summary>How severe the problem is</summary>
    public IssueSeverity Severity { get; set; }

    /// <summary>
    /// Source category: "Runtime", "Container", "ContainerLog", "Worker"
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Container ID, worker ID, or "container-runtime"</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Human-readable description of the issue (max ~260 chars)</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the issue was detected during this scan</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
