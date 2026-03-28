using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class HighLevelQueryToolMediator
{
    private const string GoogleToolId = "web.search.google";
    private const string DuckDuckGoToolId = "web.search.duckduckgo";
    private const string WikipediaToolId = "knowledge.wikipedia.search";
    private const string RailToolId = "travel.rail.search";
    private const string HsrToolId = "travel.hsr.search";
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
        _ = channel;
        _ = userId;
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(query))
        {
            return HighLevelQueryToolResult.Fail(
                "請提供 ?search 的查詢內容，例如：?search 中央氣象署官網",
                "search_query_missing");
        }

        var googleResult = await SearchWebWithToolAsync(GoogleToolId, query);
        if (googleResult.Success)
            return googleResult;

        _logger.LogWarning(
            "High-level Google web search failed with {Error}; falling back to DuckDuckGo.",
            googleResult.Error ?? "unknown_error");

        var fallbackResult = await SearchWebWithToolAsync(DuckDuckGoToolId, query);
        if (!fallbackResult.Success)
        {
            return HighLevelQueryToolResult.Fail(
                !string.IsNullOrWhiteSpace(googleResult.Error)
                    ? $"受控搜尋失敗：{googleResult.Error}"
                    : "受控搜尋失敗，請稍後再試。",
                googleResult.Error ?? "tool_spec_unavailable");
        }

        if (!string.IsNullOrWhiteSpace(googleResult.Error))
        {
            fallbackResult.Reply = string.Join('\n', new[]
            {
                "Google 搜尋目前失敗，已改用 DuckDuckGo 備援。",
                string.Empty,
                fallbackResult.Reply
            });
        }

        return fallbackResult;
    }

    public Task<HighLevelQueryToolResult> SearchWikipediaAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
    {
        _ = channel;
        _ = userId;
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(HighLevelQueryToolResult.Fail(
                "請提供 Wikipedia 查詢內容，例如：?search 北京市 行政區劃",
                "wikipedia_query_missing"));
        }

        return SearchReadOnlyToolAsync(
            WikipediaToolId,
            query,
            locale: "zh-TW",
            limit: 5,
            safeMode: null,
            unavailableMessage: "目前無法使用 Wikipedia 查詢工具。",
            bindingUnavailableMessage: "目前無法使用 broker-mediated Wikipedia 查詢路徑。",
            dispatchFailurePrefix: "Wikipedia 查詢失敗：");
    }

    public Task<HighLevelQueryToolResult> SearchRailAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
        => SearchTransportAsync(channel, userId, RailToolId, query, cancellationToken);

    public Task<HighLevelQueryToolResult> SearchHsrAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
        => SearchTransportAsync(channel, userId, HsrToolId, query, cancellationToken);

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

    private Task<HighLevelQueryToolResult> SearchWebWithToolAsync(string toolId, string query)
        => SearchReadOnlyToolAsync(
            toolId,
            query,
            locale: "zh-TW",
            limit: 5,
            safeMode: "moderate",
            unavailableMessage: "目前無法使用受控搜尋工具。",
            bindingUnavailableMessage: "目前無法使用 broker-mediated web search route。",
            dispatchFailurePrefix: "搜尋失敗：");

    private async Task<HighLevelQueryToolResult> SearchReadOnlyToolAsync(
        string toolId,
        string query,
        string locale,
        int limit,
        string? safeMode,
        string unavailableMessage,
        string bindingUnavailableMessage,
        string dispatchFailurePrefix)
    {
        var spec = _toolSpecRegistry.Get(toolId);
        if (spec == null || !string.Equals(spec.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return HighLevelQueryToolResult.Fail(
                unavailableMessage,
                "tool_spec_unavailable");
        }

        var binding = spec.CapabilityBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.CapabilityId, toolId, StringComparison.OrdinalIgnoreCase));
        if (binding == null || !binding.Registered || string.IsNullOrWhiteSpace(binding.Route))
        {
            return HighLevelQueryToolResult.Fail(
                bindingUnavailableMessage,
                "tool_binding_unavailable");
        }

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = query,
            ["locale"] = locale,
            ["limit"] = limit
        };
        if (!string.IsNullOrWhiteSpace(safeMode))
            args["safe_mode"] = safeMode;

        // EXC-HLQM-BYPASS: 暫時例外，Phase 2 將消除此 bypass
        ApprovedRequest.WarnIfBypass();
        var approvedRequest = new ApprovedRequest
        {
            RequestId = $"hlq_{Guid.NewGuid():N}"[..18],
            CapabilityId = binding.CapabilityId,
            Route = binding.Route,
            PrincipalId = "system:high-level-coordinator",
            TaskId = "global",
            SessionId = "high-level-query",
            Scope = "{}",
            Payload = JsonSerializer.Serialize(new
            {
                route = binding.Route,
                args
            })
        };

        var executionResult = await _dispatcher.DispatchAsync(approvedRequest);
        if (!executionResult.Success || string.IsNullOrWhiteSpace(executionResult.ResultPayload))
        {
            var error = executionResult.ErrorMessage ?? "tool_dispatch_failed";
            _logger.LogWarning("High-level read-only tool {ToolId} failed: {Error}", toolId, error);
            return HighLevelQueryToolResult.Fail(
                $"{dispatchFailurePrefix}{error}",
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

            return new HighLevelQueryToolResult
            {
                Success = true,
                Reply = BuildSearchReply(engine, returnedQuery, items),
                ToolId = toolId,
                Engine = engine,
                Results = items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse high-level query tool result for {ToolId}", toolId);
            return HighLevelQueryToolResult.Fail(
                "查詢工具回傳格式無法解析。",
                "tool_result_parse_failed");
        }
    }

    private async Task<HighLevelQueryToolResult> SearchTransportAsync(
        string channel,
        string userId,
        string toolId,
        string query,
        CancellationToken cancellationToken = default)
    {
        _ = channel;
        _ = userId;
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(query))
        {
            return HighLevelQueryToolResult.Fail(
                "請提供交通查詢內容，例如：?rail 台北 台中 今天 18:00",
                "transport_query_missing");
        }

        var spec = _toolSpecRegistry.Get(toolId);
        if (spec == null || !string.Equals(spec.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return HighLevelQueryToolResult.Fail(
                "目前無法使用這個交通查詢工具。",
                "transport_tool_unavailable");
        }

        var binding = spec.CapabilityBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.CapabilityId, toolId, StringComparison.OrdinalIgnoreCase));
        if (binding == null || !binding.Registered || string.IsNullOrWhiteSpace(binding.Route))
        {
            return HighLevelQueryToolResult.Fail(
                "目前無法使用 broker-mediated 交通查詢 route。",
                "transport_binding_unavailable");
        }

        var approvedRequest = new ApprovedRequest
        {
            RequestId = $"hlt_{Guid.NewGuid():N}"[..18],
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

            // TDX 結構化結果（優先路徑）
            if (doc.RootElement.TryGetProperty("tdx", out var tdxNode) &&
                tdxNode.ValueKind == JsonValueKind.Object)
            {
                var tdxItems = ParseTdxTimetableResult(tdxNode);
                var tdxSources = doc.RootElement.TryGetProperty("sources_used", out var tdxSourcesNode) &&
                                 tdxSourcesNode.ValueKind == JsonValueKind.Array
                    ? string.Join("、", tdxSourcesNode.EnumerateArray()
                        .Select(node => node.GetString())
                        .Where(text => !string.IsNullOrWhiteSpace(text)))
                    : "TDX";
                var tdxRetrievedAt = doc.RootElement.TryGetProperty("retrieved_at", out var tdxRetrievedAtNode)
                    ? tdxRetrievedAtNode.GetString() ?? string.Empty
                    : string.Empty;

                return new HighLevelQueryToolResult
                {
                    Success = true,
                    Reply = BuildTdxTimetableReply(mode, returnedQuery, tdxNode, tdxSources, tdxRetrievedAt),
                    ToolId = toolId,
                    Engine = mode,
                    Results = tdxItems
                };
            }

            // Fallback: 網頁搜尋結果
            var items = new List<HighLevelQuerySearchResult>();
            if (doc.RootElement.TryGetProperty("results", out var resultsNode) &&
                resultsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in resultsNode.EnumerateArray())
                {
                    var timeCandidates = item.TryGetProperty("time_candidates", out var timeNode) &&
                                         timeNode.ValueKind == JsonValueKind.Array
                        ? string.Join("、", timeNode.EnumerateArray()
                            .Select(element => element.GetString())
                            .Where(text => !string.IsNullOrWhiteSpace(text)))
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
                ? string.Join("、", sourcesNode.EnumerateArray()
                    .Select(node => node.GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text)))
                : string.Empty;

            return new HighLevelQueryToolResult
            {
                Success = true,
                Reply = BuildTransportReply(mode, returnedQuery, items, sources, retrievedAt),
                ToolId = toolId,
                Engine = mode,
                Results = items
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse high-level transport result for {ToolId}", toolId);
            return HighLevelQueryToolResult.Fail(
                "交通查詢結果無法解析。",
                "transport_result_parse_failed");
        }
    }

    internal static string BuildSearchReply(
        string engine,
        string query,
        IReadOnlyList<HighLevelQuerySearchResult> results)
    {
        var lines = new List<string>
        {
            $"已使用 {engine} 搜尋：{query}",
            string.Empty
        };

        if (results.Count == 0)
        {
            lines.Add("目前沒有取得可用結果。你可以嘗試：");
            lines.Add("1. 用更具體的關鍵詞");
            lines.Add("2. 加入時間或地點限定");
            lines.Add("3. 用英文搜尋");
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

    internal static string BuildTransportReply(
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
            lines.Add("目前沒有取得可用班次結果。");
            lines.Add("建議提供更完整的查詢條件，例如：");
            lines.Add("?rail 台北 台中 今天 18:00");
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

    /// <summary>解析 TDX 結構化時刻表結果為 HighLevelQuerySearchResult 列表</summary>
    private static List<HighLevelQuerySearchResult> ParseTdxTimetableResult(JsonElement tdxNode)
    {
        var items = new List<HighLevelQuerySearchResult>();

        if (!tdxNode.TryGetProperty("trains", out var trainsNode) || trainsNode.ValueKind != JsonValueKind.Array)
            return items;

        var rank = 1;
        foreach (var train in trainsNode.EnumerateArray())
        {
            var trainNo = train.TryGetProperty("train_no", out var noNode) ? noNode.GetString() ?? "" : "";
            var trainType = train.TryGetProperty("train_type", out var typeNode) ? typeNode.GetString() ?? "" : "";
            var departure = train.TryGetProperty("departure_time", out var depNode) ? depNode.GetString() ?? "" : "";
            var arrival = train.TryGetProperty("arrival_time", out var arrNode) ? arrNode.GetString() ?? "" : "";

            var title = string.IsNullOrWhiteSpace(trainType)
                ? $"車次 {trainNo}"
                : $"{trainType} {trainNo}";

            items.Add(new HighLevelQuerySearchResult
            {
                Rank = rank++,
                Title = title,
                Snippet = $"{departure} 發車 → {arrival} 到達"
            });
        }

        return items;
    }

    /// <summary>為 TDX 結構化時刻表結果建立人類可讀的回覆</summary>
    internal static string BuildTdxTimetableReply(
        string mode,
        string query,
        JsonElement tdxNode,
        string sources,
        string retrievedAt)
    {
        var origin = tdxNode.TryGetProperty("origin", out var origNode) ? origNode.GetString() ?? "" : "";
        var destination = tdxNode.TryGetProperty("destination", out var destNode) ? destNode.GetString() ?? "" : "";
        var date = tdxNode.TryGetProperty("date", out var dateNode) ? dateNode.GetString() ?? "" : "";
        var trainCount = tdxNode.TryGetProperty("train_count", out var countNode) && countNode.TryGetInt32(out var count) ? count : 0;

        var modeLabel = mode switch
        {
            "rail" => "台鐵",
            "hsr" => "高鐵",
            _ => mode
        };

        var lines = new List<string>
        {
            $"{modeLabel}時刻表：{origin} → {destination}（{date}）",
            $"共 {trainCount} 班，來源：{sources}"
        };

        if (!string.IsNullOrWhiteSpace(retrievedAt))
            lines.Add($"查詢時間：{retrievedAt}");

        lines.Add(string.Empty);

        if (!tdxNode.TryGetProperty("trains", out var trainsNode) || trainsNode.ValueKind != JsonValueKind.Array)
        {
            lines.Add("無班次資料。");
            return string.Join('\n', lines);
        }

        var displayed = 0;
        foreach (var train in trainsNode.EnumerateArray())
        {
            if (displayed >= 15) // 最多顯示 15 班
            {
                lines.Add($"...（還有 {trainCount - displayed} 班）");
                break;
            }

            var trainNo = train.TryGetProperty("train_no", out var noNode) ? noNode.GetString() ?? "" : "";
            var trainType = train.TryGetProperty("train_type", out var typeNode) ? typeNode.GetString() ?? "" : "";
            var departure = train.TryGetProperty("departure_time", out var depNode) ? depNode.GetString() ?? "" : "";
            var arrival = train.TryGetProperty("arrival_time", out var arrNode) ? arrNode.GetString() ?? "" : "";

            var typePrefix = string.IsNullOrWhiteSpace(trainType) ? "" : $"[{trainType}] ";
            lines.Add($"  {typePrefix}{trainNo}  {departure} → {arrival}");
            displayed++;
        }

        return string.Join('\n', lines);
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
