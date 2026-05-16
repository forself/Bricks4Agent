using System.Text.Json;
using Broker.Helpers;
using Broker.Services;
using BrokerCore;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// W14 安全加固 P1 + P5 — 緊急停機 / 唯讀鎖定
///
/// POST /api/v1/emergency/stop-all   — 觸發 KillSwitch（停 AutoTrader、所有 watch 設 inactive）
/// POST /api/v1/emergency/clear      — 解除 KillSwitch（不會自動重啟 AutoTrader、admin 自己決定）
/// POST /api/v1/emergency/lockdown   — 切換 ReadOnlyMode（{enable:true|false}）
/// GET  /api/v1/emergency/status     — 看當前狀態（兩個旗標 + 觸發者 + 時間）
///
/// 全部需 role_admin。所有觸發/解除動作都寫一筆 audit_events（KILL_SWITCH / KILL_SWITCH_CLEARED /
/// READONLY_LOCKDOWN_*）、demo / 真出事都能在 ForensicsEndpoints 拉時間軸看誰按的。
/// </summary>
public static class EmergencyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var em = group.MapGroup("/emergency");

        em.MapPost("/stop-all", (HttpContext ctx,
            IEmergencyState state,
            IAuditService audit) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var pid = RequestBodyHelper.GetPrincipalId(ctx);
            var body = RequestBodyHelper.GetBody(ctx);
            var reason = body.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

            // 設旗標：middleware 攔截後續寫操作
            state.TriggerKillSwitch(pid, reason);

            var traceId = IdGen.New("trc");
            audit.RecordEvent(traceId, "KILL_SWITCH",
                principalId: pid,
                resourceRef: "broker.emergency",
                details: JsonSerializer.Serialize(new {
                    reason, triggered_at = DateTime.UtcNow,
                }));

            return Results.Ok(ApiResponseHelper.Success(new {
                kill_switch = true,
                watches_paused = 0,
                trace_id = traceId,
                message = "Emergency stop triggered. Trading capability blocked. Manual /emergency/clear required to resume.",
            }));
        });

        em.MapPost("/clear", (HttpContext ctx,
            IEmergencyState state,
            IAuditService audit) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var pid = RequestBodyHelper.GetPrincipalId(ctx);
            var body = RequestBodyHelper.GetBody(ctx);
            var reason = body.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

            if (!state.KillSwitchActive)
                return Results.Ok(ApiResponseHelper.Success(new {
                    kill_switch = false, message = "Already cleared (no-op)."
                }));

            state.ClearKillSwitch(pid);
            var traceId = IdGen.New("trc");
            audit.RecordEvent(traceId, "KILL_SWITCH_CLEARED",
                principalId: pid,
                resourceRef: "broker.emergency",
                details: JsonSerializer.Serialize(new { reason, cleared_at = DateTime.UtcNow }));

            return Results.Ok(ApiResponseHelper.Success(new {
                kill_switch = false, trace_id = traceId,
                message = "Cleared. AutoTrader is still disabled — call /auto-trader/enable to resume.",
            }));
        });

        em.MapPost("/lockdown", (HttpContext ctx,
            IEmergencyState state,
            IAuditService audit) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var pid = RequestBodyHelper.GetPrincipalId(ctx);
            var body = RequestBodyHelper.GetBody(ctx);
            var enable = body.TryGetProperty("enable", out var e) && e.ValueKind == JsonValueKind.True;
            var reason = body.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

            state.SetReadOnly(enable, pid, reason);
            var traceId = IdGen.New("trc");
            audit.RecordEvent(traceId, enable ? "READONLY_LOCKDOWN_ON" : "READONLY_LOCKDOWN_OFF",
                principalId: pid,
                resourceRef: "broker.emergency",
                details: JsonSerializer.Serialize(new { reason, at = DateTime.UtcNow }));

            return Results.Ok(ApiResponseHelper.Success(new {
                read_only = enable, trace_id = traceId,
                message = enable
                    ? "Read-only mode ON. All write operations rejected (exempt: emergency, auth, health)."
                    : "Read-only mode OFF. Writes resumed.",
            }));
        });

        em.MapGet("/status", (IEmergencyState state) =>
        {
            return Results.Ok(ApiResponseHelper.Success(new {
                kill_switch = new {
                    active = state.KillSwitchActive,
                    at = state.KillSwitchAt,
                    by = state.KillSwitchBy,
                    reason = state.KillSwitchReason,
                },
                read_only = new {
                    active = state.ReadOnlyMode,
                    at = state.ReadOnlyAt,
                    by = state.ReadOnlyBy,
                    reason = state.ReadOnlyReason,
                },
            }));
        });
    }

    private static bool RequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (RequestBodyHelper.IsAdmin(ctx)) { denied = null!; return true; }
        denied = Results.Json(ApiResponseHelper.Error("Forbidden: admin role required.", 403), statusCode: 403);
        return false;
    }
}
