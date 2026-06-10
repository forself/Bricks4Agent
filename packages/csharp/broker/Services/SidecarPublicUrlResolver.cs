using System.Text;

namespace Broker.Services;

public sealed class SidecarPublicUrlResolver
{
    private readonly BrokerArtifactDownloadOptions _options;

    public SidecarPublicUrlResolver(BrokerArtifactDownloadOptions options)
    {
        _options = options;
    }

    public string? TryGetPublicBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.SidecarLastTunnelUrlPath))
            return null;

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.SidecarLastTunnelUrlPath));
        if (!File.Exists(path))
            return null;

        var raw = File.ReadAllText(path, new UTF8Encoding(false)).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return null;

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }
}
