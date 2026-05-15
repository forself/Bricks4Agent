using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// G1 — Policy Replay Debugger
///
/// Sandbox 模式跑 Benson 的 PolicyEngine、不真派發、不寫 audit、純看「這個 capability + payload
/// 在當前 PolicyEngine 規則下會被 Allow / Deny + 為什麼」。
///
/// 兩個 endpoint：
///
/// POST /api/v1/replay/evaluate
///   body: { capability_id, payload (string JSON), principal_id (optional) }
///   resp: { decision, reason, capability_snapshot, gates_walked }
///
/// GET /api/v1/replay/from-trace?trace_id=trc_xxx
///   從 audit_events / execution_requests 把原始 payload 撈出來、給 admin 一鍵套用、
///   再走 /replay/evaluate 看當下決策。
///
/// 為什麼要做：
/// - Benson 的 PolicyEngine 7 條規則寫在程式碼裡、admin 看 audit_events 只看到 Allow/Deny 結果、
///   不知道是哪條規則 fired
/// - replay 把 PolicyEngine 變成「可解釋」、admin 改 capability schema 後可立刻測試
/// - 答辯：「Benson 設計的 PolicyEngine 不是 black box、broker 提供 sandbox replay UI」
///
/// 不真派發、不寫 audit、不 mutate state。Caller 必須 admin。
/// </summary>
public static class PolicyReplayEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var rp = group.MapGroup("/replay");

        rp.MapPost("/evaluate", (HttpContext ctx,
            IPolicyEngine policyEngine, ICapabilityCatalog catalog, IRevocationService revoke) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            var capId = body.TryGetProperty("capability_id", out var c) ? c.GetString() ?? "" : "";
            var payload = body.TryGetProperty("payload", out var p) ? p.GetString() ?? "{}" : "{}";
            var principalId = body.TryGetProperty("principal_id", out var pp) ? pp.GetString() ?? "prn_replay" : "prn_replay";

            if (string.IsNullOrWhiteSpace(capId))
                return Results.BadRequest(ApiResponseHelper.Error("capability_id required"));

            var capability = catalog.GetCapability(capId);
            if (capability == null)
                return Results.Ok(ApiResponseHelper.Success(new {
                    decision = "Deny",
                    reason = $"Capability '{capId}' not in registry — would fail before PolicyEngine.",
                    gate_failed = "PRE_POLICY_CATALOG_LOOKUP",
                }));

            // 合成 sandbox grant + task：寬鬆設定（不模擬 quota / expires）。
            // 真實 dispatch 會額外檢 grant.ConsumeQuota；這裡只看 PolicyEngine 自己 7 條規則。
            var sandboxGrant = new CapabilityGrant {
                GrantId = "grt_replay",
                PrincipalId = principalId,
                CapabilityId = capId,
                ScopeOverride = "{}",
                RemainingQuota = -1,   // unlimited
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                Status = GrantStatus.Active,
            };
            var sandboxTask = new BrokerTask {
                TaskId = "task_replay",
                ScopeDescriptor = "{}",
            };
            var request = new ExecutionRequest {
                RequestId = "req_replay",
                TaskId = sandboxTask.TaskId,
                SessionId = "ses_replay",
                PrincipalId = principalId,
                CapabilityId = capId,
                Intent = "replay sandbox",
                RequestPayload = payload,
                TraceId = "trc_replay",
                IdempotencyKey = "replay",
            };

            var currentEpoch = revoke.GetCurrentEpoch();
            var result = policyEngine.Evaluate(request, capability, sandboxGrant, sandboxTask,
                currentEpoch: currentEpoch, tokenEpoch: currentEpoch);

            // 把 7 條規則的「會 fail 的可能位置」列出來、admin 對著看
            var gates = EnumerateGates(capability);

            return Results.Ok(ApiResponseHelper.Success(new {
                decision = result.Decision.ToString(),
                reason = result.Reason,
                retryable = result.Retryable,
                capability_snapshot = new {
                    id = capability.CapabilityId,
                    route = capability.Route,
                    risk = capability.RiskLevel.ToString(),
                    approval_policy = capability.ApprovalPolicy,
                    schema = capability.ParamSchema,
                },
                gates_walked = gates,
                evaluated_at = DateTime.UtcNow,
                note = "Replay 不寫 audit、不真派發；只跑 PolicyEngine 規則。" +
                       "結果用「當前」capability registry — 跟原始事件當時的 capability 可能不同（schema/risk 改過）",
            }));
        });

        rp.MapGet("/from-trace", (HttpContext ctx, IAuditService audit) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var traceId = ctx.Request.Query.TryGetValue("trace_id", out var t) ? t.ToString() : "";
            if (string.IsNullOrWhiteSpace(traceId))
                return Results.BadRequest(ApiResponseHelper.Error("trace_id query required"));

            var events = audit.GetTraceEvents(traceId);
            if (events.Count == 0)
                return Results.NotFound(ApiResponseHelper.Error($"trace {traceId} not found"));

            // 找 dispatch / execution event 拿 capability_id + payload
            string? capabilityId = null;
            string? extractedPayload = null;
            string? principalId = events[0].PrincipalId;

            foreach (var ev in events)
            {
                if (ev.EventType.StartsWith("DISPATCH", StringComparison.OrdinalIgnoreCase)
                    || ev.EventType.StartsWith("EXECUTION", StringComparison.OrdinalIgnoreCase)
                    || ev.EventType.Equals("REQUEST_RECEIVED", StringComparison.OrdinalIgnoreCase))
                {
                    if (capabilityId == null && !string.IsNullOrEmpty(ev.ResourceRef))
                        capabilityId = ev.ResourceRef;
                    try
                    {
                        using var doc = JsonDocument.Parse(ev.Details ?? "{}");
                        if (doc.RootElement.TryGetProperty("payload", out var pp)
                            && pp.ValueKind == JsonValueKind.String)
                            extractedPayload ??= pp.GetString();
                        if (capabilityId == null && doc.RootElement.TryGetProperty("capability_id", out var cc))
                            capabilityId = cc.GetString();
                    }
                    catch { /* details 可能不是 JSON */ }
                }
            }

            return Results.Ok(ApiResponseHelper.Success(new {
                trace_id = traceId,
                event_count = events.Count,
                event_types = events.Select(e => e.EventType).Distinct().ToList(),
                suggested = new {
                    capability_id = capabilityId,
                    principal_id = principalId,
                    payload = extractedPayload,
                },
                events = events.Select(e => new {
                    seq = e.TraceSeq, type = e.EventType, at = e.OccurredAt,
                    principal = e.PrincipalId, ref_ = e.ResourceRef,
                }),
                note = capabilityId == null
                    ? "trace 找到但 audit_events 沒記 capability_id（可能是 HTTP middleware trace、不是 dispatch trace）"
                    : "點 Replay → 把 capability_id + payload 帶進 evaluate endpoint",
            }));
        });
    }

    private static object[] EnumerateGates(Capability cap) => new object[]
    {
        new { gate = "G1 token_epoch", check = "tokenEpoch >= currentEpoch（kill switch 後舊 token 無效）" },
        new { gate = "G2 risk_level",  check = $"capability.risk ({cap.RiskLevel}) <= Medium（高風險預設 deny）" },
        new { gate = "G3 route_match", check = $"payload.route 必須等於 capability.route ({cap.Route})" },
        new { gate = "G4 scope",       check = "payload 的 resource 必須在 grant.ScopeOverride / task.ScopeDescriptor 內" },
        new { gate = "G5 path_sandbox",check = "payload 的 path 不能有 .. 或絕對路徑（純 dispatch capability 多半 N/A）" },
        new { gate = "G6 blacklist",   check = "payload 不含黑名單 command" },
        new { gate = "G7 schema",      check = $"payload.args 須通過 capability.ParamSchema JSON Schema 驗證" },
    };

    private static bool RequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (RequestBodyHelper.IsAdmin(ctx)) { denied = null!; return true; }
        denied = Results.Json(ApiResponseHelper.Error("Forbidden: admin role required.", 403), statusCode: 403);
        return false;
    }
}
