using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class HighLevelQueryToolMediator
{
    private const string DuckDuckGoToolId = "web.search.duckduckgo";

    private readonly IToolSpecRegistry _toolSpecRegistry;
    private readonly IExecutionDispatcher _dispatcher;
    private readonly ILogger<HighLevelQueryToolMediator> _logger;

    public HighLevelQueryToolMediator(
        IToolSpecRegistry toolSpecRegistry,
        IExecutionDispatcher dispatcher,
        ILogger<HighLevelQueryToolMediator> logger)
    {
        _toolSpecRegistry = toolSpecRegistry;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<HighLevelQueryToolResult> SearchWebAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(query))
        {
            return HighLevelQueryToolResult.Fail(
                "\u8acb\u4f7f\u7528 ?search \u95dc\u9375\u5b57\uff0c\u4f8b\u5982 ?search \u53f0\u5317\u4eca\u5929\u5929\u6c23\u3002",
                "search_query_missing");
        }

        var spec = _toolSpecRegistry.Get(DuckDuckGoToolId);
        if (spec == null || !string.Equals(spec.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return HighLevelQueryToolResult.Fail(
                "\u76ee\u524d\u6c92\u6709\u53ef\u7528\u7684 web search \u5de5\u5177\u3002",
                "tool_spec_unavailable");
        }

        var binding = spec.CapabilityBindings.FirstOrDefault(binding =>
            string.Equals(binding.CapabilityId, DuckDuckGoToolId, StringComparison.OrdinalIgnoreCase));
        if (binding == null || !binding.Registered || string.IsNullOrWhiteSpace(binding.Route))
        {
            return HighLevelQueryToolResult.Fail(
                "\u76ee\u524d\u6c92\u6709\u53ef\u7528\u7684 broker-mediated web search route\u3002",
                "tool_binding_unavailable");
        }

        var requestId = $"hlq_{Guid.NewGuid():N}"[..18];
        var approvedRequest = new ApprovedRequest
        {
            RequestId = requestId,
            CapabilityId = binding.CapabilityId,
            Route = binding.Route,
            PrincipalId = "system:high-level-coordinator",
            TaskId = "global",
            SessionId = "high-level-query",
            Scope = "{}",
            Payload = JsonSerializer.Serialize(new
            {
                route = binding.Route,
                args = new
                {
                    query,
                    locale = "zh-TW",
                    limit = 5
                }
            })
        };

        var executionResult = await _dispatcher.DispatchAsync(approvedRequest);
        if (!executionResult.Success || string.IsNullOrWhiteSpace(executionResult.ResultPayload))
        {
            var error = executionResult.ErrorMessage ?? "tool_dispatch_failed";
            _logger.LogWarning("High-level web search failed: {Error}", error);
            return HighLevelQueryToolResult.Fail(
                $"\u641c\u5c0b\u5931\u6557\uff1a{error}",
                "tool_dispatch_failed");
        }

        try
        {
            using var doc = JsonDocument.Parse(executionResult.ResultPayload);
            var engine = doc.RootElement.TryGetProperty("engine", out var engineNode)
                ? engineNode.GetString() ?? "duckduckgo"
                : "duckduckgo";
            var returnedQuery = doc.RootElement.TryGetProperty("query", out var queryNode)
                ? queryNode.GetString() ?? query
                : query;

            var items = new List<HighLevelQuerySearchResult>();
            if (doc.RootElement.TryGetProperty("results", out var resultsNode) &&
                resultsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsNode.EnumerateArray())
                {
                    items.Add(new HighLevelQuerySearchResult
                    {
                        Rank = item.TryGetProperty("rank", out var rankNode) && rankNode.TryGetInt32(out var rank)
                            ? rank
                            : items.Count + 1,
                        Title = item.TryGetProperty("title", out var titleNode)
                            ? titleNode.GetString() ?? string.Empty
                            : string.Empty,
                        Url = item.TryGetProperty("url", out var urlNode)
                            ? urlNode.GetString() ?? string.Empty
                            : string.Empty,
                        Snippet = item.TryGetProperty("snippet", out var snippetNode)
                            ? snippetNode.GetString() ?? string.Empty
                            : string.Empty
                    });
                }
            }

            var reply = BuildSearchReply(engine, returnedQuery, items);
            return new HighLevelQueryToolResult
            {
                Success = true,
                Reply = reply,
                ToolId = DuckDuckGoToolId,
                Engine = engine,
                Results = items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse high-level query tool result");
            return HighLevelQueryToolResult.Fail(
                "\u641c\u5c0b\u5de5\u5177\u7684\u56de\u50b3\u7d50\u679c\u7121\u6cd5\u89e3\u6790\u3002",
                "tool_result_parse_failed");
        }
    }

    private static string BuildSearchReply(string engine, string query, IReadOnlyList<HighLevelQuerySearchResult> results)
    {
        if (results.Count == 0)
        {
            return string.Join('\n', new[]
            {
                $"\u5df2\u4f7f\u7528 {engine} \u641c\u5c0b\uff1a{query}",
                "\u76ee\u524d\u6c92\u6709\u627e\u5230\u660e\u78ba\u7d50\u679c\u3002"
            });
        }

        var lines = new List<string>
        {
            $"\u5df2\u4f7f\u7528 {engine} \u641c\u5c0b\uff1a{query}",
            ""
        };

        foreach (var result in results)
        {
            lines.Add($"{result.Rank}. {result.Title}");
            lines.Add(result.Url);
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                lines.Add(result.Snippet);
            lines.Add(string.Empty);
        }

        return string.Join('\n', lines.Where(line => line != null));
    }
}

public sealed class HighLevelQueryToolResult
{
    public bool Success { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public IReadOnlyList<HighLevelQuerySearchResult> Results { get; set; } = Array.Empty<HighLevelQuerySearchResult>();

    public static HighLevelQueryToolResult Fail(string reply, string error)
        => new()
        {
            Success = false,
            Reply = reply,
            Error = error
        };
}

public sealed class HighLevelQuerySearchResult
{
    public int Rank { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}
