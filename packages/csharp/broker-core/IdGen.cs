using System.Security.Cryptography;

namespace BrokerCore;

/// <summary>
/// 前綴式 ULID-like ID 生成器（零外部依賴）
///
/// 格式：{prefix}_{timestamp_hex}_{random_hex}
/// 範例：ses_0194A3B2C1D4_7F3A2E1B
///
/// 特性：
/// - 時間排序（毫秒精度）
/// - 全域唯一（48-bit timestamp + 64-bit random）
/// - URL 安全
/// - 可讀（前綴標示類型）
/// </summary>
public static class IdGen
{
    /// <summary>生成帶前綴的唯一 ID</summary>
    /// <param name="prefix">ID 前綴（例如 "ses"、"task"、"req"）</param>
    public static string New(string prefix)
    {
        // 48-bit timestamp（毫秒，Unix epoch）
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tsHex = timestamp.ToString("X12"); // 12 hex chars = 48 bits

        // 64-bit cryptographic random
        var randomBytes = RandomNumberGenerator.GetBytes(8);
        var randHex = Convert.ToHexString(randomBytes); // 16 hex chars

        return $"{prefix}_{tsHex}{randHex}";
    }
}
