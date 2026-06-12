using System.Security.Cryptography;
using System.Text;

namespace Broker.Services;

public sealed class BrokerArtifactDownloadService
{
    private readonly HighLevelLineWorkspaceService _workspace;
    private readonly SidecarPublicUrlResolver _publicUrlResolver;
    private readonly BrokerArtifactDownloadOptions _options;

    public BrokerArtifactDownloadService(
        HighLevelLineWorkspaceService workspace,
        SidecarPublicUrlResolver publicUrlResolver,
        BrokerArtifactDownloadOptions options)
    {
        _workspace = workspace;
        _publicUrlResolver = publicUrlResolver;
        _options = options;
    }

    public string? CreateSignedDownloadUrl(string artifactId, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningSecret))
            return null;

        var artifact = _workspace.ReadArtifactById(artifactId);
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.FilePath) || !File.Exists(artifact.FilePath))
            return null;

        var publicBaseUrl = _publicUrlResolver.TryGetPublicBaseUrl();
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
            return null;

        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var minutes = _options.LinkTtlMinutes <= 0 ? 60 : _options.LinkTtlMinutes;
        var exp = issuedAt.AddMinutes(minutes).ToUnixTimeSeconds();
        var sig = ComputeSignature(artifact.ArtifactId, artifact.FileName, exp);
        return $"{publicBaseUrl}/api/v1/artifacts/download/{Uri.EscapeDataString(artifact.ArtifactId)}?exp={exp}&sig={Uri.EscapeDataString(sig)}";
    }

    public BrokerArtifactDownloadResolution ValidateAndResolve(
        string artifactId,
        long exp,
        string sig,
        DateTimeOffset now)
    {
        if (now.ToUnixTimeSeconds() > exp)
            return BrokerArtifactDownloadResolution.Expired();

        var artifact = _workspace.ReadArtifactById(artifactId);
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.FilePath) || !File.Exists(artifact.FilePath))
            return BrokerArtifactDownloadResolution.NotFound();

        if (string.IsNullOrWhiteSpace(_options.SigningSecret))
            return BrokerArtifactDownloadResolution.Invalid();

        var expected = ComputeSignature(artifact.ArtifactId, artifact.FileName, exp);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(sig ?? string.Empty);
        if (expectedBytes.Length != actualBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return BrokerArtifactDownloadResolution.Invalid();
        }

        return BrokerArtifactDownloadResolution.Success(
            artifact,
            artifact.FilePath,
            SanitizeFileName(artifact.FileName));
    }

    private string ComputeSignature(string artifactId, string fileName, long exp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningSecret));
        var payload = $"{artifactId}\n{fileName}\n{exp}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = string.IsNullOrWhiteSpace(fileName) ? "artifact.bin" : fileName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return safe;
    }
}

public sealed class BrokerArtifactDownloadResolution
{
    public bool IsValid { get; init; }
    public bool IsExpired { get; init; }
    public bool IsMissing { get; init; }
    public HighLevelLineArtifactRecord? Artifact { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string SafeFileName { get; init; } = string.Empty;

    public static BrokerArtifactDownloadResolution Invalid() => new();
    public static BrokerArtifactDownloadResolution Expired() => new() { IsExpired = true };
    public static BrokerArtifactDownloadResolution NotFound() => new() { IsMissing = true };
    public static BrokerArtifactDownloadResolution Success(
        HighLevelLineArtifactRecord artifact,
        string filePath,
        string safeFileName)
        => new() { IsValid = true, Artifact = artifact, FilePath = filePath, SafeFileName = safeFileName };
}
