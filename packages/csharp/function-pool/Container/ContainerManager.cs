using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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

    public async Task<List<ManagedContainer>> ListManagedAsync(CancellationToken ct = default)
    {
        // 自動同步：從 Docker 掃描所有 b4a-* 容器，合併到記憶體追蹤
        await SyncFromDockerAsync(ct);
        return _containers.Values.ToList();
    }

    /// <summary>
    /// 從 Docker 掃描所有 b4a-* 容器，自動同步到記憶體追蹤。
    /// 解決 broker 重啟後遺失容器追蹤的問題。
    /// </summary>
    private async Task SyncFromDockerAsync(CancellationToken ct)
    {
        try
        {
            var (exitCode, stdout, _) = await RunCommandAsync(
                _config.Runtime,
                "ps -a --format \"{{.ID}}|{{.Names}}|{{.Image}}|{{.Status}}|{{.CreatedAt}}\"",
                TimeSpan.FromSeconds(10), ct);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return;

            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('|');
                if (parts.Length < 4) continue;

                var id = parts[0].Trim();
                if (id.Length > 12) id = id[..12];

                // 已追蹤的跳過
                if (_containers.ContainsKey(id)) continue;

                var name       = parts[1].Trim();
                var image      = parts[2].Trim();
                var statusText = parts[3].Trim().ToLowerInvariant();

                // 排除 broker 本身（掃描到自己沒意義）
                if (name.Contains("broker")) continue;

                // 從容器名稱或 image 推測 worker type
                var workerType = image.Split(':')[0].Split('/').Last();
                if (name.StartsWith("b4a-"))
                {
                    var rest = name[4..];
                    var dash = rest.LastIndexOf('-');
                    if (dash > 0) workerType = rest[..dash];
                }

                var state = statusText.Contains("up") ? ContainerState.Running :
                            statusText.Contains("exited") ? ContainerState.Stopped :
                            ContainerState.Failed;

                _containers[id] = new ManagedContainer
                {
                    ContainerId = id,
                    WorkerId    = name,
                    WorkerType  = workerType,
                    ImageName   = image,
                    State       = state,
                    SpawnedAt   = DateTime.UtcNow
                };

                _logger.LogInformation("Discovered existing container: {Id} ({Type}) state={State}",
                    id, workerType, state);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Docker sync scan failed (non-critical)");
        }
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

    public async Task<List<ContainerStats>> GetStatsAsync(CancellationToken ct = default)
    {
        await SyncFromDockerAsync(ct);

        var running = _containers.Values
            .Where(c => c.State == ContainerState.Running)
            .ToList();

        if (running.Count == 0)
            return [];

        var ids = string.Join(" ", running.Select(c => c.ContainerId));

        // docker/podman stats outputs one JSON object per line (NDJSON)
        var (exitCode, stdout, _) = await RunCommandAsync(
            _config.Runtime,
            $"stats --no-stream --format \"{{{{json .}}}}\" {ids}",
            TimeSpan.FromSeconds(15), ct);

        if (exitCode != 0)
            return [];

        var result = new List<ContainerStats>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = ParseStatsLine(line.Trim());
            if (s != null) result.Add(s);
        }
        return result;
    }

    /// <summary>Parse one line of <c>docker stats --format "{{json .}}"</c> output</summary>
    private static ContainerStats? ParseStatsLine(string json)
    {
        if (string.IsNullOrEmpty(json) || !json.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            var id = r.TryGetProperty("ID", out var idEl) ? idEl.GetString() ?? "" : "";
            if (id.Length > 12) id = id[..12];

            var memStr = r.TryGetProperty("MemUsage", out var muEl) ? muEl.GetString() : null;
            var netStr = r.TryGetProperty("NetIO",    out var niEl) ? niEl.GetString() : null;
            var blkStr = r.TryGetProperty("BlockIO",  out var biEl) ? biEl.GetString() : null;

            var (memUse, memLim) = ParseSlashPair(memStr);
            var (netIn,  netOut) = ParseSlashPair(netStr);
            var (blkR,   blkW)  = ParseSlashPair(blkStr);

            return new ContainerStats
            {
                ContainerId        = id,
                ContainerName      = r.TryGetProperty("Name",    out var nEl)  ? nEl.GetString()  ?? "" : "",
                CpuPercent         = ParsePercent(r.TryGetProperty("CPUPerc", out var cEl)  ? cEl.GetString()  : null),
                MemoryPercent      = ParsePercent(r.TryGetProperty("MemPerc", out var mpEl) ? mpEl.GetString() : null),
                MemoryUsageBytes   = memUse,
                MemoryLimitBytes   = memLim,
                NetworkInputBytes  = netIn,
                NetworkOutputBytes = netOut,
                BlockReadBytes     = blkR,
                BlockWriteBytes    = blkW,
            };
        }
        catch { return null; }
    }

    private static double ParsePercent(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.TrimEnd('%').Trim();
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Parse "A / B" size strings (e.g. "1.5MiB / 2GiB") into (A, B) bytes</summary>
    private static (long A, long B) ParseSlashPair(string? s)
    {
        if (string.IsNullOrEmpty(s)) return (0, 0);
        var parts = s.Split('/');
        return (ParseBytes(parts[0].Trim()),
                parts.Length > 1 ? ParseBytes(parts[1].Trim()) : 0);
    }

    /// <summary>Parse a human-readable byte size string into raw bytes (e.g. "1.5MiB" → 1572864)</summary>
    private static long ParseBytes(string s)
    {
        if (string.IsNullOrEmpty(s) || s == "0") return 0;

        // Check suffixes longest-first to avoid partial matches (e.g. "MiB" before "B")
        (string Suffix, long Mult)[] units =
        [
            ("TiB", 1L << 40), ("GiB", 1L << 30), ("MiB", 1L << 20), ("KiB", 1L << 10),
            ("TB",  1_000_000_000_000L), ("GB", 1_000_000_000L), ("MB", 1_000_000L), ("kB", 1_000L),
            ("B",   1L),
        ];

        foreach (var (suffix, mult) in units)
        {
            if (!s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var numStr = s[..^suffix.Length].Trim();
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return (long)(v * mult);
        }

        return long.TryParse(s, out var raw) ? raw : 0;
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
