using System.Net;
using System.Net.Sockets;

namespace SiteCrawlerWorker.Services;

public sealed record SafeUrlValidationResult(bool IsAllowed, Uri? Uri, string Reason)
{
    public static SafeUrlValidationResult Allow(Uri uri) => new(true, uri, string.Empty);

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
        var canonicalHost = CanonicalizeHostForSafety(host);
        if (string.Equals(canonicalHost, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(canonicalHost, out var address))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsBlockedIPv4(address);
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedIPv4(address.MapToIPv4());
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal ||
                address.IsIPv6Multicast ||
                (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }

    private static string CanonicalizeHostForSafety(string host) =>
        host.Trim().TrimEnd('.').Trim('[', ']');

    private static bool IsBlockedIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 0 ||
            bytes[0] == 10 ||
            bytes[0] == 127 ||
            (bytes[0] == 169 && bytes[1] == 254) ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168) ||
            (bytes[0] >= 224 && bytes[0] <= 239);
    }
}
