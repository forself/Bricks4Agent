namespace BrokerCore.Services;

/// <summary>JSON Schema 驗證</summary>
public interface ISchemaValidator
{
    /// <summary>
    /// 驗證 JSON payload 是否符合 schema
    /// </summary>
    /// <param name="payload">待驗證的 JSON 字串</param>
    /// <param name="schema">JSON Schema 定義</param>
    /// <returns>(是否有效, 錯誤訊息)</returns>
    (bool IsValid, string? ErrorMessage) Validate(string payload, string schema);
}
