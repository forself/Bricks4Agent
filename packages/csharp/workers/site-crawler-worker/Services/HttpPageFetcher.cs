using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SiteCrawlerWorker.Services;

public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct);

    Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        return FetchAsync(uri, ct);
    }
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
        return await FetchAsync(uri, long.MaxValue, ct);
    }

    public async Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var resolvedAddressFailure = await ValidateResolvedAddressesAsync(uri, ct);
        if (resolvedAddressFailure is not null)
        {
            return PageFetchResult.Fail(uri, 0, resolvedAddressFailure);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpResponseMessage responseMessage;
        try
        {
            responseMessage = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (InvalidOperationException exception) when (IsFetchSafetyFailure(exception.Message))
        {
            return PageFetchResult.Fail(uri, 0, exception.Message);
        }
        catch (HttpRequestException exception)
            when (exception.InnerException is InvalidOperationException inner &&
                IsFetchSafetyFailure(inner.Message))
        {
            return PageFetchResult.Fail(uri, 0, inner.Message);
        }
        catch (HttpRequestException exception)
        {
            return PageFetchResult.Fail(uri, 0, BuildTransportFailureReason(exception));
        }
        catch (IOException exception)
        {
            return PageFetchResult.Fail(uri, 0, BuildTransportFailureReason(exception));
        }

        using var response = responseMessage;
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

        string? html;
        try
        {
            html = await ReadHtmlWithinLimitAsync(response.Content, maxBytes, ct);
        }
        catch (HttpRequestException exception)
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, contentType, BuildTransportFailureReason(exception));
        }
        catch (IOException exception)
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, contentType, BuildTransportFailureReason(exception));
        }

        if (html is null)
        {
            return PageFetchResult.Fail(uri, finalUri, statusCode, contentType, "response_too_large");
        }

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
        try
        {
            await ResolveAllowedEndpointAsync(
                new DnsEndPoint(uri.IdnHost, uri.Port),
                hostAddressResolver,
                ct);
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        return null;
    }

    public static async Task<IPEndPoint> ResolveAllowedEndpointAsync(
        DnsEndPoint dnsEndPoint,
        IHostAddressResolver resolver,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dnsEndPoint);
        ArgumentNullException.ThrowIfNull(resolver);

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.GetHostAddressesAsync(dnsEndPoint.Host, ct);
        }
        catch (SocketException exception)
        {
            throw new InvalidOperationException("dns_resolution_failed", exception);
        }

        if (addresses.Count == 0)
        {
            throw new InvalidOperationException("dns_resolution_failed");
        }

        if (addresses.Any(SafeUrlPolicy.IsBlockedIpAddress))
        {
            throw new InvalidOperationException("blocked_resolved_ip");
        }

        return new IPEndPoint(addresses[0], dnsEndPoint.Port);
    }

    private static bool IsRedirect(int statusCode)
    {
        return statusCode >= 300 && statusCode <= 399;
    }

    private static Uri ResolveRedirectUri(Uri baseUri, Uri location)
    {
        return location.IsAbsoluteUri ? location : new Uri(baseUri, location);
    }

    private static bool IsFetchSafetyFailure(string reason)
    {
        return reason is "blocked_resolved_ip" or "dns_resolution_failed";
    }

    private static string BuildTransportFailureReason(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => "fetch_http_request_failed",
            IOException => "fetch_io_failed",
            _ => "fetch_failed",
        };
    }

    private static HttpClient CreateNonRedirectingHttpClient()
    {
        return new HttpClient(CreateSafeSocketsHandler(SystemHostAddressResolver.Instance));
    }

    private static SocketsHttpHandler CreateSafeSocketsHandler(IHostAddressResolver resolver)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectCallback = async (context, ct) =>
            {
                var endpoint = await ResolveAllowedEndpointAsync(context.DnsEndPoint, resolver, ct);
                var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(endpoint, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }

    private static async Task<string?> ReadHtmlWithinLimitAsync(
        HttpContent? content,
        long maxBytes,
        CancellationToken ct)
    {
        if (content is null)
        {
            return string.Empty;
        }

        if (maxBytes < 0)
        {
            return null;
        }

        if (content.Headers.ContentLength is long contentLength && contentLength > maxBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var readBuffer = new byte[8192];
        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, ct);
            if (bytesRead == 0)
            {
                break;
            }

            if (totalBytes > maxBytes - bytesRead)
            {
                return null;
            }

            buffer.Write(readBuffer, 0, bytesRead);
            totalBytes += bytesRead;
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
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
