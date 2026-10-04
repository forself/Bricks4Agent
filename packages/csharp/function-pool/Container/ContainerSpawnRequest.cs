namespace FunctionPool.Container;

/// <summary>
/// What to start: the worker type (selects the configured image), the worker id, and the
/// environment the broker composes for it.
/// </summary>
public sealed class ContainerSpawnRequest
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Worker type key (e.g. "file-worker", "agent"); must exist in ContainerManager:WorkerImages.</summary>
    public required string WorkerType { get; init; }

    /// <summary>Worker id assigned by the broker; also used in the container name.</summary>
    public required string WorkerId { get; init; }

    /// <summary>
    /// Environment composed by the broker itself (never copied from a request body).
    /// It may carry BROKER_* values for agents and is passed as <c>-e NAME=VALUE</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> TrustedEnvironment { get; init; } = Empty;

    /// <summary>
    /// Secret values. Only <c>-e NAME</c> appears in the runtime CLI arguments; the value reaches
    /// the container through the CLI process environment, so it is absent from argv, process
    /// listings and error messages. (The container runtime's own inspect output still shows it.)
    /// </summary>
    public IReadOnlyDictionary<string, string> SecretEnvironment { get; init; } = Empty;
}
