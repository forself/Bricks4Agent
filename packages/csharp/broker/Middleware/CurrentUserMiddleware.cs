using Broker.Services;

namespace Broker.Middleware;

/// <summary>
/// 把當前登入身份（從 b4a_session cookie）解析出來、塞進 HttpContext.Items、
/// 給後續 endpoints 用。**不擋未登入的請求**——擋與不擋是 endpoint 自己決定的。
///
/// HttpContext.Items keys：
///   "current_principal_id"  → string（未登入 = null）
///   "current_role"          → "admin" / "user" / null
///
/// 為什麼不擋：很多 endpoint（health、auth/login 自己、工具用的內部 capability dispatch）
/// 不需要登入；統一擋會引入一堆 whitelist。改成「endpoint 用 GetCurrentUser() 拿、null 自己決定怎麼處理」。
/// </summary>
public class CurrentUserMiddleware
{
    public const string PrincipalKey = "current_principal_id";
    public const string RoleKey = "current_role";

    private readonly RequestDelegate _next;

    public CurrentUserMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, PrincipalAuthService auth)
    {
        var session = auth.GetAuthenticatedSession(context);
        if (session != null)
        {
            context.Items[PrincipalKey] = session.PrincipalId;
            context.Items[RoleKey] = session.Role;
        }
        await _next(context);
    }
}

public static class CurrentUserExtensions
{
    /// <summary>從 HttpContext 拿目前登入者；未登入回 (null, null)。</summary>
    public static (string? PrincipalId, string? Role) GetCurrentUser(this HttpContext ctx)
    {
        var pid = ctx.Items.TryGetValue(CurrentUserMiddleware.PrincipalKey, out var p) ? p as string : null;
        var role = ctx.Items.TryGetValue(CurrentUserMiddleware.RoleKey, out var r) ? r as string : null;
        return (pid, role);
    }

    public static bool IsAdmin(this HttpContext ctx)
    {
        var (_, role) = ctx.GetCurrentUser();
        return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseCurrentUser(this IApplicationBuilder app)
        => app.UseMiddleware<CurrentUserMiddleware>();
}
