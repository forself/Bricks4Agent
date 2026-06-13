using System.Text.Json;
using ExecutionAdapterWorker.Support;
using WorkerSdk;

namespace ExecutionAdapterWorker.Handlers;

/// <summary>
/// build.test.run 能力處理器（route: execution.build_test.run）— §14.3 Build/Test Adapter。
///
/// 責任：在隔離環境執行白名單命令、收集 stdout/stderr、產出結構化結果。
/// 命令必須白名單化（npm test / npm run build / dotnet test / pytest …）。
/// 不經 shell 執行（避免注入）；只接受已被 broker 核可的請求。
/// </summary>
public class BuildTestRunHandler : ICapabilityHandler
{
    private readonly string _workspaceRoot;
    private readonly HashSet<string> _whitelist;
    private readonly string _evidenceRoot;
    private readonly TimeSpan _timeout;

    public string CapabilityId => "build.test.run";

    public static readonly string[] DefaultWhitelist =
    {
        "npm test", "npm run build", "dotnet test", "dotnet build", "pytest",
    };

    public BuildTestRunHandler(
        string workspaceRoot,
        IEnumerable<string>? whitelist = null,
        string? evidenceRoot = null,
        TimeSpan? timeout = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _whitelist = new HashSet<string>(
            (whitelist ?? DefaultWhitelist).Select(Normalize), StringComparer.Ordinal);
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? Path.Combine(_workspaceRoot, ".b4a-evidence"));
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            JsonElement root;
            using var doc = JsonDocument.Parse(payload);
            root = doc.RootElement.TryGetProperty("args", out var argsEl) ? argsEl : doc.RootElement;

            var command = root.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() : null;

            if (string.IsNullOrWhiteSpace(command))
                return Fail("Missing 'command'.");

            var normalized = Normalize(command);
            if (!_whitelist.Contains(normalized))
                return Fail($"Command not whitelisted: '{command}'. Allowed: {string.Join(", ", _whitelist)}");

            // 切成 exe + 參數（白名單命令皆為簡單 token，不經 shell）
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var exe = tokens[0];
            var args = tokens.Skip(1).ToArray();

            var run = await ProcessRunner.RunAsync(exe, args, _workspaceRoot, _timeout, ct: ct);

            Directory.CreateDirectory(_evidenceRoot);
            var evidenceFile = Path.Combine(_evidenceRoot, $"{Sanitize(requestId)}.log");
            await File.WriteAllTextAsync(evidenceFile,
                $"$ {normalized}\n[exit {run.ExitCode}{(run.TimedOut ? " TIMEOUT" : "")}]\n\n--- stdout ---\n{run.Stdout}\n--- stderr ---\n{run.Stderr}",
                ct);

            var success = !run.TimedOut && run.ExitCode == 0;
            var result = JsonSerializer.Serialize(new
            {
                capability = CapabilityId,
                success,
                command = normalized,
                exit_code = run.ExitCode,
                timed_out = run.TimedOut,
                stdout = run.Stdout,
                stderr = run.Stderr,
                evidence_ref = evidenceFile,
            });

            // 命令本身執行了（即使測試失敗 exit!=0），仍視為 handler 成功回報；
            // success 欄位反映命令結果。逾時則 handler 回報失敗。
            return run.TimedOut
                ? (false, result, $"Command timed out after {_timeout.TotalSeconds}s.")
                : (true, result, null);
        }
        catch (Exception ex)
        {
            return Fail($"build.test.run error: {ex.Message}");
        }
    }

    private static string Normalize(string command)
        => string.Join(' ', command.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Sanitize(string s)
        => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private static (bool, string?, string?) Fail(string msg) => (false, null, msg);
}
