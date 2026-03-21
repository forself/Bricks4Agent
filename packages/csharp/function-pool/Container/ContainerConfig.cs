namespace FunctionPool.Container;

/// <summary>
/// Container orchestration configuration
/// </summary>
public class ContainerConfig
{
    /// <summary>Container runtime: "docker" or "podman"</summary>
    public string Runtime { get; set; } = "docker";

    /// <summary>Docker/Podman compose network name (for inter-container DNS)</summary>
    public string NetworkName { get; set; } = "bricks4agent_worker-net";

    /// <summary>Worker type → Docker image name mapping</summary>
    public Dictionary<string, WorkerImageConfig> WorkerImages { get; set; } = new();

    /// <summary>Max containers per worker type</summary>
    public int MaxContainersPerType { get; set; } = 3;

    /// <summary>Container spawn timeout</summary>
    public TimeSpan SpawnTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Auto-respawn on health check failure</summary>
    public bool AutoRespawn { get; set; } = true;

    /// <summary>Broker host as seen from inside worker containers</summary>
    public string BrokerHostForWorkers { get; set; } = "broker";

    /// <summary>Broker TCP port for workers</summary>
    public int BrokerPortForWorkers { get; set; } = 7000;
}

/// <summary>
/// Per-worker-type image configuration
/// </summary>
public class WorkerImageConfig
{
    /// <summary>Docker image name (e.g. "bricks4agent/file-worker:latest")</summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>Additional environment variables</summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>Volume mounts (host:container format)</summary>
    public List<string> Volumes { get; set; } = new();

    /// <summary>Ports to expose (host:container format)</summary>
    public List<string> Ports { get; set; } = new();

    /// <summary>Whether to run as non-root (--user)</summary>
    public string? User { get; set; }

    /// <summary>Memory limit (e.g. "256m")</summary>
    public string? MemoryLimit { get; set; }

    /// <summary>CPU limit (e.g. "0.5")</summary>
    public string? CpuLimit { get; set; }
}
