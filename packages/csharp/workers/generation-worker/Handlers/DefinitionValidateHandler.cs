using System.Text.Json.Nodes;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace GenerationWorker.Handlers;

/// <summary>
/// generation.definition.validate（route: validate_definition）：驗證 DefinitionTemplate，無副作用。
/// 轉呼叫 CLI 的 <c>validate</c>（與 build 前的驗證是同一份程式）並原樣回傳其 JSON。
/// 驗證不通過時 CLI 回 <c>ok:false</c> 與結構化 errors，這裡仍回 Success=true，讓代理依錯誤修正定義；
/// 只有內部錯誤（逾時、輸出過大、CLI 結束碼非 0）才回 Success=false。
/// </summary>
public sealed class DefinitionValidateHandler : ICapabilityHandler
{
    public const string Route = "validate_definition";

    private readonly GenerationWorkerOptions _options;
    private readonly IGeneratorCli _cli;
    private readonly ILogger _logger;

    public DefinitionValidateHandler(GenerationWorkerOptions options, IGeneratorCli cli, ILogger logger)
    {
        _options = options;
        _cli = cli;
        _logger = logger;
    }

    public string CapabilityId => "generation.definition.validate";

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

            if (args["template"] is not JsonObject template)
                return Fail("template is required and must be a JSON object.");

            if (!GenerationRequest.TryGetStringArray(args, "page_ids", 64, out var pageIds))
                return Fail("page_ids must be an array of strings of at most 64 characters.");

            // 只轉交認得的鍵。
            var input = new JsonObject { ["template"] = template.DeepClone() };
            if (pageIds != null)
                input["page_ids"] = pageIds;

            var result = await _cli.RunAsync("validate", input, _options.QueryTimeout, ct);
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
