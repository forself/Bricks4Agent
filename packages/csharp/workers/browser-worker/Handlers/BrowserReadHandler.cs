using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace BrowserWorker.Handlers;

/// <summary>
/// Handles browser_read requests: navigates to a URL using Playwright,
/// extracts page content (title, text, description), and returns structured result.
/// </summary>
public sealed class BrowserReadHandler : ICapabilityHandler
{
    public string CapabilityId => "browser.read";

    private readonly PlaywrightBrowserService _browserService;
    private readonly ILogger<BrowserReadHandler> _logger;

    public BrowserReadHandler(PlaywrightBrowserService browserService, ILogger<BrowserReadHandler> logger)
    {
        _browserService = browserService;
        _logger = logger;
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        string? url = null;
        string? userAgent = null;

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (root.TryGetProperty("args", out var args))
                {
                    url = TryGetString(args, "url") ?? TryGetString(args, "start_url");
                    userAgent = TryGetString(args, "user_agent");
                }

                url ??= TryGetString(root, "url") ?? TryGetString(root, "start_url");
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse browser_read payload");
            }
        }

        if (string.IsNullOrWhiteSpace(url))
            return (false, null, "url is required.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, null, "url must be an absolute http or https URL.");
        }

        _logger.LogInformation("BrowserReadHandler: fetching {Url}", url);

        var pageResult = await _browserService.FetchPageAsync(url, userAgent, ct);

        if (!pageResult.Success)
            return (false, null, pageResult.Error ?? "browser_fetch_failed");

        var result = JsonSerializer.Serialize(new
        {
            request_id = requestId,
            status_code = pageResult.StatusCode,
            final_url = pageResult.FinalUrl,
            title = pageResult.Title,
            description = pageResult.Description,
            content_text = pageResult.TextContent,
            fetched_at = pageResult.FetchedAt,
            has_screenshot = pageResult.Screenshot != null
        });

        return (true, result, null);
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
