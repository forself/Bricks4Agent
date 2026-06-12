namespace Broker.Services;

public sealed class BrokerArtifactDownloadOptions
{
    public string SigningSecret { get; set; } = string.Empty;
    public int LinkTtlMinutes { get; set; } = 60;
    public bool AllowRepeatedDownloads { get; set; } = true;
    public string SidecarLastTunnelUrlPath { get; set; } = @".\packages\csharp\workers\line-worker\.last-tunnel-url";
}
