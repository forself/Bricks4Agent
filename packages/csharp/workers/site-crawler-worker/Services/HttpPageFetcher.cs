namespace SiteCrawlerWorker.Services;

public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct);
}

public sealed record PageFetchResult(
    bool IsSuccess,
    Uri FinalUri,
    int StatusCode,
    string Html,
    string FailureReason)
{
    public static PageFetchResult Ok(Uri finalUri, int statusCode, string html)
    {
        ArgumentNullException.ThrowIfNull(finalUri);

        return new PageFetchResult(true, finalUri, statusCode, html, string.Empty);
    }

    public static PageFetchResult Fail(Uri finalUri, int statusCode, string failureReason)
    {
        ArgumentNullException.ThrowIfNull(finalUri);

        return new PageFetchResult(false, finalUri, statusCode, string.Empty, failureReason);
    }
}

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

        if (!response.IsSuccessStatusCode)
        {
            return PageFetchResult.Fail(finalUri, statusCode, $"http_status_{statusCode}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return PageFetchResult.Fail(finalUri, statusCode, "missing_content_type");
        }

        if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return PageFetchResult.Fail(finalUri, statusCode, "non_html_content_type");
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        return PageFetchResult.Ok(finalUri, statusCode, html);
    }
}
