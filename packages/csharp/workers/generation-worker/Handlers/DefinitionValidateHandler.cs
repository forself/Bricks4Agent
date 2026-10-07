using System.Text.Json.Nodes;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace GenerationWorker.Handlers;

/// <summary>
/// generation.definition.validate（route: validate_definition）：驗證 DefinitionTemplate，無副作用。
/// 轉呼叫 CLI 的 <c>validate</c>（與 build 前的驗證是同一份程式）並原樣回傳其 JSON。
/// 驗證不通過時 CLI 回 <c>ok:false</c> 與結構化 errors，這裡仍回 Success=true，讓代理依錯誤修正定義。
/// 結果超過 MaxResultBytes 時改回截斷的結構化結果（前幾筆錯誤、total_errors、truncated:true），仍為 Success=true；
/// 只有內部錯誤（逾時、stdout 超過上限、CLI 結束碼非 0）才回 Success=false。
/// grant scope 帶 <c>max_pages</c> 時（broker 寫入，與 generate 的上限相同），選取的頁數超過它也回 <c>ok:false</c>
/// 與 <c>MAX_PAGES_EXCEEDED</c>，代理不必等到 generate 才知道頁數太多。
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

            // 定義的鍵都是短名稱；過長的鍵在轉交 CLI 之前擋下（CLI 的第 1 層也有相同的上限）。
            if (GenerationRequest.HasOverlongKey(template))
                return Fail($"template contains an object key longer than {GenerationRequest.MaxTemplateKeyLength} characters.");

            if (!GenerationRequest.TryGetStringArray(args, "page_ids", 64, out var pageIds))
                return Fail("page_ids must be an array of strings of at most 64 characters.");

            if (!GenerationRequest.TryReadOptionalMaxPages(scope, out var maxPages))
                return Fail($"Grant scope max_pages must be an integer between 1 and {GenerationRequest.MaxPagesLimit}.");

            // 只轉交認得的鍵。
            var input = new JsonObject { ["template"] = template.DeepClone() };
            if (pageIds != null)
                input["page_ids"] = pageIds;

            var result = await _cli.RunAsync("validate", input, _options.QueryTimeout, ct);
            if (maxPages is { } limit)
                result = EnforceMaxPages(result, limit);
            return GeneratorResponses.PassThrough(result, _options, _logger, CapabilityId, truncateIssues: true);
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

    /// <summary>選取的頁數超過 scope 的 max_pages 時，結果改為 ok:false 並在 errors 最前面加上 MAX_PAGES_EXCEEDED。</summary>
    internal static GeneratorCliResult EnforceMaxPages(GeneratorCliResult result, int maxPages)
    {
        if (!result.Succeeded ||
            result.Output!["pages"] is not JsonArray pages ||
            pages.Count <= maxPages)
        {
            return result;
        }

        var output = (JsonObject)result.Output.DeepClone();
        var errors = output["errors"] as JsonArray ?? new JsonArray();
        output.Remove("errors");
        errors.Insert(0, new JsonObject
        {
            ["code"] = "MAX_PAGES_EXCEEDED",
            ["path"] = "definitions.pages",
            ["message"] = $"This task allows at most {maxPages} pages; the template selects {pages.Count}.",
            ["hint"] = "Merge or remove pages so the template stays within the page limit."
        });
        output["errors"] = errors;
        output["ok"] = false;
        return GeneratorCliResult.FromOutput(output, output.ToJsonString());
    }

    private static (bool, string?, string?) Fail(string message) => (false, null, message);
}
