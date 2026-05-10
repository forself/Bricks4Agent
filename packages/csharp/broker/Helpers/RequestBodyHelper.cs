using System.Text.Json;
using Broker.Middleware;

namespace Broker.Helpers;

/// <summary>
/// 統一 request body 解析工具 —— 修復 JsonDocument 記憶體洩漏
///
/// 使用 HttpContext.Response.RegisterForDispose() 確保 JsonDocument
/// 在請求結束時正確釋放 ArrayPool 租借的記憶體。
/// 同一請求內多次呼叫只會解析一次（快取在 HttpContext.Items）。
/// </summary>
public static class RequestBodyHelper
{
    private const string ParsedBodyDocKey = "_parsed_body_doc";

    /// <summary>
    /// 從 HttpContext 取得解密後的 JSON body（含自動 Dispose 註冊）
    /// </summary>
    public static JsonElement GetBody(HttpContext ctx)
    {
        // 同一請求內只解析一次
        if (ctx.Items.TryGetValue(ParsedBodyDocKey, out var existing) && existing is JsonDocument cachedDoc)
            return cachedDoc.RootElement;

        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        var doc = JsonDocument.Parse(json);

        ctx.Items[ParsedBodyDocKey] = doc;
        ctx.Response.RegisterForDispose(doc); // 請求結束時自動 Dispose

        return doc.RootElement;
    }

    /// <summary>
    /// 取得已驗證的 role_id（"role_admin" / "role_user"）。
    /// 支援兩套 auth 來源：
    ///   1. BrokerAuthMiddleware（POST + scoped_token、值已是 "role_admin"/"role_user"）
    ///   2. CurrentUserMiddleware（cookie session、值是 "admin"/"user"、需要前綴 "role_"）
    /// 之前只看第一套、cookie 進來的 dashboard admin 永遠當成沒角色、admin 端點全 403。
    /// </summary>
    public static string GetRoleId(HttpContext ctx)
    {
        var brokerRole = ctx.Items[BrokerAuthMiddleware.RoleIdKey] as string;
        if (!string.IsNullOrEmpty(brokerRole)) return brokerRole;

        // cookie 路徑：current_role = "admin"/"user" → 加前綴與 token path 對齊
        var cookieRole = ctx.Items[CurrentUserMiddleware.RoleKey] as string;
        if (string.IsNullOrEmpty(cookieRole)) return "";
        return cookieRole.StartsWith("role_", StringComparison.OrdinalIgnoreCase)
            ? cookieRole
            : $"role_{cookieRole}";
    }

    /// <summary>
    /// 取得已驗證的 principal_id（兩套 auth 來源都看）。
    /// </summary>
    public static string GetPrincipalId(HttpContext ctx)
    {
        var brokerPid = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string;
        if (!string.IsNullOrEmpty(brokerPid)) return brokerPid;
        return ctx.Items[CurrentUserMiddleware.PrincipalKey] as string ?? "";
    }

    /// <summary>
    /// 從 HttpContext.Items 取得已驗證的 session_id
    /// </summary>
    public static string GetSessionId(HttpContext ctx)
        => ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? "";

    /// <summary>
    /// 從 HttpContext.Items 取得已驗證的 task_id
    /// </summary>
    public static string GetTaskId(HttpContext ctx)
        => ctx.Items[BrokerAuthMiddleware.TaskIdKey] as string ?? "";

    /// <summary>
    /// 檢查呼叫者角色是否為管理員（兩套 auth 來源都認）。
    /// </summary>
    public static bool IsAdmin(HttpContext ctx)
        => GetRoleId(ctx) == "role_admin";

    /// <summary>
    /// M-1 修復：從 body 取得必填字串欄位，若為空則回傳錯誤
    /// </summary>
    public static bool TryGetRequired(JsonElement body, string propertyName,
        out string value, out IResult? error)
    {
        value = "";
        error = null;

        if (!body.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(prop.GetString()))
        {
            error = Results.BadRequest(ApiResponseHelper.Error($"Missing or empty required field: {propertyName}"));
            return false;
        }

        value = prop.GetString()!;
        return true;
    }

    /// <summary>
    /// M-1 修復：一次驗證多個必填欄位，回傳字典
    /// </summary>
    public static bool TryGetRequiredFields(JsonElement body, string[] fieldNames,
        out Dictionary<string, string> values, out IResult? error)
    {
        values = new Dictionary<string, string>(fieldNames.Length);
        error = null;

        foreach (var name in fieldNames)
        {
            if (!TryGetRequired(body, name, out var val, out error))
                return false;
            values[name] = val;
        }
        return true;
    }
}
