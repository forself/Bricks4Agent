using System.Text.Json;

namespace Broker.Helpers;

/// <summary>
/// ApprovedRequest payload 解析共用工具
/// </summary>
public static class PayloadHelper
{
    public static JsonElement GetArgsElement(JsonElement root)
    {
        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            return args;
        if (root.TryGetProperty("tool_args", out var legacyArgs) && legacyArgs.ValueKind == JsonValueKind.Object)
            return legacyArgs;
        return root;
    }

    public static bool IsPayloadRouteValid(JsonElement root, string approvedRoute)
    {
        var payloadRoute = TryGetString(root, "route", "tool_name");
        return string.IsNullOrWhiteSpace(payloadRoute) ||
               payloadRoute.Equals(approvedRoute, StringComparison.OrdinalIgnoreCase);
    }

    public static string? TryGetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    public static int? TryGetInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedNumber))
                return parsedNumber;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsedString))
                return parsedString;
        }
        return null;
    }

    /// <summary>解析路徑，確保在沙箱範圍內</summary>
    public static string? ResolveSandboxedPath(string sandboxRoot, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(sandboxRoot, path));
            if (!fullPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase))
                return null;
            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
