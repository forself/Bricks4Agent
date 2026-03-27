using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore.Contracts;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Handlers.Web;

public sealed class WebFetchHandler : IRouteHandler
{
    public string Route => "web_fetch";

    private readonly ILogger<WebFetchHandler> _logger;

    public WebFetchHandler(ILogger<WebFetchHandler> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = PayloadHelper.GetArgsElement(doc.RootElement);
        var urlStr = PayloadHelper.TryGetString(args, "url") ?? "";
        var maxLenStr = PayloadHelper.TryGetString(args, "max_length");
        var maxLen = int.TryParse(maxLenStr, out var ml) ? ml : 50000;

        if (string.IsNullOrEmpty(urlStr))
            return ExecutionResult.Fail(request.RequestId, "url is required.");

        if (!Uri.TryCreate(urlStr, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return ExecutionResult.Fail(request.RequestId, "Invalid URL. Only http/https allowed.");

        try
        {
            var html = await WebSearchHelper.SharedHttpClient.GetStringAsync(uri);

            // HTML -> plain text
            var text = WebSearchHelper.HtmlToText(html);
            if (text.Length > maxLen)
                text = text[..maxLen] + "\n... [truncated]";

            // Extract title
            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success ? System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim() : "";

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { url = urlStr, title, content = text, length = text.Length }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Web fetch failed: {ex.Message}");
        }
    }
}
