namespace SiteCrawlerWorker.Services;

public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct);
}

public sealed record PageFetchResult(
    bool IsSuccess,
    Uri Uri,
    Uri FinalUri,
    int StatusCode,
    string ContentType,
    string Html,
    string Error,
    Uri? RedirectUri = null)
{
    public string FailureReason => Error;

    public static PageFetchResult Ok(Uri finalUri, int statusCode, string contentType, string html)
    {
        ArgumentNullException.ThrowIfNull(finalUri);

        return new PageFetchResult(
            true,
            finalUri,
            finalUri,
            statusCode,
            contentType,
            html,
            string.Empty);
    }

    public static PageFetchResult Ok(Uri finalUri, int statusCode, string html)
    {
        return Ok(finalUri, statusCode, string.Empty, html);
    }

    public static PageFetchResult Fail(Uri finalUri, int statusCode, string failureReason)
    {
        ArgumentNullException.ThrowIfNull(finalUri);

        return new PageFetchResult(
            false,
            finalUri,
            finalUri,
            statusCode,
            string.Empty,
            string.Empty,
            failureReason);
    }

    public static PageFetchResult Fail(
        Uri uri,
        Uri finalUri,
        int statusCode,
        string contentType,
        string failureReason)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(finalUri);

        return new PageFetchResult(
            false,
            uri,
            finalUri,
            statusCode,
            contentType,
            string.Empty,
            failureReason);
    }

    public static PageFetchResult Redirect(Uri uri, Uri finalUri, int statusCode, Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(finalUri);
        ArgumentNullException.ThrowIfNull(redirectUri);

        return new PageFetchResult(
            false,
            uri,
            finalUri,
            statusCode,
            string.Empty,
            string.Empty,
            "redirect_not_followed",
            redirectUri);
    }
}

// Supply an HttpClient whose handler has AllowAutoRedirect = false. The fetcher
// never recursively follows 3xx responses it receives.
public sealed class HttpPageFetcher : IPageFetcher
{
    private readonly HttpClient httpClient;

    public HttpPageFetcher(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var statusCode = (int)response.StatusCode;
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (IsRedirect(statusCode) && response.Headers.Location is not null)
        {
            return PageFetchResult.Redirect(uri, finalUri, statusCode, ResolveRedirectUri(finalUri, response.Headers.Location));
        }

        if (!response.IsSuccessStatusCode)
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, mediaType, $"http_status_{statusCode}");
        }

        if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, mediaType, "non_html_content_type");
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        return new PageFetchResult(
            true,
            uri,
            finalUri,
            statusCode,
            mediaType,
            html,
            string.Empty);
    }

    private static bool IsRedirect(int statusCode)
    {
        return statusCode >= 300 && statusCode <= 399;
    }

    private static Uri ResolveRedirectUri(Uri baseUri, Uri location)
    {
        return location.IsAbsoluteUri ? location : new Uri(baseUri, location);
    }
}
