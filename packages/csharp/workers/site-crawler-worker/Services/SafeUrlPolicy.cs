using System.Net;
using System.Net.Sockets;

namespace SiteCrawlerWorker.Services;

public sealed record SafeUrlValidationResult(bool IsAllowed, Uri? Uri, string? Reason)
{
    public static SafeUrlValidationResult Allow(Uri uri) => new(true, uri, null);

    public static SafeUrlValidationResult Deny(string reason) => new(false, null, reason);
}

public static class SafeUrlPolicy
{
    public static SafeUrlValidationResult Validate(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return SafeUrlValidationResult.Deny("url_required");
        }

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return SafeUrlValidationResult.Deny("invalid_url");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return SafeUrlValidationResult.Deny("unsupported_scheme");
        }

        var normalizedUri = Normalize(uri);
        if (IsBlockedHost(normalizedUri.Host))
        {
            return SafeUrlValidationResult.Deny("blocked_host");
        }

        return SafeUrlValidationResult.Allow(normalizedUri);
    }

    private static Uri Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };

        if (string.IsNullOrEmpty(builder.Path))
        {
            builder.Path = "/";
        }

        return builder.Uri;
    }

    private static bool IsBlockedHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsBlockedIPv4(address);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal ||
                address.IsIPv6Multicast;
        }

        return false;
    }

    private static bool IsBlockedIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 0 ||
            bytes[0] == 10 ||
            bytes[0] == 127 ||
            (bytes[0] == 169 && bytes[1] == 254) ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }
}
