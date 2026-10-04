namespace FunctionPool.Container;

/// <summary>
/// Container orchestration configuration
/// </summary>
public class ContainerConfig
{
    /// <summary>Container runtime: "docker" or "podman"</summary>
    public string Runtime { get; set; } = "docker";

    /// <summary>
    /// Docker/Podman network for worker containers (inter-container DNS).
    /// Agent containers never fall back to this network: they need their own
    /// <see cref="WorkerImageConfig.NetworkName"/> (see <see cref="AllowAgentDefaultNetwork"/>).
    /// </summary>
    public string NetworkName { get; set; } = "bricks4agent_worker-net";

    /// <summary>Worker type → Docker image name mapping</summary>
    public Dictionary<string, WorkerImageConfig> WorkerImages { get; set; } = new();

    /// <summary>Max containers per worker type</summary>
    public int MaxContainersPerType { get; set; } = 3;

    /// <summary>Container spawn timeout</summary>
    public TimeSpan SpawnTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Auto-respawn on health check failure (not implemented; kept for configuration compatibility)</summary>
    public bool AutoRespawn { get; set; } = true;

    /// <summary>Broker host as seen from inside worker containers</summary>
    public string BrokerHostForWorkers { get; set; } = "broker";

    /// <summary>Broker TCP port for workers</summary>
    public int BrokerPortForWorkers { get; set; } = 7000;

    /// <summary>Broker HTTP URL as seen from inside agent containers</summary>
    public string AgentBrokerUrl { get; set; } = "http://broker:5000";

    /// <summary>
    /// When false (default), an agent container without its own NetworkName is refused.
    /// Only a deployment whose broker runs on the host and has no dedicated agent network
    /// (the Windows sidecar) sets this to true; the agent then uses the runtime default network.
    /// </summary>
    public bool AllowAgentDefaultNetwork { get; set; }

    /// <summary>
    /// Host directories that non-agent worker images may bind-mount (agent images may not mount anything).
    /// Empty means no host path is allowed; named volumes are always allowed.
    /// </summary>
    public List<string> AllowedHostPathRoots { get; set; } = new();

    /// <summary>Memory limit applied when an image config sets none</summary>
    public string DefaultMemoryLimit { get; set; } = "512m";

    /// <summary>--pids-limit applied when an image config sets none</summary>
    public int DefaultPidsLimit { get; set; } = 256;
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

    /// <summary>
    /// Volume mounts (host:container[:options]). Refused for agent images; for other images the
    /// host path must sit under <see cref="ContainerConfig.AllowedHostPathRoots"/>.
    /// </summary>
    public List<string> Volumes { get; set; } = new();

    /// <summary>Ports to publish (127.0.0.1:host:container). Refused for agent images.</summary>
    public List<string> Ports { get; set; } = new();

    /// <summary>Optional --user override; root (0 / root) is refused</summary>
    public string? User { get; set; }

    /// <summary>Memory limit (e.g. "256m"); defaults to <see cref="ContainerConfig.DefaultMemoryLimit"/></summary>
    public string? MemoryLimit { get; set; }

    /// <summary>CPU limit (e.g. "0.5")</summary>
    public string? CpuLimit { get; set; }

    /// <summary>Optional per-image network override</summary>
    public string? NetworkName { get; set; }

    /// <summary>--pids-limit for this image; defaults to <see cref="ContainerConfig.DefaultPidsLimit"/></summary>
    public int? PidsLimit { get; set; }
}
