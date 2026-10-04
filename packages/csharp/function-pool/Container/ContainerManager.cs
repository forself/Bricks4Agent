using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
/// - Every container gets the design §13.2 hardening (read-only rootfs, tmpfs /tmp, no
///   capabilities, no-new-privileges, pids and memory limits); configuration that would
///   weaken it (root user, host network, agent mounts or ports, socket or system mounts)
///   is refused instead of being passed through.
/// </summary>
public class ContainerManager : IContainerManager
{
    public const string AgentWorkerType = "agent";
    internal const string TmpfsMount = "/tmp:rw,noexec,nosuid,nodev,size=64m";
    private const string FallbackMemoryLimit = "512m";

    private static readonly Regex TrustedEnvNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_.\-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex SecretEnvNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex NamedVolumePattern = new(@"^[A-Za-z0-9][A-Za-z0-9_.\-]*$", RegexOptions.CultureInvariant);

    private static readonly string[] ForbiddenLinuxHostRoots =
    {
        "/", "/etc", "/proc", "/sys", "/dev", "/run", "/var/run", "/var/lib/docker",
        "/var/lib/containers", "/boot", "/root", "/usr", "/bin", "/sbin", "/lib", "/lib64",
    };

    private readonly ContainerConfig _config;
    private readonly ILogger<ContainerManager> _logger;
    private readonly ConcurrentDictionary<string, ManagedContainer> _containers = new();

