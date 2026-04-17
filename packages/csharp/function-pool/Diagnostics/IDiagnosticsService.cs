namespace FunctionPool.Diagnostics;

/// <summary>
/// Runs diagnostic scans across the container runtime, managed containers, and registered workers.
/// Results are returned as a <see cref="DiagnosticReport"/> — no side effects.
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// Perform a full diagnostic scan:
    /// <list type="bullet">
    ///   <item>Container runtime availability</item>
    ///   <item>Container state anomalies (Failed, stuck-Stopping)</item>
    ///   <item>Log scanning for ERROR/EXCEPTION/WARN patterns</item>
    ///   <item>Worker heartbeat staleness</item>
    /// </list>
    /// </summary>
    Task<DiagnosticReport> ScanAsync(CancellationToken ct = default);
}
