using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace GenerationWorker.Support;

/// <summary>生成器 CLI 一次呼叫的結果：成功時 <see cref="Output"/> 是 CLI 回傳的 JSON 物件。</summary>
public sealed class GeneratorCliResult
{
    private GeneratorCliResult(JsonObject? output, string? rawJson, string? failure)
    {
        Output = output;
        RawJson = rawJson;
        Failure = failure;
    }

    /// <summary>CLI 回傳的 JSON 物件（含 <c>ok</c>）；失敗時為 null。</summary>
    public JsonObject? Output { get; }

    /// <summary>CLI 原始 stdout（去除前後空白）。</summary>
    public string? RawJson { get; }

    /// <summary>內部失敗原因（逾時、輸出過大、結束碼非 0、輸出不是 JSON）；不含主機路徑。</summary>
    public string? Failure { get; }

    public bool Succeeded => Output != null;

    /// <summary>CLI 的 <c>ok</c> 欄位。</summary>
    public bool Ok => Output?["ok"] is JsonValue value && value.TryGetValue<bool>(out var ok) && ok;

    public static GeneratorCliResult FromOutput(JsonObject output, string rawJson) => new(output, rawJson, null);

    public static GeneratorCliResult Fail(string failure) => new(null, null, failure);
}

/// <summary>呼叫生成器 CLI（<c>node tools/generation/cli.mjs &lt;command&gt;</c>）。</summary>
public interface IGeneratorCli
{
    Task<GeneratorCliResult> RunAsync(string command, JsonObject input, TimeSpan timeout, CancellationToken ct);
}

/// <summary>
/// 以 node 子程序執行生成器 CLI。契約：從 stdin 讀一個 JSON、向 stdout 寫一個 JSON；
/// 正常處理（含驗證不通過）結束碼為 0，以 <c>ok</c> 表示結果；結束碼非 0 代表用法錯誤或內部例外。
/// </summary>
public sealed class NodeGeneratorCli : IGeneratorCli
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal) { "catalog", "validate", "build" };

    private readonly GenerationWorkerOptions _options;
    private readonly ILogger _logger;

    public NodeGeneratorCli(GenerationWorkerOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<GeneratorCliResult> RunAsync(string command, JsonObject input, TimeSpan timeout, CancellationToken ct)
    {
        if (!AllowedCommands.Contains(command))
            return GeneratorCliResult.Fail($"Unsupported generator command '{command}'.");

        var stdin = Encoding.UTF8.GetBytes(input.ToJsonString());
        if (stdin.Length > _options.MaxInputBytes)
            return GeneratorCliResult.Fail($"Generator input is too large ({stdin.Length} bytes, limit {_options.MaxInputBytes}).");

        BoundedProcessResult result;
        try
        {
            result = await BoundedProcessRunner.RunAsync(new BoundedProcessRequest
            {
                FileName = _options.NodePath,
                // V8 heap 上限：單次呼叫的記憶體有固定上限，不會因異常輸入占滿主機
                Arguments = new[] { $"--max-old-space-size={_options.MaxOldSpaceMegabytes}", _options.CliPath, command },
                WorkingDirectory = _options.ToolsRoot,
                Stdin = stdin,
                Timeout = timeout,
                MaxStdoutBytes = _options.MaxStdoutBytes,
                MaxStderrBytes = _options.MaxStderrBytes,
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 例外訊息可能含主機路徑，只寫進日誌。
            _logger.LogError(ex, "Generator process for '{Command}' could not be started.", command);
            return GeneratorCliResult.Fail("Generator process could not be started.");
        }

        if (!string.IsNullOrWhiteSpace(result.Stderr))
            _logger.LogDebug("Generator '{Command}' stderr: {Stderr}", command, result.Stderr);

        if (result.TimedOut)
            return GeneratorCliResult.Fail($"Generator '{command}' timed out after {timeout.TotalSeconds:0} seconds.");
        if (result.StdoutOverflow)
            return GeneratorCliResult.Fail($"Generator '{command}' output exceeded {_options.MaxStdoutBytes} bytes.");
        if (result.ExitCode != 0)
        {
            _logger.LogWarning("Generator '{Command}' exited with code {ExitCode}: {Stderr}", command, result.ExitCode, result.Stderr);
            return GeneratorCliResult.Fail($"Generator '{command}' failed (exit code {result.ExitCode}).");
        }

        var raw = Encoding.UTF8.GetString(result.Stdout).Trim();
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return GeneratorCliResult.Fail($"Generator '{command}' did not return JSON.");
        }

        if (parsed is not JsonObject output ||
            output["ok"] is not JsonValue okValue ||
            !okValue.TryGetValue<bool>(out _))
        {
            return GeneratorCliResult.Fail($"Generator '{command}' returned JSON without a boolean 'ok'.");
        }

        return GeneratorCliResult.FromOutput(output, raw);
    }
}
