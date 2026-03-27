using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Web;

public sealed class WikipediaSearchHandler : IRouteHandler
{
    public string Route => "knowledge_wikipedia_search";

    private readonly ILogger<WikipediaSearchHandler> _logger;

    public WikipediaSearchHandler(ILogger<WikipediaSearchHandler> logger)
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
            var results = await WebSearchHelper.SearchWikipediaAsync(query, limit, locale);
            return ExecutionResult.Ok(
                request.RequestId,
                JsonSerializer.Serialize(new { engine = "wikipedia", query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Wikipedia search failed: {ex.Message}");
        }
    }
}
