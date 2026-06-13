using System.Security.Cryptography;
using System.Text;

namespace Broker.Services;

/// <summary>
/// 使用者審批連結 token(§18.2-C2)—— 短時效、HMAC 簽章,綁單一 userId。
/// 經 LINE 把連結送給使用者;web 端用此驗 token → 解出 userId,只授權其自己的 User 層待審。
/// 沿用 artifact 下載的簽章 secret(BrokerArtifactDownloadOptions.SigningSecret)。
/// </summary>
public sealed class ApprovalLinkService
{
    private readonly string _secret;
    private readonly int _ttlMinutes;

    public ApprovalLinkService(BrokerArtifactDownloadOptions options, int ttlMinutes = 15)
    {
        _secret = options.SigningSecret;
        _ttlMinutes = ttlMinutes <= 0 ? 15 : ttlMinutes;
    }

    /// <summary>產生 token `b64url(userId).exp.sig`;secret 未設或 userId 空回 null。</summary>
    public string? CreateToken(string userId, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(_secret) || string.IsNullOrWhiteSpace(userId))
            return null;
        var exp = (now ?? DateTimeOffset.UtcNow).AddMinutes(_ttlMinutes).ToUnixTimeSeconds();
        return $"{Base64Url(Encoding.UTF8.GetBytes(userId))}.{exp}.{Sign(userId, exp)}";
    }

    /// <summary>驗 token → 回 userId;無效/過期/被竄改回 null。</summary>
    public string? Validate(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(_secret) || string.IsNullOrWhiteSpace(token))
            return null;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        string userId;
        try { userId = Encoding.UTF8.GetString(FromBase64Url(parts[0])); }
        catch { return null; }

        if (!long.TryParse(parts[1], out var exp))
            return null;
        if (now.ToUnixTimeSeconds() > exp)
            return null;

        var expected = Encoding.UTF8.GetBytes(Sign(userId, exp));
        var actual = Encoding.UTF8.GetBytes(parts[2]);
        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
            return null;

        return userId;
    }

    private string Sign(string userId, long exp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var payload = $"approval-link\n{userId}\n{exp}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
