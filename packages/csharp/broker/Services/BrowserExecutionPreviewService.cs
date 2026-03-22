using System.Net;
using System.Text.RegularExpressions;
using BrokerCore.Contracts;

namespace Broker.Services;

public sealed class BrowserExecutionPreviewService
{
    private static readonly Regex TitleRegex = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ScriptStyleRegex = new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IBrowserExecutionRequestBuilder _builder;
    private readonly HttpClient _httpClient;

    public BrowserExecutionPreviewService(IBrowserExecutionRequestBuilder builder, HttpClient httpClient)
    {
        _builder = builder;
        _httpClient = httpClient;
    }

    public async Task<BrowserPreviewExecutionResult> ExecuteAnonymousReadAsync(
        string toolId,
        BrowserExecutionRequestBuildInput input,
        CancellationToken cancellationToken = default)
    {
        var built = _builder.TryBuild(toolId, input);
        if (!built.Success || built.Request == null)
            return BrowserPreviewExecutionResult.Fail(built.Error ?? "browser_request_build_failed");

        var request = built.Request;
        if (!string.Equals(request.IdentityMode, "anonymous", StringComparison.Ordinal))
            return BrowserPreviewExecutionResult.Fail("browser_preview_only_supports_anonymous");

        if (!string.Equals(request.SiteBindingMode, "public_open", StringComparison.Ordinal))
            return BrowserPreviewExecutionResult.Fail("browser_preview_only_supports_public_open");

        if (!Uri.TryCreate(request.StartUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BrowserPreviewExecutionResult.Fail("browser_preview_invalid_url");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return BrowserPreviewExecutionResult.Fail($"browser_preview_http_{(int)response.StatusCode}");

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var title = ExtractTitle(html);
        var text = ExtractText(html);
        var result = BrowserExecutionResult.Ok(
            request.RequestId,
            request.ToolId,
            request.IntendedActionLevel,
            request.StartUrl,
            title: title,
            contentText: text,
            structuredDataJson: "{\"mode\":\"preview_fetch\"}");

        return BrowserPreviewExecutionResult.Ok(request, result);
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex.Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : string.Empty;
    }

    private static string ExtractText(string html)
    {
        var withoutScripts = ScriptStyleRegex.Replace(html, " ");
        var withoutTags = TagRegex.Replace(withoutScripts, " ");
        var normalized = SpaceRegex.Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
        return normalized.Length <= 4000 ? normalized : normalized[..4000];
    }
}

public sealed class BrowserPreviewExecutionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public BrowserExecutionRequest? Request { get; set; }
    public BrowserExecutionResult? Result { get; set; }

    public static BrowserPreviewExecutionResult Ok(BrowserExecutionRequest request, BrowserExecutionResult result)
        => new()
        {
            Success = true,
            Request = request,
            Result = result
        };

    public static BrowserPreviewExecutionResult Fail(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
