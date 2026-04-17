namespace FunctionPool.Container;

/// <summary>
/// Real-time resource usage statistics for a single managed container.
/// Values are parsed from <c>docker stats --no-stream --format "{{json .}}"</c> output.
/// </summary>
public class ContainerStats
{
    /// <summary>Short container ID (12 chars)</summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>Container name as assigned by Docker/Podman</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>CPU usage percentage (0–100+, can exceed 100 on multi-core)</summary>
    public double CpuPercent { get; set; }

    /// <summary>Memory in use, in bytes</summary>
    public long MemoryUsageBytes { get; set; }

    /// <summary>Memory limit imposed on the container, in bytes</summary>
    public long MemoryLimitBytes { get; set; }

    /// <summary>Memory usage as percentage of limit</summary>
    public double MemoryPercent { get; set; }

    /// <summary>Total bytes received over the network</summary>
    public long NetworkInputBytes { get; set; }

    /// <summary>Total bytes sent over the network</summary>
    public long NetworkOutputBytes { get; set; }

    /// <summary>Total bytes read from block devices</summary>
    public long BlockReadBytes { get; set; }

    /// <summary>Total bytes written to block devices</summary>
    public long BlockWriteBytes { get; set; }

    /// <summary>UTC timestamp when these stats were collected</summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
