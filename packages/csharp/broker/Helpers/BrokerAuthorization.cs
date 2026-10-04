using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Helpers;

/// <summary>
/// 端點層的角色與擁有者檢查（縱深防禦，在 BrokerAuthMiddleware 驗證 token 之後執行）。
/// 角色與任務都取自已驗證的 token claims，不採信 body 內的自述身分。
/// </summary>
public static class BrokerAuthorization
{
    /// <summary>呼叫者是否為管理員（沿用 RequestBodyHelper.IsAdmin 的判定）。</summary>
    public static bool IsAdmin(HttpContext ctx) => RequestBodyHelper.IsAdmin(ctx);

    /// <summary>非管理員回 403。</summary>
    public static bool TryRequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (IsAdmin(ctx))
        {
            denied = null!;
            return true;
        }

        denied = Forbidden("Forbidden: admin role required.");
        return false;
    }

    /// <summary>
    /// 任務存取：管理員，或 token 的 TaskId 等於該任務，或任務的 SubmittedBy 等於 token 的 principal；否則 403。
    /// task 為 null（不存在）時，只有管理員或 token 本身綁定該 taskId 者會通過，讓呼叫端照常回 404；
    /// 其他人一律 403，避免以回應碼探測任務是否存在。
    /// </summary>
    public static bool TryRequireTaskAccess(HttpContext ctx, BrokerTask? task, string taskId, out IResult denied)
    {
        if (HasTaskAccess(ctx, task, taskId))
        {
            denied = null!;
            return true;
        }

        denied = Forbidden("Forbidden: task access denied.");
        return false;
    }

    /// <summary>以 taskId 查出任務後做 <see cref="TryRequireTaskAccess(HttpContext, BrokerTask?, string, out IResult)"/>。</summary>
    public static bool TryRequireTaskAccess(HttpContext ctx, IBrokerService broker, string? taskId, out IResult denied)
    {
        var normalizedTaskId = taskId ?? string.Empty;
        var task = string.IsNullOrWhiteSpace(normalizedTaskId) ? null : broker.GetTask(normalizedTaskId);
        return TryRequireTaskAccess(ctx, task, normalizedTaskId, out denied);
    }

    public static bool HasTaskAccess(HttpContext ctx, BrokerTask? task, string taskId)
    {
        if (IsAdmin(ctx))
        {
            return true;
        }

        var callerTaskId = RequestBodyHelper.GetTaskId(ctx);
        var effectiveTaskId = task?.TaskId ?? taskId;
        if (!string.IsNullOrEmpty(callerTaskId) &&
            !string.IsNullOrEmpty(effectiveTaskId) &&
            string.Equals(callerTaskId, effectiveTaskId, StringComparison.Ordinal))
        {
            return true;
        }

        var callerPrincipalId = RequestBodyHelper.GetPrincipalId(ctx);
        return task != null &&
               !string.IsNullOrEmpty(callerPrincipalId) &&
               string.Equals(task.SubmittedBy, callerPrincipalId, StringComparison.Ordinal);
    }

    /// <summary>
    /// 對端點或群組加上管理員檢查（endpoint filter）。
    /// 只作用在 <see cref="BrokerAuthPolicy.ScopedToken"/> 端點：角色來自 scoped token，
    /// 以其他機制驗證的端點（例如 worker 簽章）由各自的政策把關，不受此 filter 影響。
    /// </summary>
    public static TBuilder RequireBrokerAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (filterContext, next) =>
        {
            var ctx = filterContext.HttpContext;
            if (BrokerAuthPolicyResolver.Resolve(ctx) == BrokerAuthPolicy.ScopedToken &&
                !TryRequireAdmin(ctx, out var denied))
            {
                return denied;
            }

            return await next(filterContext);
        });

        return builder;
    }

    private static IResult Forbidden(string message)
        => Results.Json(ApiResponseHelper.Error(message, StatusCodes.Status403Forbidden), statusCode: StatusCodes.Status403Forbidden);
}
