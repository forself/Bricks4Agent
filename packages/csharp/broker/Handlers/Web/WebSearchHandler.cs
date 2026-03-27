using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;

namespace Broker.Handlers.Web;

public sealed class WebSearchHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "web_search";

    private readonly ILogger<WebSearchHandler> _logger;

    public WebSearchHandler(ILogger<WebSearchHandler> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var limitStr = PayloadHelper.TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 10) : 5;

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            // DuckDuckGo Lite HTML search (no API key)
            var url = $"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(query)}";
            var html = await WebSearchHelper.SharedHttpClient.GetStringAsync(url);

            var results = WebSearchHelper.ParseDuckDuckGoLite(html, limit);

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Web search failed: {ex.Message}");
        }
    }
}
