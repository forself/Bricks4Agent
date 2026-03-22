using System.Diagnostics;

namespace Broker.Services;

public sealed class ProcessRunSpec
{
    public string FileName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public IDictionary<string, string?> EnvironmentVariables { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProcessRunResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
}

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunSpec spec, CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessRunSpec spec, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(spec.WorkingDirectory)
                ? Directory.GetCurrentDirectory()
                : spec.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var item in spec.EnvironmentVariables)
            psi.Environment[item.Key] = item.Value ?? string.Empty;

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdOutTask,
            StandardError = await stdErrTask
        };
    }
}
