using System.Text;
using System.Text.Json.Nodes;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace GenerationWorker.Handlers;

/// <summary>
/// generation.catalog.query（route: query_component_catalog）：查詢元件型錄摘要。
/// 只轉呼叫生成器 CLI 的 <c>catalog</c> 並原樣回傳其 JSON；找不到元件時 CLI 回 <c>ok:false</c>，
/// 仍以 Success=true 回傳，讓代理依結構化錯誤繼續。只有內部錯誤才回 Success=false。
/// </summary>
public sealed class CatalogQueryHandler : ICapabilityHandler
{
    public const string Route = "query_component_catalog";

    private static readonly HashSet<string> Sections = new(StringComparer.Ordinal) { "overview", "field_types", "example", "component" };

    private readonly GenerationWorkerOptions _options;
    private readonly IGeneratorCli _cli;
    private readonly ILogger _logger;

    public CatalogQueryHandler(GenerationWorkerOptions options, IGeneratorCli cli, ILogger logger)
    {
        _options = options;
        _cli = cli;
        _logger = logger;
    }

    public string CapabilityId => "generation.catalog.query";

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            if (!string.Equals(route, Route, StringComparison.Ordinal))
                return Fail($"Route '{route}' is not handled by {CapabilityId}.");

            var args = GenerationRequest.ExtractArgs(payload);
            if (args == null)
                return Fail("Payload is not a JSON object.");

            if (!GenerationRequest.TryGetString(args, "section", 32, out var section) ||
                (section != null && !Sections.Contains(section)))
            {
                return Fail("section must be one of overview, field_types, example, component.");
            }

            if (!GenerationRequest.TryGetString(args, "name", 64, out var name))
                return Fail("name must be a string of at most 64 characters.");

            var input = new JsonObject();
            if (section != null)
                input["section"] = section;
            if (name != null)
                input["name"] = name;

            var result = await _cli.RunAsync("catalog", input, _options.QueryTimeout, ct);
            return GeneratorResponses.PassThrough(result, _options, _logger, CapabilityId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Capability} failed for {RequestId}", CapabilityId, requestId);
            return Fail($"{CapabilityId} failed.");
        }
    }

    private static (bool, string?, string?) Fail(string message) => (false, null, message);
}

/// <summary>catalog 與 validate 共用：把 CLI 的 JSON 原樣回傳前做大小與本機路徑檢查。</summary>
internal static class GeneratorResponses
{
    /// <summary>超過大小上限時，validate 結果保留的錯誤與警告筆數起點（放不下時逐次減半）。</summary>
    internal const int TruncatedErrorCount = 50;
    internal const int TruncatedWarningCount = 20;

    /// <param name="truncateIssues">
    /// validate 用：結果超過上限時改回結構化的截斷結果（Success=true、保留 ok、前幾筆 errors／warnings、
    /// total_errors 與 truncated:true），代理仍能依錯誤修正；其他指令超過上限時回 Success=false。
    /// </param>
    public static (bool Success, string? ResultPayload, string? Error) PassThrough(
        GeneratorCliResult result, GenerationWorkerOptions options, ILogger logger, string capabilityId, bool truncateIssues = false)
    {
        if (!result.Succeeded)
            return (false, null, result.Failure);

        var json = result.RawJson!;
        if (Encoding.UTF8.GetByteCount(json) > options.MaxResultBytes)
        {
            var truncated = truncateIssues ? TruncateIssues(result.Output!, options.MaxResultBytes) : null;
            if (truncated == null)
                return (false, null, $"{capabilityId} result exceeded {options.MaxResultBytes} bytes.");

            logger.LogWarning("{Capability} result exceeded {Limit} bytes; returning the first errors only.", capabilityId, options.MaxResultBytes);
            json = truncated;
        }

        if (LocalPathGuard.ContainsLocalPath(json, options.ToolsRoot, options.OutputRoot))
        {
            logger.LogWarning("{Capability} result contained a local path and was withheld.", capabilityId);
            return (false, null, $"{capabilityId} result contained a local path and was withheld.");
        }

        return (true, json, null);
    }

    /// <summary>
    /// 只保留前幾筆 errors 與 warnings，附上 total_errors／total_warnings 與 truncated:true；ok 與其他欄位照 CLI 的結果。
    /// 仍放不下時逐次減半，連一筆都放不下才回傳 null。
    /// </summary>
    internal static string? TruncateIssues(JsonObject output, int maxBytes)
    {
        if (output["errors"] is not JsonArray errors)
            return null;

        var warnings = output["warnings"] as JsonArray ?? new JsonArray();
        var totalErrors = ReadCount(output, "total_errors", errors.Count);
        var totalWarnings = ReadCount(output, "total_warnings", warnings.Count);
        var keepErrors = Math.Min(errors.Count, TruncatedErrorCount);
        var keepWarnings = Math.Min(warnings.Count, TruncatedWarningCount);

        while (true)
        {
            var copy = new JsonObject();
            foreach (var (key, value) in output)
            {
                if (key is "errors" or "warnings" or "total_errors" or "total_warnings" or "truncated" or "warnings_truncated")
                    continue;
                copy[key] = value?.DeepClone();
            }

            copy["errors"] = new JsonArray(errors.Take(keepErrors).Select(item => item?.DeepClone()).ToArray());
            copy["warnings"] = new JsonArray(warnings.Take(keepWarnings).Select(item => item?.DeepClone()).ToArray());
            copy["total_errors"] = totalErrors;
            copy["truncated"] = true;
            copy["total_warnings"] = totalWarnings;
            copy["warnings_truncated"] = keepWarnings < totalWarnings;

            var text = copy.ToJsonString();
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
                return text;
            if (keepErrors <= 1 && keepWarnings == 0)
                return null;

            keepErrors = Math.Max(1, keepErrors / 2);
            keepWarnings /= 2;
        }
    }

    private static int ReadCount(JsonObject output, string name, int fallback)
        => output[name] is JsonValue value && value.TryGetValue<int>(out var count) && count >= fallback ? count : fallback;
}
