namespace FunctionPool.Container;

/// <summary>
/// No-op implementation when container management is disabled.
/// All operations return empty/false — endpoints still resolve without error.
/// </summary>
public class NoOpContainerManager : IContainerManager
{
    public Task<string> SpawnWorkerAsync(
        string workerType, string workerId,
        Dictionary<string, string>? envOverrides = null,
        CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "Container management is not enabled. Set FunctionPool:ContainerManager:Enabled=true.");
    }

    public Task StopWorkerAsync(string containerId, CancellationToken ct = default)
    {
        throw new InvalidOperationException("Container management is not enabled.");
    }

    public Task<List<ManagedContainer>> ListManagedAsync(CancellationToken ct = default)
        => Task.FromResult(new List<ManagedContainer>());

    public Task<string> GetLogsAsync(string containerId, int tailLines = 50, CancellationToken ct = default)
        => Task.FromResult("Container management is not enabled.");

    public Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(false);
}
