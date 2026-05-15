using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// H1 — Quota dashboard endpoints
///
/// GET /api/v1/quota/snapshot — 全 principal 當日 LLM token / dispatch 用量 + limit
/// GET /api/v1/quota/config   — 看當前 default + enforce mode
///
/// 沒寫 modify endpoint：quota 由 appsettings + env override 設定、不 runtime 改、避免攻擊者拿 admin 後 raise quota。
/// </summary>
public static class QuotaEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var q = group.MapGroup("/quota");

        q.MapGet("/snapshot", (HttpContext ctx, IPrincipalQuotaService quota) =>
        {
            if (!RequestBodyHelper.IsAdmin(ctx))
                return Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);

            var snap = quota.Snapshot();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                generated_at = DateTime.UtcNow,
                enforce_mode = quota.EnforceMode,
                default_llm_per_day = quota.DefaultDailyLlmTokens,
                default_dispatch_per_day = quota.DefaultDailyDispatches,
                principals = snap.Select(kv => new {
                    principal_id = kv.Key,
                    llm_tokens_used = kv.Value.LlmTokensUsed,
                    llm_tokens_limit = kv.Value.LlmTokensLimit,
                    llm_pct = kv.Value.LlmTokensLimit > 0
                        ? Math.Round(kv.Value.LlmTokensUsed * 100.0 / kv.Value.LlmTokensLimit, 1) : 0,
                    llm_over_limit = kv.Value.LlmOverLimit,
                    dispatches_used = kv.Value.DispatchesUsed,
                    dispatches_limit = kv.Value.DispatchesLimit,
                    dispatch_pct = kv.Value.DispatchesLimit > 0
                        ? Math.Round(kv.Value.DispatchesUsed * 100.0 / kv.Value.DispatchesLimit, 1) : 0,
                    dispatch_over_limit = kv.Value.DispatchOverLimit,
                    day_utc = kv.Value.DayUtc,
                }).OrderByDescending(p => p.llm_pct),
            }));
        });

        q.MapGet("/config", (IPrincipalQuotaService quota) => Results.Ok(ApiResponseHelper.Success(new {
            enforce_mode = quota.EnforceMode,
            default_llm_per_day = quota.DefaultDailyLlmTokens,
            default_dispatch_per_day = quota.DefaultDailyDispatches,
            note = "Quota 由 appsettings (Quota:*) + env (QUOTA_OVERRIDE_<pid>=llm,disp) 設定、不可 runtime 改",
        })));
    }
}
