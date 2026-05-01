using System.Net;
using System.Net.Sockets;

namespace SiteCrawlerWorker.Services;

public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct);
}

public interface IHostAddressResolver
{
    Task<IReadOnlyList<IPAddress>> GetHostAddressesAsync(string host, CancellationToken ct);
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
    private readonly IHostAddressResolver hostAddressResolver;

    public HttpPageFetcher()
        : this(CreateNonRedirectingHttpClient(), SystemHostAddressResolver.Instance)
    {
    }

    public HttpPageFetcher(HttpMessageHandler nonRedirectingHandler, IHostAddressResolver hostAddressResolver)
        : this(new HttpClient(nonRedirectingHandler, disposeHandler: false), hostAddressResolver)
    {
    }

    private HttpPageFetcher(HttpClient httpClient, IHostAddressResolver hostAddressResolver)
    {
        this.httpClient = httpClient;
        this.hostAddressResolver = hostAddressResolver;
    }

    public async Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var resolvedAddressFailure = await ValidateResolvedAddressesAsync(uri, ct);
        if (resolvedAddressFailure is not null)
        {
            return PageFetchResult.Fail(uri, 0, resolvedAddressFailure);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var statusCode = (int)response.StatusCode;
        var contentType = response.Content?.Headers.ContentType?.ToString() ?? string.Empty;
        var mediaType = response.Content?.Headers.ContentType?.MediaType ?? string.Empty;

        if (IsRedirect(statusCode) && response.Headers.Location is not null)
        {
            return PageFetchResult.Redirect(uri, finalUri, statusCode, ResolveRedirectUri(finalUri, response.Headers.Location));
        }

        if (!response.IsSuccessStatusCode)
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, contentType, $"http_status_{statusCode}");
        }

        if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, contentType, "non_html_content_type");
        }

        var html = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);
        return new PageFetchResult(
            true,
            uri,
            finalUri,
            statusCode,
            contentType,
            html,
            string.Empty);
    }

    private async Task<string?> ValidateResolvedAddressesAsync(Uri uri, CancellationToken ct)
    {
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await hostAddressResolver.GetHostAddressesAsync(uri.IdnHost, ct);
        }
        catch (SocketException)
        {
            return "dns_resolution_failed";
        }

        return addresses.Any(SafeUrlPolicy.IsBlockedIpAddress) ? "blocked_resolved_ip" : null;
    }

    private static bool IsRedirect(int statusCode)
    {
        return statusCode >= 300 && statusCode <= 399;
    }

    private static Uri ResolveRedirectUri(Uri baseUri, Uri location)
    {
        return location.IsAbsoluteUri ? location : new Uri(baseUri, location);
    }

    private static HttpClient CreateNonRedirectingHttpClient()
    {
        return new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        });
    }

    private sealed class SystemHostAddressResolver : IHostAddressResolver
    {
        public static readonly SystemHostAddressResolver Instance = new();

        public async Task<IReadOnlyList<IPAddress>> GetHostAddressesAsync(string host, CancellationToken ct)
        {
            return await Dns.GetHostAddressesAsync(host, ct);
        }
    }
}
