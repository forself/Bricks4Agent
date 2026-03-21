namespace FunctionPool.Container;

/// <summary>
/// Tracked container instance spawned by the broker
/// </summary>
public class ManagedContainer
{
    public string ContainerId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public string WorkerType { get; set; } = string.Empty;
    public string ImageName { get; set; } = string.Empty;
    public DateTime SpawnedAt { get; set; } = DateTime.UtcNow;
    public ContainerState State { get; set; } = ContainerState.Starting;
}

public enum ContainerState
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed
}
