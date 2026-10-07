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
    public static (bool Success, string? ResultPayload, string? Error) PassThrough(
        GeneratorCliResult result, GenerationWorkerOptions options, ILogger logger, string capabilityId)
    {
        if (!result.Succeeded)
            return (false, null, result.Failure);

        var json = result.RawJson!;
        if (Encoding.UTF8.GetByteCount(json) > options.MaxResultBytes)
            return (false, null, $"{capabilityId} result exceeded {options.MaxResultBytes} bytes.");

        if (LocalPathGuard.ContainsLocalPath(json, options.ToolsRoot, options.OutputRoot))
        {
            logger.LogWarning("{Capability} result contained a local path and was withheld.", capabilityId);
            return (false, null, $"{capabilityId} result contained a local path and was withheld.");
        }

        return (true, json, null);
    }
}
