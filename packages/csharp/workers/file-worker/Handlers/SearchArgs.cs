using System.Text.Json;

namespace FileWorker.Handlers;

/// <summary>搜尋處理器的參數讀取：依序取第一個字串型別的屬性（與 broker 的 InProcess 實作相同）。</summary>
internal static class SearchArgs
{
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
