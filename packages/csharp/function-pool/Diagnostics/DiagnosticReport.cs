namespace FunctionPool.Diagnostics;

/// <summary>
/// Aggregated result of one diagnostic scan pass.
/// Contains system-wide health metrics and a flat list of all detected issues.
/// </summary>
public class DiagnosticReport
{
    /// <summary>UTC time the scan completed</summary>
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the container runtime (Docker/Podman) responded</summary>
    public bool RuntimeAvailable { get; set; }

    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public int TotalWorkers { get; set; }
    public int ReadyWorkers { get; set; }

    /// <summary>All detected issues, ordered by severity (Critical first)</summary>
    public List<DiagnosticIssue> Issues { get; set; } = [];

    // ── Derived counts (computed, not stored) ──
    public int CriticalCount => Issues.Count(i => i.Severity == IssueSeverity.Critical);
    public int ErrorCount    => Issues.Count(i => i.Severity == IssueSeverity.Error);
    public int WarningCount  => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    public bool Healthy      => Issues.All(i => i.Severity == IssueSeverity.Info);
}
