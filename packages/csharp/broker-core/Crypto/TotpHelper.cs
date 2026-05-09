using System.Security.Cryptography;
using System.Text;

namespace BrokerCore.Crypto;

/// <summary>
/// RFC 6238 TOTP（HMAC-SHA1, 30s step, 6 digits）—— Google Authenticator / Authy / 1Password 通用格式。
/// secret 是任意 byte[]、外部用 Base32 顯示給人輸入或塞 QR code（otpauth://...）。
/// 容錯 ±1 步（90s 視窗）給時鐘漂移、足夠大多數 client。
/// </summary>
public static class TotpHelper
{
    private const int CodeDigits = 6;
    private const int StepSeconds = 30;
    private const int SecretBytes = 20;       // 160-bit、跟 RFC 4226 範例一致

    public static byte[] GenerateSecret()
    {
        var bytes = new byte[SecretBytes];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public static string ToBase32(byte[] bytes)
    {
        // 標準 Base32（RFC 4648）— TOTP secret 是這個 encoding
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                sb.Append(alphabet[(buffer >> bitsLeft) & 0x1F]);
            }
        }
        if (bitsLeft > 0) sb.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string b32)
    {
        b32 = b32.Trim().ToUpperInvariant().Replace(" ", "").Replace("=", "");
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var ch in b32)
        {
            var idx = alphabet.IndexOf(ch);
            if (idx < 0) throw new FormatException($"Invalid base32 char: {ch}");
            buffer = (buffer << 5) | idx;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xff));
            }
        }
        return output.ToArray();
    }

    /// <summary>產 otpauth:// URL、給 Google Authenticator 掃 QR code 用。</summary>
    public static string BuildOtpAuthUrl(byte[] secret, string accountLabel, string issuer = "B4A Broker")
    {
        var b32 = ToBase32(secret);
        var label = Uri.EscapeDataString($"{issuer}:{accountLabel}");
        var iss = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={b32}&issuer={iss}&algorithm=SHA1&digits=6&period=30";
    }

    public static string GenerateCode(byte[] secret, DateTime utcNow)
    {
        var step = (long)(utcNow - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        return GenerateCodeForStep(secret, step);
    }

    private static string GenerateCodeForStep(byte[] secret, long step)
    {
        var stepBytes = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(stepBytes);

        // RFC 4226 dynamic truncation
        var offset = hash[hash.Length - 1] & 0x0f;
        var truncated = ((hash[offset] & 0x7f) << 24)
                      | ((hash[offset + 1] & 0xff) << 16)
                      | ((hash[offset + 2] & 0xff) << 8)
                      | (hash[offset + 3] & 0xff);
        var code = truncated % 1_000_000;
        return code.ToString("D6");
    }

    /// <summary>驗 code、容 ±1 步差（讓 client/server 各自 30s 漂移都過）。</summary>
    public static bool Verify(byte[] secret, string userCode, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(userCode) || userCode.Length != CodeDigits) return false;
        var step = (long)(utcNow - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        for (var window = -1; window <= 1; window++)
        {
            var expected = GenerateCodeForStep(secret, step + window);
            // constant-time compare
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(userCode)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 產 N 個 backup code（10 位字母+數字混合、避開易混淆字元）。原始字串只在 enrollment 時 user 看到一次、之後存 hash。
    /// </summary>
    public static string[] GenerateBackupCodes(int count = 8)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 去掉 I / O / 0 / 1 易混淆
        var codes = new string[count];
        for (var i = 0; i < count; i++)
        {
            var sb = new StringBuilder(10);
            for (var j = 0; j < 10; j++)
            {
                var b = new byte[1];
                RandomNumberGenerator.Fill(b);
                sb.Append(alphabet[b[0] % alphabet.Length]);
            }
            codes[i] = sb.ToString();
        }
        return codes;
    }
}
