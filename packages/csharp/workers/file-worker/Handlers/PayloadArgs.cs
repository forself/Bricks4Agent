using System.Text.Json;

namespace FileWorker.Handlers;

/// <summary>
/// handler 的參數讀取。參數所在的物件與 broker 的 PolicyEngine 採用同一個順序：
/// <c>args</c>（物件）→ <c>tool_args</c>（物件）→ payload 根層。
/// 路徑一律從這個物件取得，所以 handler 實際使用的路徑，就是 PolicyEngine 檢查過的路徑。
/// </summary>
public static class PayloadArgs
{
    /// <summary>依 args → tool_args → 根層的順序，回傳第一個是物件的參數位置。</summary>
    public static JsonElement GetArgsElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return root;

        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            return args;

        if (root.TryGetProperty("tool_args", out var legacyArgs) && legacyArgs.ValueKind == JsonValueKind.Object)
            return legacyArgs;

        return root;
    }

    /// <summary>依序取第一個字串型別的屬性（與 broker 的 InProcess 實作相同）；都沒有時回傳 null。</summary>
    public static string? GetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }
}
