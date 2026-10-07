using System.Diagnostics;
using System.Text;

namespace GenerationWorker.Support;

/// <summary>一次子程序執行的參數。</summary>
public sealed class BoundedProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
    public required string WorkingDirectory { get; init; }
    public byte[]? Stdin { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required int MaxStdoutBytes { get; init; }
    public required int MaxStderrBytes { get; init; }
}

/// <summary>子程序執行結果。stdout 超過上限時 <see cref="StdoutOverflow"/> 為 true，且子程序已被終止。</summary>
public sealed record BoundedProcessResult(
    int ExitCode,
    byte[] Stdout,
    string Stderr,
    bool TimedOut,
    bool StdoutOverflow);

/// <summary>
/// 安全執行子程序：不經 shell（檔名與參數陣列分開）、輸入只經 stdin、逾時即終止整個程序樹、
/// stdout 超過上限即終止、stderr 只保留上限內的部分。
/// 子程序的環境變數會移除 worker 憑證與其他密鑰類變數，以及會改變 node 載入行為的變數。
/// </summary>
public static class BoundedProcessRunner
{
    private static readonly string[] RemovedVariablePrefixes = { "WORKER_" };

    private static readonly string[] RemovedVariableNames = { "NODE_OPTIONS", "NODE_PATH" };

    private static readonly string[] RemovedVariableFragments =
    {
        "SECRET", "TOKEN", "PASSWORD", "PASSWD", "API_KEY", "APIKEY", "PRIVATE_KEY", "CREDENTIAL",
    };

    public static async Task<BoundedProcessResult> RunAsync(BoundedProcessRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in request.Arguments)
            psi.ArgumentList.Add(argument);
        ScrubEnvironment(psi.Environment);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutOverflow = 0;
        var stdoutTask = ReadStdoutAsync(process, request.MaxStdoutBytes, () => Interlocked.Exchange(ref stdoutOverflow, 1));
        var stderrTask = ReadStderrAsync(process.StandardError.BaseStream, request.MaxStderrBytes);
        var stdinTask = WriteStdinAsync(process, request.Stdin);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.Timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            if (ct.IsCancellationRequested)
                throw;
            timedOut = true;
        }

        // 子程序結束後管線會關閉；仍以上限等待，避免殘留的孫程序握住管線而卡住。
        var stdout = await AwaitWithin(stdoutTask, Array.Empty<byte>());
        var stderr = await AwaitWithin(stderrTask, string.Empty);
        await AwaitWithin(stdinTask, true);

        var exitCode = timedOut ? -1 : SafeExitCode(process);
        return new BoundedProcessResult(exitCode, stdout, stderr, timedOut, Volatile.Read(ref stdoutOverflow) == 1);
    }

    /// <summary>移除不應交給生成器子程序的環境變數。</summary>
    internal static void ScrubEnvironment(IDictionary<string, string?> environment)
    {
        foreach (var key in environment.Keys.ToList())
        {
            var upper = key.ToUpperInvariant();
            if (RemovedVariablePrefixes.Any(prefix => upper.StartsWith(prefix, StringComparison.Ordinal)) ||
                RemovedVariableNames.Contains(upper, StringComparer.Ordinal) ||
                RemovedVariableFragments.Any(fragment => upper.Contains(fragment, StringComparison.Ordinal)))
            {
                environment.Remove(key);
            }
        }
    }

    private static async Task<byte[]> ReadStdoutAsync(Process process, int maxBytes, Action markOverflow)
    {
        var stream = process.StandardOutput.BaseStream;
        using var collected = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer);
            }
            catch (Exception) when (process.HasExited)
            {
                break;
            }

            if (read == 0)
                break;

            if (collected.Length + read > maxBytes)
            {
                markOverflow();
                TryKill(process);
                break;
            }

            collected.Write(buffer, 0, read);
        }

        return collected.ToArray();
    }

    private static async Task<string> ReadStderrAsync(Stream stream, int maxBytes)
    {
        using var collected = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer);
            }
            catch (Exception)
            {
                break;
            }

            if (read == 0)
                break;

            // 超過上限的部分丟棄，但持續讀取，避免子程序因管線滿了而卡住。
            var room = maxBytes - (int)collected.Length;
            if (room > 0)
                collected.Write(buffer, 0, Math.Min(room, read));
        }

        return Encoding.UTF8.GetString(collected.ToArray());
    }

    private static async Task<bool> WriteStdinAsync(Process process, byte[]? input)
    {
        try
        {
            if (input is { Length: > 0 })
            {
                await process.StandardInput.BaseStream.WriteAsync(input);
                await process.StandardInput.BaseStream.FlushAsync();
            }
        }
        catch (Exception)
        {
            // 子程序提早結束（管線關閉）時寫入會失敗；結果由結束碼與輸出判斷。
        }
        finally
        {
            try { process.StandardInput.Close(); } catch { }
        }

        return true;
    }

    private static async Task<T> AwaitWithin<T>(Task<T> task, T fallback)
    {
        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; } catch { return -1; }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }
}
