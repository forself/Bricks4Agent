using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;

namespace Broker.Handlers.Travel;

/// <summary>
/// Shared travel search logic extracted from InProcessDispatcher.ExecuteTravelSearchAsync.
/// Used by all travel route handlers.
/// </summary>
internal static class TravelSearchHelper
{
    public static async Task<ExecutionResult> ExecuteTravelSearchAsync(
        ApprovedRequest request,
        string mode,
        string sourceLabel,
        Func<string, string> queryDecorator,
        ILogger logger)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var locale = PayloadHelper.TryGetString(args, "locale") ?? "zh-TW";
        var limit = PayloadHelper.TryGetInt(args, "limit") ?? 5;
        limit = Math.Clamp(limit, 1, 5);

        if (string.IsNullOrWhiteSpace(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            var searchResults = await WebSearchHelper.SearchDuckDuckGoAsync(queryDecorator(query), limit, locale);
            var normalizedResults = new List<object>();
            foreach (var searchResult in searchResults.Take(limit))
            {
                var resultJson = JsonSerializer.Serialize(searchResult);
                using var resultDoc = JsonDocument.Parse(resultJson);
                var root = resultDoc.RootElement;
                var url = PayloadHelper.TryGetString(root, "url") ?? string.Empty;
                var snippet = PayloadHelper.TryGetString(root, "snippet") ?? string.Empty;
                var title = PayloadHelper.TryGetString(root, "title") ?? string.Empty;
                var rank = PayloadHelper.TryGetInt(root, "rank") ?? (normalizedResults.Count + 1);
                var timeCandidates = new List<string>();

                if (!string.IsNullOrWhiteSpace(snippet))
                    timeCandidates.AddRange(WebSearchHelper.ExtractTimeCandidates(snippet));

                if (timeCandidates.Count < 4 && !string.IsNullOrWhiteSpace(url))
                {
                    try
                    {
                        var html = await WebSearchHelper.SharedHttpClient.GetStringAsync(url);
                        var content = WebSearchHelper.HtmlToText(html);
                        timeCandidates.AddRange(WebSearchHelper.ExtractTimeCandidates(content));
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Skipping follow fetch for travel result {Url}", url);
                    }
                }

                normalizedResults.Add(new
                {
                    rank,
                    title,
                    url,
                    snippet,
                    time_candidates = timeCandidates
                        .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToArray()
                });
            }

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new
                {
                    mode,
                    query,
                    retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                    results = normalizedResults,
                    sources_used = new[] { sourceLabel }
                }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"{mode} travel search failed: {ex.Message}");
        }
    }
}
