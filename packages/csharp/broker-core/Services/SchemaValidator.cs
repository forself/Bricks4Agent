using System.Text.Json;

namespace BrokerCore.Services;

/// <summary>
/// JSON Schema 驗證器（Phase 1 簡易實作）
///
/// Phase 1：基礎型別/必填欄位驗證
/// Phase 2：完整 JSON Schema Draft 7 支援（可引入 NJsonSchema）
///
/// 設計考量：
/// - 零外部依賴（不引入 NJsonSchema / Newtonsoft）
/// - 足以處理 Phase 1 的 file.read、file.list 等簡單 schema
/// - 當 schema = "{}" 時直接通過（無限制）
/// </summary>
public class SchemaValidator : ISchemaValidator
{
    /// <inheritdoc />
    public (bool IsValid, string? ErrorMessage) Validate(string payload, string schema)
    {
        // 空 schema 或 {} → 不驗證
        if (string.IsNullOrWhiteSpace(schema) || schema.Trim() is "{}" or "")
            return (true, null);

        // 驗證 payload 是合法 JSON
        JsonDocument payloadDoc;
        try
        {
            payloadDoc = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return (false, $"Invalid JSON payload: {ex.Message}");
        }

        // 解析 schema
        JsonDocument schemaDoc;
        try
        {
            schemaDoc = JsonDocument.Parse(schema);
        }
        catch (JsonException ex)
        {
            return (false, $"Invalid JSON Schema: {ex.Message}");
        }

        using (payloadDoc)
        using (schemaDoc)
        {
            return ValidateElement(payloadDoc.RootElement, schemaDoc.RootElement, "$");
        }
    }

    private static (bool IsValid, string? ErrorMessage) ValidateElement(
        JsonElement value, JsonElement schema, string path)
    {
        // type 檢查
        if (schema.TryGetProperty("type", out var typeProp))
        {
            var expectedType = typeProp.GetString();
            if (!MatchesType(value, expectedType))
                return (false, $"Type mismatch at {path}: expected {expectedType}, got {value.ValueKind}");
        }

        // required 檢查（僅 object）
        if (schema.TryGetProperty("required", out var requiredProp) && value.ValueKind == JsonValueKind.Object)
        {
            foreach (var req in requiredProp.EnumerateArray())
            {
                var fieldName = req.GetString();
                if (fieldName != null && !value.TryGetProperty(fieldName, out _))
                    return (false, $"Missing required field at {path}: {fieldName}");
            }
        }

        // properties 遞迴驗證
        if (schema.TryGetProperty("properties", out var propsDef) && value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propSchema in propsDef.EnumerateObject())
            {
                if (value.TryGetProperty(propSchema.Name, out var propValue))
                {
                    var result = ValidateElement(propValue, propSchema.Value, $"{path}.{propSchema.Name}");
                    if (!result.IsValid)
                        return result;
                }
            }
        }

        // items 驗證（array）
        if (schema.TryGetProperty("items", out var itemsSchema) && value.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var item in value.EnumerateArray())
            {
                var result = ValidateElement(item, itemsSchema, $"{path}[{idx}]");
                if (!result.IsValid)
                    return result;
                idx++;
            }
        }

        // maxLength 檢查（string）
        if (schema.TryGetProperty("maxLength", out var maxLen) && value.ValueKind == JsonValueKind.String)
        {
            var str = value.GetString() ?? "";
            if (str.Length > maxLen.GetInt32())
                return (false, $"String too long at {path}: max {maxLen.GetInt32()}, got {str.Length}");
        }

        // enum 檢查
        if (schema.TryGetProperty("enum", out var enumProp))
        {
            var valueStr = value.ToString();
            bool found = false;
            foreach (var e in enumProp.EnumerateArray())
            {
                if (e.ToString() == valueStr)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                return (false, $"Value at {path} not in allowed enum values");
        }

        return (true, null);
    }

    private static bool MatchesType(JsonElement value, string? expectedType)
    {
        return expectedType switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind is JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true // 未知型別 → 不限制
        };
    }
}
