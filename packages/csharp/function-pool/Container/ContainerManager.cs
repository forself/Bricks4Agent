using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Container;

/// <summary>
/// Container lifecycle manager — spawns/stops worker containers via Docker/Podman CLI.
///
/// Design decisions:
/// - CLI-based (no Docker SDK dependency) — works with both Docker and Podman
/// - Container spawning is infrequent, CLI overhead is negligible
/// - Each spawned container is tracked in-memory with ManagedContainer
/// - Workers connect back to broker via TCP (outbound from container)
/// </summary>
public class ContainerManager : IContainerManager
{
    private readonly ContainerConfig _config;
    private readonly ILogger<ContainerManager> _logger;
    private readonly ConcurrentDictionary<string, ManagedContainer> _containers = new();

    public ContainerManager(ContainerConfig config, ILogger<ContainerManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> SpawnWorkerAsync(
        string workerType,
        string workerId,
        Dictionary<string, string>? envOverrides = null,
        CancellationToken ct = default)
    {
        if (!_config.WorkerImages.TryGetValue(workerType, out var imageConfig))
            throw new InvalidOperationException($"Unknown worker type: '{workerType}'. Configure in ContainerManager:WorkerImages.");

        // Check per-type limit
        var typeCount = _containers.Values.Count(c =>
            c.WorkerType == workerType &&
            c.State is ContainerState.Starting or ContainerState.Running);

        if (typeCount >= _config.MaxContainersPerType)
            throw new InvalidOperationException(
                $"Max containers for '{workerType}' reached ({_config.MaxContainersPerType})");

        var containerName = $"b4a-{workerType}-{workerId[..Math.Min(12, workerId.Length)]}";

        // Build docker run command
        var args = new StringBuilder();
        args.Append($"run -d --name {containerName}");

        // Network
        if (!string.IsNullOrEmpty(_config.NetworkName))
            args.Append($" --network {_config.NetworkName}");

        // Resource limits
        if (!string.IsNullOrEmpty(imageConfig.MemoryLimit))
            args.Append($" --memory {imageConfig.MemoryLimit}");
        if (!string.IsNullOrEmpty(imageConfig.CpuLimit))
            args.Append($" --cpus {imageConfig.CpuLimit}");

        // User
        if (!string.IsNullOrEmpty(imageConfig.User))
            args.Append($" --user {imageConfig.User}");

        // Standard worker environment
        args.Append($" -e WORKER_Worker__BrokerHost={_config.BrokerHostForWorkers}");
        args.Append($" -e WORKER_Worker__BrokerPort={_config.BrokerPortForWorkers}");
        args.Append($" -e WORKER_Worker__WorkerId={workerId}");

        // Image-specific environment
        foreach (var (key, val) in imageConfig.Environment)
            args.Append($" -e {key}={val}");

        // Override environment
        if (envOverrides != null)
        {
            foreach (var (key, val) in envOverrides)
                args.Append($" -e {key}={val}");
        }

        // Volumes
        foreach (var vol in imageConfig.Volumes)
            args.Append($" -v {vol}");

        // Ports
        foreach (var port in imageConfig.Ports)
            args.Append($" -p {port}");

        // Restart policy
        args.Append(" --restart on-failure:3");

        // Image
        args.Append($" {imageConfig.Image}");

        var managed = new ManagedContainer
        {
            WorkerId = workerId,
            WorkerType = workerType,
            ImageName = imageConfig.Image,
            State = ContainerState.Starting
        };

        _logger.LogInformation(
            "Spawning worker container: type={Type} id={WorkerId} image={Image}",
            workerType, workerId, imageConfig.Image);

        var (exitCode, stdout, stderr) = await RunCommandAsync(
            _config.Runtime, args.ToString(), _config.SpawnTimeout, ct);

        if (exitCode != 0)
        {
            managed.State = ContainerState.Failed;
            _logger.LogError(
                "Failed to spawn container: exit={Exit} stderr={Stderr}",
                exitCode, stderr);
            throw new InvalidOperationException($"Container spawn failed: {stderr}");
        }

        var containerId = stdout.Trim();
        if (containerId.Length > 12)
            containerId = containerId[..12]; // short ID

        managed.ContainerId = containerId;
        managed.State = ContainerState.Running;
        _containers[containerId] = managed;

        _logger.LogInformation(
            "Worker container spawned: containerId={ContainerId} type={Type} workerId={WorkerId}",
            containerId, workerType, workerId);

        return containerId;
    }

    public async Task StopWorkerAsync(string containerId, CancellationToken ct = default)
    {
        if (_containers.TryGetValue(containerId, out var managed))
            managed.State = ContainerState.Stopping;

        _logger.LogInformation("Stopping worker container: {ContainerId}", containerId);

        // Stop
        await RunCommandAsync(_config.Runtime, $"stop -t 10 {containerId}", TimeSpan.FromSeconds(15), ct);

        // Remove
        var (exitCode, _, stderr) = await RunCommandAsync(
            _config.Runtime, $"rm -f {containerId}", TimeSpan.FromSeconds(10), ct);

        if (_containers.TryRemove(containerId, out var removed))
            removed.State = ContainerState.Stopped;

        if (exitCode != 0)
            _logger.LogWarning("Container removal warning: {Stderr}", stderr);
        else
            _logger.LogInformation("Worker container removed: {ContainerId}", containerId);
    }

    public Task<List<ManagedContainer>> ListManagedAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_containers.Values.ToList());
    }

    public async Task<string> GetLogsAsync(string containerId, int tailLines = 50, CancellationToken ct = default)
    {
        var (_, stdout, stderr) = await RunCommandAsync(
            _config.Runtime, $"logs --tail {tailLines} {containerId}",
            TimeSpan.FromSeconds(10), ct);
        return string.IsNullOrEmpty(stdout) ? stderr : stdout;
    }

    public async Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var (exitCode, _, _) = await RunCommandAsync(
                _config.Runtime, "version --format json",
                TimeSpan.FromSeconds(5), ct);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Execute a CLI command and capture output</summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCommandAsync(
        string command, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"Command timed out after {timeout}: {command} {arguments}");
        }
    }
}
