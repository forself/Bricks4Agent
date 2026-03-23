using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class HighLevelQueryToolMediator
{
    private const string GoogleToolId = "web.search.google";
    private const string DuckDuckGoToolId = "web.search.duckduckgo";
    private const string RailToolId = "travel.rail.search";
    private const string BusToolId = "travel.bus.search";
    private const string FlightToolId = "travel.flight.search";

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

        var primaryResult = await SearchWebWithToolAsync(GoogleToolId, query);
        if (primaryResult.Success)
            return primaryResult;

        _logger.LogWarning(
            "High-level Google web search failed with {Error}; falling back to DuckDuckGo.",
            primaryResult.Error ?? "unknown_error");

        var fallbackResult = await SearchWebWithToolAsync(DuckDuckGoToolId, query);
        if (fallbackResult.Success)
        {
            if (!string.IsNullOrWhiteSpace(primaryResult.Error))
            {
                fallbackResult.Reply = string.Join('\n', new[]
                {
                    "Google 搜尋目前不可用，已改用 DuckDuckGo 作為備援。",
                    string.Empty,
                    fallbackResult.Reply
                });
            }

            return fallbackResult;
        }

        return HighLevelQueryToolResult.Fail(
            !string.IsNullOrWhiteSpace(primaryResult.Error)
                ? $"搜尋失敗：{primaryResult.Error}"
                : "目前沒有可用的 web search 工具。",
            primaryResult.Error ?? "tool_spec_unavailable");
    }

    public Task<HighLevelQueryToolResult> SearchRailAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
        => SearchTransportAsync(channel, userId, RailToolId, query, cancellationToken);

    public Task<HighLevelQueryToolResult> SearchBusAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
        => SearchTransportAsync(channel, userId, BusToolId, query, cancellationToken);

    public Task<HighLevelQueryToolResult> SearchFlightAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
        => SearchTransportAsync(channel, userId, FlightToolId, query, cancellationToken);

    private async Task<HighLevelQueryToolResult> SearchWebWithToolAsync(string toolId, string query)
    {
        var spec = _toolSpecRegistry.Get(toolId);
        if (spec == null || !string.Equals(spec.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return HighLevelQueryToolResult.Fail(
                "目前沒有可用的 web search 工具。",
                "tool_spec_unavailable");
        }

        var binding = spec.CapabilityBindings.FirstOrDefault(binding =>
            string.Equals(binding.CapabilityId, toolId, StringComparison.OrdinalIgnoreCase));
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
                    limit = 5,
                    safe_mode = "moderate"
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
                ? engineNode.GetString() ?? toolId
                : toolId;
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
                ToolId = toolId,
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

    private async Task<HighLevelQueryToolResult> SearchTransportAsync(
        string channel,
        string userId,
        string toolId,
        string query,
        CancellationToken cancellationToken)
    {
        _ = channel;
        _ = userId;
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(query))
        {
            return HighLevelQueryToolResult.Fail(
                "請在查詢指令後提供條件，例如 ?rail 台北 台中 今天 18:00。",
                "transport_query_missing");
        }

        var spec = _toolSpecRegistry.Get(toolId);
        if (spec == null || !string.Equals(spec.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return HighLevelQueryToolResult.Fail(
                "目前沒有可用的交通查詢工具。",
                "transport_tool_unavailable");
        }

        var binding = spec.CapabilityBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.CapabilityId, toolId, StringComparison.OrdinalIgnoreCase));
        if (binding == null || !binding.Registered || string.IsNullOrWhiteSpace(binding.Route))
        {
            return HighLevelQueryToolResult.Fail(
                "目前沒有可用的 broker-mediated 交通查詢 route。",
                "transport_binding_unavailable");
        }

        var requestId = $"hlt_{Guid.NewGuid():N}"[..18];
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
            var error = executionResult.ErrorMessage ?? "transport_tool_dispatch_failed";
            _logger.LogWarning("High-level transport search failed for {ToolId}: {Error}", toolId, error);
            return HighLevelQueryToolResult.Fail(
                $"交通查詢失敗：{error}",
                "transport_tool_dispatch_failed");
        }

        try
        {
            using var doc = JsonDocument.Parse(executionResult.ResultPayload);
            var mode = doc.RootElement.TryGetProperty("mode", out var modeNode)
                ? modeNode.GetString() ?? toolId
                : toolId;
            var returnedQuery = doc.RootElement.TryGetProperty("query", out var queryNode)
                ? queryNode.GetString() ?? query
                : query;

            var items = new List<HighLevelQuerySearchResult>();
            if (doc.RootElement.TryGetProperty("results", out var resultsNode) &&
                resultsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsNode.EnumerateArray())
                {
                    var timeCandidates = item.TryGetProperty("time_candidates", out var timeNode) &&
                                         timeNode.ValueKind == JsonValueKind.Array
                        ? string.Join("、", timeNode.EnumerateArray().Select(element => element.GetString()).Where(text => !string.IsNullOrWhiteSpace(text)))
                        : string.Empty;
                    var snippet = item.TryGetProperty("snippet", out var snippetNode)
                        ? snippetNode.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(timeCandidates))
                    {
                        snippet = string.IsNullOrWhiteSpace(snippet)
                            ? $"候選時間：{timeCandidates}"
                            : $"{snippet}\n候選時間：{timeCandidates}";
                    }

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
                        Snippet = snippet
                    });
                }
            }

            var retrievedAt = doc.RootElement.TryGetProperty("retrieved_at", out var retrievedAtNode)
                ? retrievedAtNode.GetString() ?? string.Empty
                : string.Empty;
            var sources = doc.RootElement.TryGetProperty("sources_used", out var sourcesNode) &&
                          sourcesNode.ValueKind == JsonValueKind.Array
                ? string.Join("、", sourcesNode.EnumerateArray().Select(node => node.GetString()).Where(text => !string.IsNullOrWhiteSpace(text)))
                : string.Empty;

            var reply = BuildTransportReply(mode, returnedQuery, items, sources, retrievedAt);
            return new HighLevelQueryToolResult
            {
                Success = true,
                Reply = reply,
                ToolId = toolId,
                Engine = mode,
                Results = items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse high-level transport result for {ToolId}", toolId);
            return HighLevelQueryToolResult.Fail(
                "交通查詢工具的回傳結果無法解析。",
                "transport_result_parse_failed");
        }
    }

    private static string BuildTransportReply(
        string mode,
        string query,
        IReadOnlyList<HighLevelQuerySearchResult> results,
        string sources,
        string retrievedAt)
    {
        var lines = new List<string>
        {
            $"已使用 {mode} 交通查詢：{query}"
        };

        if (!string.IsNullOrWhiteSpace(retrievedAt))
            lines.Add($"查詢時間：{retrievedAt}");

        if (!string.IsNullOrWhiteSpace(sources))
            lines.Add($"來源：{sources}");

        lines.Add(string.Empty);

        if (results.Count == 0)
        {
            lines.Add("目前沒有取得明確班次候選。可改用更完整條件，例如起訖地、日期與時間。");
            return string.Join('\n', lines);
        }

        foreach (var result in results)
        {
            lines.Add($"{result.Rank}. {result.Title}");
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                lines.Add(result.Snippet);
            if (!string.IsNullOrWhiteSpace(result.Url))
                lines.Add(result.Url);
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
