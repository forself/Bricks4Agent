using System.Diagnostics;
using System.Text;

namespace ExecutionAdapterWorker.Support;

/// <summary>
/// 安全執行子行程：不經 shell（避免命令注入），分開 exe 與參數，
/// 帶逾時與輸出截斷。repo-adapter（git）與 build-test-adapter 共用。
/// </summary>
public static class ProcessRunner
{
    public record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

    public const int DefaultMaxOutputChars = 64 * 1024;

    /// <summary>
    /// 執行 fileName + args（不經 shell）。args 已是分好的參數陣列。
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        int maxOutputChars = DefaultMaxOutputChars,
        string? stdin = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (stdin != null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
        }

        return new ProcessResult(
            timedOut ? -1 : proc.ExitCode,
            Truncate(stdout.ToString(), maxOutputChars),
            Truncate(stderr.ToString(), maxOutputChars),
            timedOut);
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s[..max] + $"\n…[truncated {s.Length - max} chars]";
    }
}
