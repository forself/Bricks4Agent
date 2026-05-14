using Broker.Helpers;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Endpoints;

/// <summary>
/// Forensics endpoint —— 把同一條 trace / 同一個時間窗 / 同一個 symbol 相關的所有
/// audit_events + approval_requests + llm_reasoning_audit 拉出來、按 ts 排序回傳。
///
/// 用途：
///   - 答辯 demo「broker 是 control plane、看得到所有事的因」最具體展示
///   - 之後 ForensicsAgent 會再用 LLM 把這串時間軸組成自然語言 narrative
///
/// 安全：要 admin 或自己的 principal_id；非 admin 看到非自己的事件會被擋
/// （沿用 audit endpoint 同一套規則）
/// </summary>
public static class ForensicsEndpoints
{
    private record TimelineEvent(
        DateTime Ts,
        string Type,
        string? TraceId,
        string? PrincipalId,
        string Summary,
        object Raw);

    public static void Map(RouteGroupBuilder group)
    {
        var forensics = group.MapGroup("/forensics");

        // GET /api/v1/forensics/timeline?since=ISO&until=ISO&trace_id=X&symbol=BTC-USDT&limit=N
        // 預設 since = 1 小時前、until = now、limit = 200（cap 500）
        forensics.MapGet("/timeline", (HttpContext ctx, BrokerDb db, HttpRequest req) =>
        {
            var callerPrincipalId = RequestBodyHelper.GetPrincipalId(ctx);
            var isAdmin = RequestBodyHelper.IsAdmin(ctx);

            var since = req.Query.TryGetValue("since", out var sn) && DateTime.TryParse(sn.ToString(), out var snDt)
                ? snDt.ToUniversalTime() : DateTime.UtcNow.AddHours(-1);
            var until = req.Query.TryGetValue("until", out var un) && DateTime.TryParse(un.ToString(), out var unDt)
                ? unDt.ToUniversalTime() : DateTime.UtcNow;
            var traceId = req.Query.TryGetValue("trace_id", out var t) ? t.ToString() : null;
            var symbol  = req.Query.TryGetValue("symbol",   out var s) ? s.ToString() : null;
            var limit   = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var lv)
                ? Math.Min(lv, 500) : 200;

            // SQLite store DateTime as ISO 8601 text — BaseOrm parameter binding 會做轉換
            // 但 BETWEEN 對 string 比較才正確，故 since/until 用 ISO format
            var sinceStr = since.ToString("o");
            var untilStr = until.ToString("o");
            var symbolLike = symbol != null ? $"%{symbol}%" : null;

            // ── 1. audit_events ──
            var auditSql = "SELECT * FROM audit_events WHERE occurred_at BETWEEN @sinceStr AND @untilStr";
            if (!string.IsNullOrEmpty(traceId)) auditSql += " AND trace_id = @traceId";
            if (!string.IsNullOrEmpty(symbol))  auditSql += " AND (COALESCE(resource_ref,'') LIKE @symbolLike OR details LIKE @symbolLike)";
            if (!isAdmin)                       auditSql += " AND principal_id = @caller";
            auditSql += " ORDER BY occurred_at DESC LIMIT @limit";

            var auditEvents = db.Query<AuditEvent>(auditSql, new {
                sinceStr, untilStr, traceId, symbolLike, caller = callerPrincipalId, limit
            });

            // ── 2. approval_requests ── 一筆可能放到 timeline 的多個事件（requested/decided/dispatched）
            var aprSql = "SELECT * FROM approval_requests WHERE requested_at BETWEEN @sinceStr AND @untilStr";
            if (!string.IsNullOrEmpty(traceId)) aprSql += " AND trace_id = @traceId";
            if (!string.IsNullOrEmpty(symbol))  aprSql += " AND (route LIKE @symbolLike OR payload LIKE @symbolLike)";
            if (!isAdmin)                       aprSql += " AND principal_id = @caller";
            aprSql += " ORDER BY requested_at DESC LIMIT @limit";

            var approvals = db.Query<ApprovalRequest>(aprSql, new {
                sinceStr, untilStr, traceId, symbolLike, caller = callerPrincipalId, limit
            });

            // ── 3. llm_reasoning_audit (W13) ──
            var llmSql = "SELECT * FROM llm_reasoning_audit WHERE occurred_at BETWEEN @sinceStr AND @untilStr";
            if (!string.IsNullOrEmpty(symbol))  llmSql += " AND (tool_args LIKE @symbolLike OR llm_reasoning LIKE @symbolLike)";
            if (!isAdmin)                       llmSql += " AND user_id = @caller";
            llmSql += " ORDER BY occurred_at DESC LIMIT @limit";

            var llmReasoning = db.Query<LlmReasoningAuditEntry>(llmSql, new {
                sinceStr, untilStr, symbolLike, caller = callerPrincipalId, limit
            });

            // ── 合併 timeline、按 ts 倒序 ──
            var events = new List<TimelineEvent>();
            foreach (var e in auditEvents)
            {
                events.Add(new TimelineEvent(
                    e.OccurredAt, "audit", e.TraceId, e.PrincipalId,
                    $"{e.EventType}{(string.IsNullOrEmpty(e.ResourceRef) ? "" : " · " + e.ResourceRef)}",
                    e));
            }
            foreach (var a in approvals)
            {
                events.Add(new TimelineEvent(
                    a.RequestedAt, "approval_requested", a.TraceId, a.PrincipalId,
                    $"approval requested: {a.CapabilityId} · {a.Route}",
                    new { a.ApprovalId, a.Status, a.CapabilityId, a.Route, payload_brief = TrimPayload(a.Payload) }));

                if (a.DecidedAt.HasValue)
                {
                    events.Add(new TimelineEvent(
                        a.DecidedAt.Value, $"approval_{a.Status}", a.TraceId, a.DecidedBy,
                        $"approval {a.Status} by {a.DecidedBy ?? "(unknown)"}: {a.DecisionReason ?? "(no reason)"}",
                        new { a.ApprovalId, a.Status, a.DecidedBy, a.DecisionReason }));
                }
                if (a.DispatchedAt.HasValue)
                {
                    events.Add(new TimelineEvent(
                        a.DispatchedAt.Value, "approval_dispatched", a.TraceId, a.DispatchedBy,
                        $"dispatched to worker by {a.DispatchedBy ?? "(unknown)"}",
                        new { a.ApprovalId, a.DispatchedBy }));
                }
            }
            foreach (var lr in llmReasoning)
            {
                events.Add(new TimelineEvent(
                    lr.OccurredAt, "llm_reasoning", null, lr.UserId,
                    $"[{lr.Source}] LLM → {lr.ToolName} (acl: {(lr.AclAllowed ? "allow" : "block")}, dispatch: {lr.DispatchResult})",
                    lr));
            }

            var sorted = events.OrderByDescending(e => e.Ts).Take(limit).ToList();

            // ── 統計摘要 ──
            var typesAgg = sorted.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count());
            var uniqueTraces = sorted.Select(e => e.TraceId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            var uniquePrincipals = sorted.Select(e => e.PrincipalId).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                query = new
                {
                    since = sinceStr, until = untilStr, trace_id = traceId, symbol, limit,
                    scope = isAdmin ? "all" : "self"
                },
                count = sorted.Count,
                events = sorted.Select(e => new
                {
                    ts = e.Ts, type = e.Type, trace_id = e.TraceId, principal_id = e.PrincipalId,
                    summary = e.Summary, raw = e.Raw
                }),
                summary = new
                {
                    types = typesAgg,
                    unique_traces = uniqueTraces,
                    unique_principals = uniquePrincipals,
                    audit_events_count = auditEvents.Count,
                    approvals_count = approvals.Count,
                    llm_reasoning_count = llmReasoning.Count
                }
            }));
        });
    }

    /// <summary>截短 payload JSON 字串、避免時間軸太肥</summary>
    private static string TrimPayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return "";
        return payload.Length > 240 ? payload[..240] + "…" : payload;
    }
}
