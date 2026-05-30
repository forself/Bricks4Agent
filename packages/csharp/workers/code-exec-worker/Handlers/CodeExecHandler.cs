using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace CodeExecWorker.Handlers;

/// <summary>
/// code.exec — 沙箱化執行提交的程式碼。
///
/// Routes: python / bash(也可由 payload.language 指定)。
/// 隔離(無 privileged、無 docker-socket):
///   timeout -s KILL <t>            牆鐘上限、逾時 SIGKILL
///   ulimit -v <memKB>              虛擬記憶體上限(擋 OOM)
///   ulimit -t <cpuS>               CPU 秒上限
///   ulimit -u <nproc>              process 數上限(擋 fork-bomb)
///   ulimit -f <fsizeKB>            檔案大小上限
///   python3 -I                     isolated 模式(忽略 env/user site)
///   non-root 執行 + 拋棄式暫存目錄 + 輸出截斷
/// 注意(v1):網路隔離靠【部署層】(把本 worker 放沒有外網 egress 的網路);
///   程式內未做 per-process netns(需 privileged/userns)。治理面向靠 broker 的
///   approval gate + capability ACL/quota + audit(見 tool-specs/code.exec)。
/// </summary>
public sealed class CodeExecHandler : ICapabilityHandler
{
    public string CapabilityId => "code.exec";

    private readonly int _maxTimeout, _memKB, _nproc, _fsizeKB, _outCap;
    private readonly ILogger _log;

    public CodeExecHandler(int maxTimeout, int memKB, int nproc, int fsizeKB, int outCap, ILogger log)
    {
        _maxTimeout = maxTimeout; _memKB = memKB; _nproc = nproc; _fsizeKB = fsizeKB; _outCap = outCap; _log = log;
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(payload).RootElement; }
        catch { return (false, null, "invalid JSON payload"); }

        var lang = (root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String)
            ? l.GetString()! : route;
        if (lang != "python" && lang != "bash")
            return (false, null, $"unsupported language: {lang}(只支援 python / bash)");
        if (!root.TryGetProperty("code", out var c) || c.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(c.GetString()))
            return (false, null, "missing 'code'");
        var code = c.GetString()!;
        if (code.Length > 100_000) return (false, null, "code too large (>100KB)");
        var t = (root.TryGetProperty("timeout_s", out var ts) && ts.ValueKind == JsonValueKind.Number) ? ts.GetInt32() : 10;
        t = Math.Clamp(t, 1, _maxTimeout);
        var stdin = (root.TryGetProperty("stdin", out var si) && si.ValueKind == JsonValueKind.String) ? si.GetString()! : "";

        // 拋棄式暫存目錄 + 程式碼檔
        var dir = Path.Combine(Path.GetTempPath(), "cx_" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, lang == "python" ? "code.py" : "code.sh");
        await File.WriteAllTextAsync(file, code, ct);

        var interp = lang == "python" ? $"python3 -I {Sh(file)}" : $"bash {Sh(file)}";
        // ulimit 套在會 exec 成使用者程式的 bash → 限制隨 exec 繼承到目標進程
        var inner = $"ulimit -v {_memKB} -t {t} -u {_nproc} -f {_fsizeKB} 2>/dev/null; cd {Sh(dir)}; exec {interp}";

        var psi = new ProcessStartInfo
        {
            FileName = "timeout",
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        // -s KILL:逾時直接 SIGKILL;新 process group 由 timeout 處理
        psi.ArgumentList.Add("-s"); psi.ArgumentList.Add("KILL"); psi.ArgumentList.Add($"{t}");
        psi.ArgumentList.Add("bash"); psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(inner);
        psi.Environment["HOME"] = dir;   // 別讓它讀到 worker 的 HOME

        var so = new StringBuilder(); var se = new StringBuilder();
        var truncated = false;
        void Cap(StringBuilder b, string? d) { if (d == null) return; if (b.Length < _outCap) b.AppendLine(d); else truncated = true; }

        var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => Cap(so, e.Data);
        proc.ErrorDataReceived += (_, e) => Cap(se, e.Data);

        var sw = Stopwatch.StartNew();
        try
        {
            proc.Start();
            proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            if (stdin.Length > 0) await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            proc.StandardInput.Close();
            if (!proc.WaitForExit((t + 5) * 1000)) { try { proc.Kill(true); } catch { } }
            proc.WaitForExit(2000);
        }
        catch (Exception ex)
        {
            TryDelete(dir);
            return (false, null, "exec failed: " + ex.Message);
        }
        sw.Stop();
        var exit = -1; try { exit = proc.ExitCode; } catch { }
        var timedOut = exit is 124 or 137;   // timeout 逾時(124)/ SIGKILL(137)
        TryDelete(dir);

        var result = JsonSerializer.Serialize(new
        {
            language = lang,
            exit_code = exit,
            timed_out = timedOut,
            duration_ms = sw.ElapsedMilliseconds,
            stdout = so.ToString(),
            stderr = se.ToString(),
            truncated,
        });
        _log.LogInformation("code.exec {Lang} req={Req} exit={Exit} {Ms}ms timeout={TO} trunc={Tr}",
            lang, requestId, exit, sw.ElapsedMilliseconds, timedOut, truncated);
        return (true, result, null);
    }

    private static string Sh(string p) => "'" + p.Replace("'", "'\\''") + "'";   // 單引號 shell escape
    private static void TryDelete(string dir) { try { Directory.Delete(dir, true); } catch { } }
}
