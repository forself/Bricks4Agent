namespace BaseLogger;

/// <summary>
/// BaseLogger 治理 redaction(設計 spec §9)。
///
/// 必備元件、非 optional helper:在事件進入任何 sink 之前,把 token / secret / PII
/// 遮罩或穩定雜湊。sink 不應自行決定是否遮罩 —— redaction 一律在 sink 之前完成。
/// </summary>
public interface IBaseLoggerRedactor
{
    /// <summary>屬性名稱是否屬敏感(含 token/secret/key/password/authorization/cookie/signature/credential)。</summary>
    bool IsSensitiveName(string name);

    /// <summary>依「名稱」決定值的處理:敏感名→遮罩、識別子→穩定雜湊、過長→截斷、其餘原樣。</summary>
    string RedactValue(string name, string? value);

    /// <summary>對整組結構化屬性逐一 redact,回傳已遮罩的字串字典。</summary>
    IReadOnlyDictionary<string, string> RedactProperties(IReadOnlyDictionary<string, object?> properties);

    /// <summary>移除自由文字(訊息 / 例外字串)中明顯的行內 secret,如 Bearer xxx、token=xxx。</summary>
    string RedactText(string? text);

    /// <summary>對 exception 字串做 RedactText。</summary>
    string RedactException(string? exception);

    /// <summary>principal id:system principal 保留明文,其餘以穩定雜湊表示。</summary>
    string RedactPrincipalId(string? principalId);

    /// <summary>對識別子(user/principal/session/IP)做穩定、不可逆的短雜湊(帶 h: 前綴)。</summary>
    string HashIdentifier(string value);
}
