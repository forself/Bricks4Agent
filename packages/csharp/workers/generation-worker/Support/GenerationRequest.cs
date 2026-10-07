using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GenerationWorker.Support;

/// <summary>
/// 解析 broker 送來的 payload 與 grant scope。
/// payload 依 broker 的慣例為 <c>{ route, args, project_root }</c>；參數依 args → tool_args → 根層的順序取用，
/// 與 PolicyEngine 做 schema 驗證的位置一致。這裡只取出各能力認得的鍵，其他鍵（含任何路徑類欄位）一律不用。
/// </summary>
public static partial class GenerationRequest
{
    /// <summary>output_slot、package_name 與 requestId 的格式：英數、底線、連字號，1～80 字。</summary>
    public const string SafeNamePattern = "^[A-Za-z0-9_-]{1,80}$";

    /// <summary>generate 唯一支援的產物格式。</summary>
    public const string SupportedPackage = "definition-site-v1";

    /// <summary>
    /// max_pages 的允許範圍：與生成器驗證的頁數上限相同（tools/generation/validate-definition.mjs 的 LIMITS.maxPages），
    /// broker 的 Generation:MaxPages 也限制在這個範圍內。
    /// </summary>
    public const int MaxPagesLimit = 12;

    [GeneratedRegex(SafeNamePattern, RegexOptions.CultureInvariant)]
    private static partial Regex SafeNameRegex();

    // Windows 保留的裝置名稱不能當目錄或檔名（不分大小寫，含 COM1～9、LPT1～9）。
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDeviceNameRegex();

    /// <summary>名稱是否可安全地當成單一路徑段使用。</summary>
    public static bool IsSafeName(string? value)
        => value != null && SafeNameRegex().IsMatch(value) && !ReservedDeviceNameRegex().IsMatch(value);

    /// <summary>取出參數物件。payload 不是 JSON 物件時回傳 null。</summary>
    public static JsonObject? ExtractArgs(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject rootObject)
            return null;

        if (rootObject["args"] is JsonObject args)
            return args;
        if (rootObject["tool_args"] is JsonObject legacyArgs)
            return legacyArgs;
        return rootObject;
    }

    /// <summary>可選的字串陣列參數（例如 page_ids）。值存在但不是字串陣列時回傳 false。</summary>
    public static bool TryGetStringArray(JsonObject args, string name, int maxItemLength, out JsonArray? copy)
    {
        copy = null;
        if (!args.TryGetPropertyValue(name, out var node) || node == null)
            return true;

        if (node is not JsonArray array)
            return false;

        var result = new JsonArray();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length > maxItemLength)
                return false;
            result.Add(text);
        }

        copy = result;
        return true;
    }

    /// <summary>
    /// grant scope 中可選的 <c>max_pages</c>（validate 用）：scope 空白、沒有這個鍵或值為 null 時沒有上限；
    /// scope 不是 JSON 物件，或值不是 1～<see cref="MaxPagesLimit"/> 的整數時回傳 false（拒絕）。
    /// </summary>
    public static bool TryReadOptionalMaxPages(string? scopeJson, out int? maxPages)
    {
        maxPages = null;
        if (string.IsNullOrWhiteSpace(scopeJson))
            return true;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(scopeJson) as JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }

        if (root == null)
            return false;
        if (!root.TryGetPropertyValue("max_pages", out var node) || node == null)
            return true;

        if (node is not JsonValue value ||
            value.GetValueKind() != JsonValueKind.Number ||
            !value.TryGetValue<int>(out var limit) ||
            limit < 1 || limit > MaxPagesLimit)
        {
            return false;
        }

        maxPages = limit;
        return true;
    }

    /// <summary>
    /// 物件鍵長上限，與生成器驗證第 1 層的上限相同（tools/generation/validate-definition.mjs 的 LIMITS.maxKeyLength）。
    /// 定義的鍵都是短名稱；worker 在轉交 CLI 之前先擋下過長的鍵。
    /// </summary>
    public const int MaxTemplateKeyLength = 128;

    /// <summary>JSON 樹中是否有任何物件鍵長於 <paramref name="maxKeyLength"/>（迭代走訪，不遞迴）。</summary>
    public static bool HasOverlongKey(JsonNode? root, int maxKeyLength = MaxTemplateKeyLength)
    {
        if (root == null)
            return false;

        var stack = new Stack<JsonNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key.Length > maxKeyLength)
                            return true;
                        if (value != null)
                            stack.Push(value);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item != null)
                            stack.Push(item);
                    }
                    break;
            }
        }

        return false;
    }

    /// <summary>可選的字串參數。值存在但不是字串或過長時回傳 false。</summary>
    public static bool TryGetString(JsonObject args, string name, int maxLength, out string? value)
    {
        value = null;
        if (!args.TryGetPropertyValue(name, out var node) || node == null)
            return true;

        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text) || text.Length > maxLength)
            return false;

        value = text;
        return true;
    }
}

/// <summary>generate 的 grant scope（由 broker 寫入）。輸出位置只由這裡決定。</summary>
public sealed class GenerationScope
{
    private GenerationScope(string outputSlot, string packageName, int maxPages)
    {
        OutputSlot = outputSlot;
        PackageName = packageName;
        MaxPages = maxPages;
    }

    public string OutputSlot { get; }
    public string PackageName { get; }
    public int MaxPages { get; }

    /// <summary>
    /// 解析並嚴格驗證 scope：必須是 JSON 物件，且含格式正確的 <c>output_slot</c>、<c>package_name</c>、
    /// <c>max_pages</c>（1～<see cref="GenerationRequest.MaxPagesLimit"/> 的整數）與 <c>package: "definition-site-v1"</c>。任何一項缺少或不符都拒絕。
    /// </summary>
    public static bool TryParse(string? scopeJson, out GenerationScope? scope, out string error)
    {
        scope = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(scopeJson))
        {
            error = "Grant scope is empty; output_slot, package_name, max_pages and package are required.";
            return false;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(scopeJson) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root == null)
        {
            error = "Grant scope is not a JSON object.";
            return false;
        }

        if (!TryReadString(root, "output_slot", out var outputSlot) || !GenerationRequest.IsSafeName(outputSlot))
        {
            error = "Grant scope output_slot is missing or not a safe name.";
            return false;
        }

        if (!TryReadString(root, "package_name", out var packageName) || !GenerationRequest.IsSafeName(packageName))
        {
            error = "Grant scope package_name is missing or not a safe name.";
            return false;
        }

        if (root["max_pages"] is not JsonValue maxPagesValue ||
            maxPagesValue.GetValueKind() != JsonValueKind.Number ||
            !maxPagesValue.TryGetValue<int>(out var maxPages) ||
            maxPages < 1 || maxPages > GenerationRequest.MaxPagesLimit)
        {
            error = $"Grant scope max_pages is missing or not an integer between 1 and {GenerationRequest.MaxPagesLimit}.";
            return false;
        }

        if (!TryReadString(root, "package", out var package) ||
            !string.Equals(package, GenerationRequest.SupportedPackage, StringComparison.Ordinal))
        {
            error = $"Grant scope package must be '{GenerationRequest.SupportedPackage}'.";
            return false;
        }

        scope = new GenerationScope(outputSlot!, packageName!, maxPages);
        return true;
    }

    private static bool TryReadString(JsonObject root, string name, out string? value)
    {
        value = null;
        if (root[name] is not JsonValue node || node.GetValueKind() != JsonValueKind.String)
            return false;
        value = node.GetValue<string>();
        return true;
    }
}
