using System.Collections.Concurrent;
using System.Diagnostics;
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

        var args = BuildRunArguments(_config, imageConfig, workerId, containerName, envOverrides);

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
            _config.Runtime, args, _config.SpawnTimeout, ct);

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
        await RunCommandAsync(_config.Runtime, new[] { "stop", "-t", "10", containerId }, TimeSpan.FromSeconds(15), ct);

        // Remove
        var (exitCode, _, stderr) = await RunCommandAsync(
            _config.Runtime, new[] { "rm", "-f", containerId }, TimeSpan.FromSeconds(10), ct);

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
            _config.Runtime, new[] { "logs", "--tail", tailLines.ToString(), containerId },
            TimeSpan.FromSeconds(10), ct);
        return string.IsNullOrEmpty(stdout) ? stderr : stdout;
    }

    public async Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var (exitCode, _, _) = await RunCommandAsync(
                _config.Runtime, new[] { "version", "--format", "json" },
                TimeSpan.FromSeconds(5), ct);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<ContainerStats>> GetStatsAsync(CancellationToken ct = default)
    {
        var running = _containers.Values
            .Where(c => c.State == ContainerState.Running)
            .Select(c => c.ContainerId)
            .ToList();
        if (running.Count == 0) return new List<ContainerStats>();

        var args = new List<string> { "stats", "--no-stream", "--format", "{{json .}}" };
        args.AddRange(running);

        var (exitCode, stdout, _) = await RunCommandAsync(
            _config.Runtime, args, TimeSpan.FromSeconds(15), ct);
        if (exitCode != 0) return new List<ContainerStats>();

        var result = new List<ContainerStats>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] != '{') continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(t);
                var r = doc.RootElement;
                var id = r.TryGetProperty("ID", out var idEl) ? idEl.GetString() ?? "" : "";
                if (id.Length > 12) id = id[..12];
                result.Add(new ContainerStats
                {
                    ContainerId   = id,
                    ContainerName = r.TryGetProperty("Name", out var nEl) ? nEl.GetString() ?? "" : "",
                    CpuPercent    = ParseStatPercent(r.TryGetProperty("CPUPerc", out var cEl) ? cEl.GetString() : null),
                    MemoryPercent = ParseStatPercent(r.TryGetProperty("MemPerc", out var mEl) ? mEl.GetString() : null),
                });
            }
            catch { /* skip malformed line */ }
        }
        return result;
    }

    private static double ParseStatPercent(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.TrimEnd('%').Trim();
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Execute a CLI command and capture output</summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCommandAsync(
        string command, IReadOnlyCollection<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

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
            throw new TimeoutException($"Command timed out after {timeout}: {command} {string.Join(' ', arguments)}");
        }
    }

    private static void AddEnv(List<string> args, string key, string? value)
    {
        args.Add("-e");
        args.Add($"{key}={value ?? string.Empty}");
    }

    internal static IReadOnlyList<string> BuildRunArguments(
        ContainerConfig config,
        WorkerImageConfig imageConfig,
        string workerId,
        string containerName,
        IReadOnlyDictionary<string, string>? envOverrides)
    {
        var args = new List<string>
        {
            "run",
            "-d",
            "--name",
            containerName
        };

        var networkName = string.IsNullOrWhiteSpace(imageConfig.NetworkName)
            ? config.NetworkName
            : imageConfig.NetworkName;
        if (!string.IsNullOrWhiteSpace(networkName))
            args.AddRange(new[] { "--network", networkName });

        if (!string.IsNullOrEmpty(imageConfig.MemoryLimit))
            args.AddRange(new[] { "--memory", imageConfig.MemoryLimit });
        if (!string.IsNullOrEmpty(imageConfig.CpuLimit))
            args.AddRange(new[] { "--cpus", imageConfig.CpuLimit });

        if (!string.IsNullOrEmpty(imageConfig.User))
            args.AddRange(new[] { "--user", imageConfig.User });

        AddEnv(args, "WORKER_Worker__BrokerHost", config.BrokerHostForWorkers);
        AddEnv(args, "WORKER_Worker__BrokerPort", config.BrokerPortForWorkers.ToString());
        AddEnv(args, "WORKER_Worker__WorkerId", workerId);

        foreach (var (key, val) in imageConfig.Environment)
            AddEnv(args, key, val);

        if (envOverrides != null)
        {
            foreach (var (key, val) in envOverrides)
                AddEnv(args, key, val);
        }

        foreach (var vol in imageConfig.Volumes)
            args.AddRange(new[] { "-v", vol });

        foreach (var port in imageConfig.Ports)
            args.AddRange(new[] { "-p", port });

        args.AddRange(new[] { "--restart", "on-failure:3" });
        args.Add(imageConfig.Image);

        return args;
    }
}
