namespace FunctionPool.Container;

/// <summary>
/// Contract for container lifecycle management.
/// The broker uses this to spawn/stop worker containers on demand.
/// </summary>
public interface IContainerManager
{
    /// <summary>Spawn a new hardened worker container</summary>
    /// <param name="request">Worker type, id and the broker-composed environment</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Container ID assigned by Docker/Podman</returns>
    Task<string> SpawnWorkerAsync(ContainerSpawnRequest request, CancellationToken ct = default);

    /// <summary>Stop and remove a managed container (including its anonymous volumes)</summary>
    Task StopWorkerAsync(string containerId, CancellationToken ct = default);

    /// <summary>List all managed containers</summary>
    Task<List<ManagedContainer>> ListManagedAsync(CancellationToken ct = default);

    /// <summary>Get container logs (last N lines)</summary>
    Task<string> GetLogsAsync(string containerId, int tailLines = 50, CancellationToken ct = default);

    /// <summary>Check if the container runtime is available</summary>
    Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default);

    /// <summary>Real-time resource usage for all running managed containers (docker stats).</summary>
    Task<List<ContainerStats>> GetStatsAsync(CancellationToken ct = default);
}
