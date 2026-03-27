using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;

namespace Broker.Handlers.Web;

public sealed class WebSearchDuckDuckGoHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "web_search_duckduckgo";

    private readonly ILogger<WebSearchDuckDuckGoHandler> _logger;

    public WebSearchDuckDuckGoHandler(ILogger<WebSearchDuckDuckGoHandler> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var locale = PayloadHelper.TryGetString(args, "locale") ?? "zh-TW";
        var limit = PayloadHelper.TryGetInt(args, "limit") ?? 5;
        limit = Math.Clamp(limit, 1, 10);

        if (string.IsNullOrWhiteSpace(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            var results = await WebSearchHelper.SearchDuckDuckGoAsync(query, limit, locale);
            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { engine = "duckduckgo", query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"DuckDuckGo search failed: {ex.Message}");
        }
    }
}
