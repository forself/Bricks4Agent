using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// Broker 自有的簽章 artifact 下載服務(2026-05-29, AnthonyLee)。
/// 執行 Benson 2026-03-29「broker artifact download API」plan 的核心新檔:
///   - 簽 HMAC-SHA256 expiring URL / 驗章 / 驗期 / 防路徑穿透 / 解析 artifact 實體檔。
/// Reuse(只呼叫、不改):[[HighLevelLineWorkspaceService]].ReadArtifactById + HighLevelLineArtifactRecord。
///
/// 安全:① HMAC 簽章(不可猜)② 固定時間比較(防 timing)③ 過期檢查 ④ 路徑穿透防護(檔須落在
/// 記錄宣告的 DocumentsRoot 下)⑤ 失敗不洩漏路徑/原因。
/// </summary>
public sealed class BrokerArtifactDownloadService
{
    private readonly BrokerArtifactDownloadOptions _opts;
    private readonly HighLevelLineWorkspaceService _workspace;
    private readonly ILogger<BrokerArtifactDownloadService> _logger;
    private readonly byte[] _secret;

    public BrokerArtifactDownloadService(
        BrokerArtifactDownloadOptions opts,
        HighLevelLineWorkspaceService workspace,
        ILogger<BrokerArtifactDownloadService> logger)
    {
        _opts = opts;
        _workspace = workspace;
        _logger = logger;

        var secret = opts.SigningSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _logger.LogWarning("ArtifactDownload: 未設簽章密鑰、已生成臨時密鑰(重啟後舊連結失效)。正式環境請設 ARTIFACT_DOWNLOAD_SIGNING_SECRET。");
        }
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>產生簽章下載路徑(或完整 URL 若有設 PublicBaseUrl)。ttlSeconds&lt;=0 用預設。</summary>
    public string CreateSignedUrl(string artifactId, int ttlSeconds = 0)
    {
        long exp = DateTimeOffset.UtcNow
            .AddSeconds(ttlSeconds > 0 ? ttlSeconds : _opts.TtlSeconds)
            .ToUnixTimeSeconds();
        var sig = Sign(artifactId, exp);
        var path = $"/api/v1/artifacts/dl?id={Uri.EscapeDataString(artifactId)}&exp={exp}&sig={sig}";
        return string.IsNullOrWhiteSpace(_opts.PublicBaseUrl)
            ? path
            : _opts.PublicBaseUrl.TrimEnd('/') + path;
    }

    /// <summary>驗簽 + 驗期。任何不符回 false(刻意不分辨是過期還是壞章、不洩漏)。</summary>
    public bool ValidateSignature(string artifactId, long exp, string? sig)
        => ValidateSignatureCore(_secret, artifactId, exp, sig, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>解析 artifact 實體檔(含路徑穿透防護)。回 (Ok, FullPath, FileName, ContentType, Error)。</summary>
    public (bool Ok, string FullPath, string FileName, string ContentType, string? Error) ResolveFile(string artifactId)
    {
        var rec = _workspace.ReadArtifactById(artifactId);
        if (rec == null) return (false, "", "", "", "not_found");
        if (string.IsNullOrWhiteSpace(rec.FilePath)) return (false, "", "", "", "no_file");

        string full;
        try { full = Path.GetFullPath(rec.FilePath); }
        catch { return (false, "", "", "", "bad_path"); }

        // 路徑穿透防護:檔案必須落在 artifact 記錄宣告的 DocumentsRoot 之下
        if (!string.IsNullOrWhiteSpace(rec.DocumentsRoot) && !IsPathUnderRoot(full, Path.GetFullPath(rec.DocumentsRoot)))
        {
            _logger.LogWarning("ArtifactDownload: artifact {Id} FilePath 落在 DocumentsRoot 之外、拒絕。", artifactId);
            return (false, "", "", "", "path_escape");
        }
        if (!File.Exists(full)) return (false, "", "", "", "missing");

        var name = string.IsNullOrWhiteSpace(rec.FileName) ? Path.GetFileName(full) : rec.FileName;
        return (true, full, SanitizeFileName(name), ContentTypeFor(full), null);
    }

    private string Sign(string artifactId, long exp) => ComputeSignature(_secret, artifactId, exp);

    // ── 可單元測試的純安全邏輯(internal static、無 DI 依賴)─────────────────────
    /// <summary>HMAC-SHA256("{id}|{exp}") → base64url。確定性。</summary>
    internal static string ComputeSignature(byte[] secret, string artifactId, long exp)
    {
        using var h = new HMACSHA256(secret);
        return Base64Url(h.ComputeHash(Encoding.UTF8.GetBytes($"{artifactId}|{exp}")));
    }

    /// <summary>驗簽核心:空 id/sig、過期(now&gt;exp)、簽章不符 → false。固定時間比較防 timing。</summary>
    internal static bool ValidateSignatureCore(byte[] secret, string artifactId, long exp, string? sig, long nowUnix)
    {
        if (string.IsNullOrWhiteSpace(artifactId) || string.IsNullOrWhiteSpace(sig)) return false;
        if (nowUnix > exp) return false;
        var expected = Encoding.UTF8.GetBytes(ComputeSignature(secret, artifactId, exp));
        var actual = Encoding.UTF8.GetBytes(sig);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>路徑穿透防護:fullPath 必須在 root 之下(用 separator 結尾比較、防 /root vs /rootEvil 前綴繞過)。</summary>
    internal static bool IsPathUnderRoot(string fullPath, string root)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string SanitizeFileName(string n)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return string.IsNullOrWhiteSpace(n) ? "artifact" : n;
    }

    private static string ContentTypeFor(string path)
    {
        var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".md" or ".txt" => "text/plain; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".csv" => "text/csv; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
    }
}
