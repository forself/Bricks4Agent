using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BaseLogger;

/// <summary>
/// 預設 redaction 策略(設計 spec §9)。純函數、無外部依賴、可確定性測試。
///
/// 規則:
///   1. 屬性名含 token/secret/key/password/authorization/cookie/signature/credential → 一律遮罩。
///   2. user_id / principal_id / session_id / IP → 穩定雜湊(system principal 例外、保留明文)。
///   3. 過長值(LINE 訊息 / assistant 回覆 / upstream body)→ 截斷。
///   4. 自由文字 / exception 的行內 secret(Bearer xxx、token=xxx、password=xxx 等)→ 遮罩值。
/// </summary>
public sealed class DefaultBaseLoggerRedactor : IBaseLoggerRedactor
{
    public const string Mask = "***REDACTED***";

    /// <summary>非敏感長值的截斷上限;超過標 restricted 並截斷,避免把整段 body 寫進 log。</summary>
    public const int MaxValueLength = 512;

    // §9.1 屬性名含這些詞(不分大小寫)→ 一律遮罩
    private static readonly string[] SensitiveNameTokens =
    {
        "token", "secret", "key", "password", "authorization", "cookie", "signature", "credential",
    };

    // §9.2 預設以穩定雜湊表示的識別子名稱
    private static readonly HashSet<string> HashedIdentifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "user_id", "userid", "principal_id", "principalid",
        "session_id", "sessionid", "ip", "ip_address", "client_ip", "remote_ip",
    };

    // 保留明文的 system principal(非真人、無 PII 顧慮)
    private static readonly HashSet<string> SystemPrincipals = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "system_rag_ingest", "broker", "scheduler", "anonymous",
    };

    // §9.4 行內 secret:Bearer <tok> 或 (authorization|token|secret|password|api_key|cookie|signature|credential)<:|=><值>
    // 標頭式允許吃掉中間的 "Bearer " 再遮後面的真值。
    private static readonly Regex InlineSecret = new(
        @"(?i)((?:authorization|token|secret|password|api[_-]?key|cookie|signature|credential)\s*[:=]\s*(?:bearer\s+)?|\bbearer\s+)([^\s,;""']+)",
        RegexOptions.Compiled);

    public bool IsSensitiveName(string name)
        => !string.IsNullOrEmpty(name)
           && SensitiveNameTokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));

    public string RedactValue(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        if (IsSensitiveName(name)) return Mask;
        if (HashedIdentifierNames.Contains(name)) return HashIdentifier(value);
        return Truncate(value);
    }

    public IReadOnlyDictionary<string, string> RedactProperties(IReadOnlyDictionary<string, object?> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (properties == null) return result;
        foreach (var kv in properties)
            result[kv.Key] = RedactValue(kv.Key, kv.Value?.ToString());
        return result;
    }

    public string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return InlineSecret.Replace(text, m => m.Groups[1].Value + Mask);
    }

    public string RedactException(string? exception) => RedactText(exception);

    public string RedactPrincipalId(string? principalId)
    {
        if (string.IsNullOrEmpty(principalId)) return "";
        return SystemPrincipals.Contains(principalId) ? principalId : HashIdentifier(principalId);
    }

    public string HashIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder("h:", 18);
        for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();  // e.g. h:1a2b3c4d5e6f7081 — 穩定、不可逆、無 PII
    }

    private static string Truncate(string value)
        => value.Length <= MaxValueLength ? value : value[..MaxValueLength] + "…[truncated]";
}
