namespace FunctionPool.Container;

/// <summary>
/// Contract for container lifecycle management.
/// The broker uses this to spawn/stop worker containers on demand.
/// </summary>
public interface IContainerManager
{
    /// <summary>Spawn a new worker container of the given type</summary>
    /// <param name="workerType">Worker type key (e.g. "file-worker", "line-worker")</param>
    /// <param name="workerId">Unique worker ID assigned by broker</param>
    /// <param name="envOverrides">Additional/override environment variables</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Container ID assigned by Docker/Podman</returns>
    Task<string> SpawnWorkerAsync(
        string workerType,
        string workerId,
        Dictionary<string, string>? envOverrides = null,
        CancellationToken ct = default);

    /// <summary>Stop and remove a managed container</summary>
    Task StopWorkerAsync(string containerId, CancellationToken ct = default);

    /// <summary>List all managed containers</summary>
    Task<List<ManagedContainer>> ListManagedAsync(CancellationToken ct = default);

    /// <summary>Get container logs (last N lines)</summary>
    Task<string> GetLogsAsync(string containerId, int tailLines = 50, CancellationToken ct = default);

    /// <summary>Check if the container runtime is available</summary>
    Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default);
}
