using System.Text;
using System.Text.Json;
using BrokerCore.Contracts;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Handlers.Web;

public sealed class WebSearchGoogleHandler : BrokerCore.Services.IRouteHandler
{
    public string Route => "web_search_google";

    private readonly ILogger<WebSearchGoogleHandler> _logger;
    private readonly IProcessRunner? _processRunner;

    public WebSearchGoogleHandler(ILogger<WebSearchGoogleHandler> logger, IProcessRunner? processRunner = null)
    {
        _logger = logger;
        _processRunner = processRunner;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var query = PayloadHelper.TryGetString(args, "query") ?? "";
        var locale = PayloadHelper.TryGetString(args, "locale") ?? "zh-TW";
        var safeMode = PayloadHelper.TryGetString(args, "safe_mode") ?? "moderate";
        var limit = PayloadHelper.TryGetInt(args, "limit") ?? 5;
        limit = Math.Clamp(limit, 1, 10);

        if (string.IsNullOrWhiteSpace(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            var results = await SearchGoogleWithFallbackAsync(query, limit, locale, safeMode);
            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { engine = "google", query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Google search failed: {ex.Message}");
        }
    }

    private async Task<List<object>> SearchGoogleWithFallbackAsync(string query, int limit, string locale, string safeMode)
    {
        var browserResults = await TrySearchGoogleWithBrowserAsync(query, limit, locale, safeMode);
        if (browserResults.Count > 0)
            return browserResults;

        return await WebSearchHelper.SearchGoogleAsync(query, limit, locale, safeMode);
    }

    private async Task<List<object>> TrySearchGoogleWithBrowserAsync(string query, int limit, string locale, string safeMode)
    {
        if (_processRunner == null)
            return new List<object>();

        var repoRoot = TryResolveRepoRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
            return new List<object>();

        var scriptPath = Path.Combine(repoRoot, "tools", "scripts", "google-search-browser.mjs");
        if (!System.IO.File.Exists(scriptPath))
            return new List<object>();

        var result = await _processRunner.RunAsync(new ProcessRunSpec
        {
            FileName = "node",
            Arguments =
                $"\"{scriptPath}\" --query-base64-utf8 \"{Convert.ToBase64String(Encoding.UTF8.GetBytes(query))}\" --locale \"{locale}\" --limit {limit} --safe-mode {safeMode}",
            WorkingDirectory = repoRoot
        });

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _logger.LogWarning(
                "Google browser search helper failed with exit {ExitCode}: {Error}",
                result.ExitCode,
                string.IsNullOrWhiteSpace(result.StandardError) ? "no stderr" : result.StandardError.Trim());
            return new List<object>();
        }

        using var doc = JsonDocument.Parse(result.StandardOutput);
        if (!doc.RootElement.TryGetProperty("results", out var resultsNode) ||
            resultsNode.ValueKind != JsonValueKind.Array)
        {
            return new List<object>();
        }

        var results = new List<object>();
        foreach (var item in resultsNode.EnumerateArray())
        {
            results.Add(new
            {
                rank = item.TryGetProperty("rank", out var rankNode) && rankNode.TryGetInt32(out var rank) ? rank : results.Count + 1,
                title = item.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? string.Empty : string.Empty,
                url = item.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty,
                snippet = item.TryGetProperty("snippet", out var snippetNode) ? snippetNode.GetString() ?? string.Empty : string.Empty
            });
        }

        return results;
    }

    private static string? TryResolveRepoRoot()
    {
        foreach (var candidate in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory()
                 })
        {
            var current = new DirectoryInfo(candidate);
            while (current != null)
            {
                if (System.IO.File.Exists(Path.Combine(current.FullName, "package.json")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        return null;
    }
}