    public ContainerManager(ContainerConfig config, ILogger<ContainerManager> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> SpawnWorkerAsync(ContainerSpawnRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workerType = request.WorkerType;
        var workerId = request.WorkerId;

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

        var args = BuildRunArguments(_config, imageConfig, request, containerName);

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
            _config.Runtime, args, _config.SpawnTimeout, ct, BuildCliEnvironment(request));

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

        // Remove, together with any anonymous volume
        var (exitCode, _, stderr) = await RunCommandAsync(
            _config.Runtime, BuildRemoveArguments(containerId), TimeSpan.FromSeconds(10), ct);

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
    /// <param name="environment">
    /// Extra variables for the CLI process only (secret values named by <c>-e NAME</c>);
    /// they never appear in the argument list.
    /// </param>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCommandAsync(
        string command,
        IReadOnlyCollection<string> arguments,
        TimeSpan timeout,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
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
        if (environment != null)
        {
            foreach (var (name, value) in environment)
                process.StartInfo.Environment[name] = value;
        }

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
            // Only the sub-command: the remaining arguments carry the container environment.
            var subCommand = arguments.Count > 0 ? arguments.First() : string.Empty;
            throw new TimeoutException($"Command timed out after {timeout}: {command} {subCommand}");
        }
    }

    private static void AddEnv(List<string> args, string key, string? value)
    {
        args.Add("-e");
        args.Add($"{key}={value ?? string.Empty}");
    }

    internal static IReadOnlyList<string> BuildRemoveArguments(string containerId)
        => new[] { "rm", "-f", "-v", containerId };

    public static bool IsAgentWorkerType(string workerType)
        => string.Equals(workerType, AgentWorkerType, StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<string> BuildRunArguments(
        ContainerConfig config,
        WorkerImageConfig imageConfig,
        ContainerSpawnRequest request,
        string containerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isAgent = IsAgentWorkerType(request.WorkerType);

        if (string.IsNullOrWhiteSpace(imageConfig.Image))
            throw new InvalidOperationException($"No image configured for worker type '{request.WorkerType}'.");

        var args = new List<string>
        {
            "run",
            "-d",
            "--name",
            containerName
        };

        // ── Network: agents never fall back to the shared worker network ──
        var networkName = isAgent
            ? imageConfig.NetworkName
            : string.IsNullOrWhiteSpace(imageConfig.NetworkName) ? config.NetworkName : imageConfig.NetworkName;
        if (string.IsNullOrWhiteSpace(networkName))
        {
            if (isAgent && !config.AllowAgentDefaultNetwork)
                throw new InvalidOperationException(
                    "Agent containers need a dedicated network (WorkerImages:agent:NetworkName). " +
                    "Set ContainerManager:AllowAgentDefaultNetwork=true only for a host-run broker without one.");
        }
        else
        {
            ValidateNetwork(networkName.Trim());
            args.AddRange(new[] { "--network", networkName.Trim() });
        }

        // ── Resource limits ──
        var memoryLimit = !string.IsNullOrWhiteSpace(imageConfig.MemoryLimit)
            ? imageConfig.MemoryLimit
            : !string.IsNullOrWhiteSpace(config.DefaultMemoryLimit) ? config.DefaultMemoryLimit : FallbackMemoryLimit;
        args.AddRange(new[] { "--memory", memoryLimit.Trim() });
        if (!string.IsNullOrEmpty(imageConfig.CpuLimit))
            args.AddRange(new[] { "--cpus", imageConfig.CpuLimit });

        var pidsLimit = imageConfig.PidsLimit ?? config.DefaultPidsLimit;
        if (pidsLimit <= 0)
            throw new InvalidOperationException($"PidsLimit must be positive for worker type '{request.WorkerType}'.");

        // ── Identity: the image's non-root USER applies unless overridden by a non-root user ──
        if (!string.IsNullOrWhiteSpace(imageConfig.User))
        {
            ValidateUser(imageConfig.User.Trim());
            args.AddRange(new[] { "--user", imageConfig.User.Trim() });
        }

        // ── §13.2 hardening, always on ──
        args.Add("--read-only");
        args.AddRange(new[] { "--tmpfs", TmpfsMount });
        args.AddRange(new[] { "--cap-drop", "ALL" });
        args.AddRange(new[] { "--security-opt", "no-new-privileges:true" });
        args.AddRange(new[] { "--pids-limit", pidsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        // ── Environment ──
        var plainNames = new HashSet<string>(StringComparer.Ordinal);
        void AddPlain(string key, string? value)
        {
            if (!TrustedEnvNamePattern.IsMatch(key))
                throw new InvalidOperationException($"Invalid environment variable name: '{key}'.");
            plainNames.Add(key);
            AddEnv(args, key, value);
        }

        AddPlain("WORKER_Worker__BrokerHost", config.BrokerHostForWorkers);
        AddPlain("WORKER_Worker__BrokerPort", config.BrokerPortForWorkers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddPlain("WORKER_Worker__WorkerId", request.WorkerId);

        foreach (var (key, val) in imageConfig.Environment)
            AddPlain(key, val);

        foreach (var (key, val) in request.TrustedEnvironment)
            AddPlain(key, val);

        foreach (var name in request.SecretEnvironment.Keys)
        {
            if (!SecretEnvNamePattern.IsMatch(name))
                throw new InvalidOperationException($"Invalid secret environment variable name: '{name}'.");
            if (plainNames.Contains(name))
                throw new InvalidOperationException($"Secret environment variable '{name}' is also set as a plain value.");
            // Name only: the runtime CLI copies the value from its own process environment.
            args.Add("-e");
            args.Add(name);
        }

        // ── Mounts and ports ──
        if (isAgent)
        {
            if (imageConfig.Volumes.Count > 0)
                throw new InvalidOperationException("Agent containers must not mount volumes (§13.4); remove WorkerImages:agent:Volumes.");
            if (imageConfig.Ports.Count > 0)
                throw new InvalidOperationException("Agent containers must not publish ports; remove WorkerImages:agent:Ports.");
        }

        foreach (var vol in imageConfig.Volumes)
        {
            ValidateVolume(vol, config.AllowedHostPathRoots);
            args.AddRange(new[] { "-v", vol });
        }

        foreach (var port in imageConfig.Ports)
        {
            ValidatePort(port);
            args.AddRange(new[] { "-p", port });
        }

        args.AddRange(new[] { "--restart", "on-failure:3" });
        args.Add(imageConfig.Image);

        return args;
    }

    /// <summary>The CLI process environment for a spawn: exactly the secret values named by <c>-e NAME</c>.</summary>
    internal static IReadOnlyDictionary<string, string> BuildCliEnvironment(ContainerSpawnRequest request)
        => new Dictionary<string, string>(request.SecretEnvironment, StringComparer.Ordinal);

    private static void ValidateNetwork(string networkName)
    {
        if (string.Equals(networkName, "host", StringComparison.OrdinalIgnoreCase) ||
            networkName.StartsWith("container:", StringComparison.OrdinalIgnoreCase) ||
            networkName.StartsWith("ns:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Network mode '{networkName}' shares a host or container namespace and is not allowed.");
        }
    }

    private static void ValidateUser(string user)
    {
        var parts = user.Split(':');
        foreach (var part in parts)
        {
            var value = part.Trim();
            if (value == "0" || string.Equals(value, "root", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Worker containers must not run as root.");
        }
    }

    private static void ValidatePort(string port)
    {
        if (!port.StartsWith("127.0.0.1:", StringComparison.Ordinal))
            throw new InvalidOperationException($"Published ports must bind 127.0.0.1 ('{port}').");
    }

    /// <summary>
    /// Non-agent mounts: named volumes are allowed; host paths must be absolute, must not be a
    /// socket or a system path, and must sit under one of the allowed host path roots.
    /// </summary>
    internal static void ValidateVolume(string spec, IReadOnlyCollection<string> allowedHostPathRoots)
    {
        var (source, target) = SplitVolume(spec);
        if (string.IsNullOrWhiteSpace(target) || !(target.StartsWith('/')))
            throw new InvalidOperationException($"Volume '{spec}' needs an absolute container path.");
        if (source.Length == 0 || NamedVolumePattern.IsMatch(source))
            return; // anonymous or named volume, not a host path

        if (LooksLikeSocket(source))
            throw new InvalidOperationException($"Mounting a runtime socket or pipe is not allowed ('{source}').");

        var normalized = NormalizeHostPath(source)
            ?? throw new InvalidOperationException($"Host path '{source}' must be absolute and must not contain '..'.");

        if (IsSystemPath(normalized))
            throw new InvalidOperationException($"Mounting system path '{source}' is not allowed.");

        var allowed = allowedHostPathRoots
            .Select(NormalizeHostPath)
            .Where(root => root != null && !IsSystemPath(root))
            .Any(root => IsUnder(normalized, root!));
        if (!allowed)
            throw new InvalidOperationException(
                $"Host path '{source}' is not under ContainerManager:AllowedHostPathRoots.");
    }

    private static (string Source, string Target) SplitVolume(string spec)
    {
        var value = spec.Trim();
        var offset = 0;
        // Windows drive letter: "D:\dir:/target" or "D:/dir:/target"
        if (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
            offset = 2;
        var separator = value.IndexOf(':', offset);
        if (separator < 0)
            return (string.Empty, value); // anonymous volume
        var source = value[..separator];
        var rest = value[(separator + 1)..];
        var optionSeparator = rest.IndexOf(':');
        var target = optionSeparator < 0 ? rest : rest[..optionSeparator];
        return (source, target);
    }

    private static bool LooksLikeSocket(string source)
    {
        var lower = source.Replace('\\', '/').ToLowerInvariant();
        return lower.EndsWith(".sock", StringComparison.Ordinal) ||
               lower.Contains("docker.sock", StringComparison.Ordinal) ||
               lower.Contains("podman.sock", StringComparison.Ordinal) ||
               lower.Contains("containerd.sock", StringComparison.Ordinal) ||
               lower.StartsWith("//./pipe/", StringComparison.Ordinal) ||
               lower.StartsWith("npipe:", StringComparison.Ordinal);
    }

    /// <summary>Forward slashes, no trailing slash; Windows paths lower-cased. Null when relative or containing "..".</summary>
    private static string? NormalizeHostPath(string path)
    {
        var value = path.Trim().Replace('\\', '/');
        var isWindows = value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '/';
        var isUnc = value.StartsWith("//", StringComparison.Ordinal);
        if (!isWindows && !isUnc && !value.StartsWith('/'))
            return null;
        if (value.Split('/').Any(segment => segment == ".."))
            return null;
        while (value.Contains("//", StringComparison.Ordinal) && !isUnc)
            value = value.Replace("//", "/", StringComparison.Ordinal);
        if (value.Length > 1 && value.EndsWith('/') && !(isWindows && value.Length == 3))
            value = value.TrimEnd('/');
        return isWindows || isUnc ? value.ToLowerInvariant() : value;
    }

    private static bool IsSystemPath(string normalized)
    {
        // Windows: a drive root or the Windows directory
        if (normalized.Length >= 2 && normalized[1] == ':')
        {
            var rest = normalized.Length > 2 ? normalized[2..].TrimEnd('/') : string.Empty;
            return rest.Length == 0 || IsUnder(rest, "/windows");
        }
        if (normalized.StartsWith("//", StringComparison.Ordinal))
            return false;

        foreach (var forbidden in ForbiddenLinuxHostRoots)
        {
            if (forbidden == "/" ? normalized == "/" : IsUnder(normalized, forbidden))
                return true;
        }
        return false;
    }

    private static bool IsUnder(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.Ordinal))
            return true;
        var prefix = root.EndsWith('/') ? root : root + "/";
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
