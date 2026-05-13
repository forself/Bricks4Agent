using Broker.Helpers;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/audit/* — 稽核查詢（非 admin 僅可查自身紀錄）</summary>
public static class AuditEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var audit = group.MapGroup("/audit");

        audit.MapPost("/query", (HttpContext ctx, IAuditService auditService) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var callerPrincipalId = RequestBodyHelper.GetPrincipalId(ctx);
            var isAdmin = RequestBodyHelper.IsAdmin(ctx);

            var eventType = body.TryGetProperty("event_type", out var et) ? et.GetString() : null;
            var requestedPrincipalId = body.TryGetProperty("principal_id", out var pid) ? pid.GetString() : null;
            var taskId = body.TryGetProperty("task_id", out var tid) ? tid.GetString() : null;
            var offset = body.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
            var limit = body.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;

            // 安全限制：非 admin 僅可查詢自己的稽核紀錄
            if (!isAdmin)
            {
                if (!string.IsNullOrEmpty(requestedPrincipalId) && requestedPrincipalId != callerPrincipalId)
                {
                    return Results.Json(
                        ApiResponseHelper.Error("Forbidden: can only query own audit events.", 403),
                        statusCode: 403);
                }
                requestedPrincipalId = callerPrincipalId;
            }

            // 限制單次查詢量（防止 DoS）
            if (limit > 200) limit = 200;
            if (offset < 0) offset = 0;

            var events = auditService.QueryEvents(eventType, requestedPrincipalId, taskId, offset, limit);
            return Results.Ok(ApiResponseHelper.Success(events));
        });

        audit.MapPost("/trace", (HttpContext ctx, IAuditService auditService) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var traceId = body.GetProperty("trace_id").GetString() ?? "";

            var events = auditService.GetTraceEvents(traceId);
            return Results.Ok(ApiResponseHelper.Success(events));
        });

        audit.MapPost("/verify", (HttpContext ctx, IAuditService auditService) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var traceId = body.GetProperty("trace_id").GetString() ?? "";

            var isValid = auditService.VerifyTraceIntegrity(traceId);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                trace_id = traceId,
                integrity_valid = isValid
            }));
        });

        // ── GET /api/v1/audit/traces ── 列出最近的 trace（給 Tracing Dashboard）
        // Query: principal_id / capability_id / offset / limit
        // 非 admin 自動限制為自己的 trace。
        audit.MapGet("/traces", (HttpContext ctx, IAuditService auditService) =>
        {
            var callerPrincipalId = RequestBodyHelper.GetPrincipalId(ctx);
            var isAdmin = RequestBodyHelper.IsAdmin(ctx);
            var q = ctx.Request.Query;

            var pid = q.TryGetValue("principal_id", out var p) ? p.ToString() : null;
            var cap = q.TryGetValue("capability_id", out var c) ? c.ToString() : null;
            var offset = int.TryParse(q["offset"].ToString(), out var o) ? Math.Max(o, 0) : 0;
            var limit  = int.TryParse(q["limit"].ToString(),  out var l) ? Math.Clamp(l, 1, 200) : 50;
            var includeHttp = bool.TryParse(q["include_http"].ToString(), out var ih) && ih;

            // 非 admin 強制只看自己
            if (!isAdmin)
            {
                if (!string.IsNullOrEmpty(pid) && pid != callerPrincipalId)
                {
                    return Results.Json(
                        ApiResponseHelper.Error("Forbidden: can only list own traces.", 403),
                        statusCode: 403);
                }
                pid = callerPrincipalId;
            }

            var traces = auditService.ListRecentTraces(
                string.IsNullOrEmpty(pid) ? null : pid,
                string.IsNullOrEmpty(cap) ? null : cap,
                offset, limit, includeHttp);

            return Results.Ok(ApiResponseHelper.Success(traces.Select(t => new
            {
                trace_id        = t.TraceId,
                first_at        = t.FirstAt,
                last_at         = t.LastAt,
                duration_ms     = t.DurationMs,
                event_count     = t.EventCount,
                first_event     = t.FirstEventType,
                last_event      = t.LastEventType,
                principal_id    = t.PrincipalId,
                task_id         = t.TaskId,
                capability_id   = t.CapabilityId,
                status          = t.Status,
            })));
        });

        // ── GET /api/v1/audit/latency?since_minutes=60 ──
        // 每 capability 的 p50/p95/p99/max + 分布 histogram
        audit.MapGet("/latency", (HttpContext ctx, IAuditService auditService) =>
        {
            var q = ctx.Request.Query;
            var sinceMin = int.TryParse(q["since_minutes"].ToString(), out var s) ? Math.Clamp(s, 1, 1440) : 60;
            var stats = auditService.GetLatencyStats(sinceMin);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                since_minutes = sinceMin,
                generated_at  = DateTime.UtcNow,
                capabilities  = stats.Select(c => new
                {
                    capability_id = c.CapabilityId,
                    calls         = c.Calls,
                    succeeded     = c.Succeeded,
                    failed        = c.Failed,
                    p50_ms        = c.P50Ms,
                    p95_ms        = c.P95Ms,
                    p99_ms        = c.P99Ms,
                    max_ms        = c.MaxMs,
                    avg_ms        = c.AvgMs,
                    distribution  = c.Distribution.Select(b => new
                    {
                        label = b.Label,
                        lower_ms = b.LowerMs,
                        upper_ms = b.UpperMs == long.MaxValue ? (long?)null : b.UpperMs,
                        count = b.Count,
                    }),
                }),
            }));
        });

        // ── GET /api/v1/audit/topology?since_minutes=60 ──
        // (principal, capability) 聚合邊，給 topology.html 桑基圖
        audit.MapGet("/topology", (HttpContext ctx, IAuditService auditService) =>
        {
            var q = ctx.Request.Query;
            var sinceMin = int.TryParse(q["since_minutes"].ToString(), out var s) ? Math.Clamp(s, 1, 1440) : 60;
            var edges = auditService.GetTopology(sinceMin);

            // 預先按 principal / capability 排好、前端不用再算
            var principals = edges.GroupBy(e => e.Principal)
                .Select(g => new { id = g.Key, calls = g.Sum(x => x.CallsTotal) })
                .OrderByDescending(x => x.calls).ToList();
            var capabilities = edges.GroupBy(e => e.Capability)
                .Select(g => new
                {
                    id = g.Key,
                    calls = g.Sum(x => x.CallsTotal),
                    succeeded = g.Sum(x => x.CallsSucceeded),
                    failed = g.Sum(x => x.CallsFailed),
                })
                .OrderByDescending(x => x.calls).ToList();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                since_minutes = sinceMin,
                generated_at = DateTime.UtcNow,
                principals,
                capabilities,
                edges = edges.Select(e => new
                {
                    from = e.Principal,
                    to = e.Capability,
                    calls = e.CallsTotal,
                    succeeded = e.CallsSucceeded,
                    failed = e.CallsFailed,
                }),
            }));
        });

        // ── W13：bot-node hybrid LLM reasoning audit ──
        // bot-node 在 client side spawn `claude --print`、broker 看不到 reasoning。
        // 這條 endpoint 讓 bot-node 在 dispatch 前同步推 LLM 完整 response、broker 落表。
        // 僅供 audit/forensics、不參與 dispatch 決策。
        // Auth：要 X-Internal-Bot-Token（bot-node 本來就有）、不開 cookie session。
        audit.MapPost("/llm-reasoning", (HttpContext ctx, BrokerDb db) =>
        {
            // bot-node 拿 X-Internal-Bot-Token 過 InternalBotAuthMiddleware 後 role 是 "user" 或 "admin"
            // 沒過 middleware 的話 GetRoleId 回空字串、就拒絕
            var role = RequestBodyHelper.GetRoleId(ctx);
            if (string.IsNullOrEmpty(role) || (role != "role_user" && role != "role_admin"))
            {
                return Results.Json(ApiResponseHelper.Error("internal bot token required", 401), statusCode: 401);
            }

            var body = RequestBodyHelper.GetBody(ctx);
            var entry = new LlmReasoningAuditEntry
            {
                OccurredAt   = DateTime.UtcNow,
                Source       = body.TryGetProperty("source",        out var s)  ? s.GetString()  ?? "discord" : "discord",
                UserId       = body.TryGetProperty("user_id",       out var u)  ? u.GetString()  ?? ""        : "",
                ChannelId    = body.TryGetProperty("channel_id",    out var c)  ? c.GetString()  ?? ""        : "",
                Turn         = body.TryGetProperty("turn",          out var t) && t.TryGetInt32(out var tv) ? tv : 0,
                LlmReasoning = body.TryGetProperty("llm_reasoning", out var r)  ? r.GetString()  ?? ""        : "",
                ToolName     = body.TryGetProperty("tool_name",     out var tn) ? tn.GetString() ?? ""        : "",
                ToolArgs     = body.TryGetProperty("tool_args",     out var ta) ? ta.GetRawText()             : "{}",
                AclAllowed   = body.TryGetProperty("acl_allowed",   out var al) && al.GetBoolean(),
                DispatchResult = body.TryGetProperty("dispatch_result", out var dr) ? dr.GetString() ?? "pending" : "pending",
            };

            // 防 DoS：reasoning 字串截到 8 KB（足夠 LLM 一輪 response、不會撐爆 DB）
            if (entry.LlmReasoning.Length > 8192) entry.LlmReasoning = entry.LlmReasoning[..8192] + "…[truncated]";
            if (entry.ToolArgs.Length > 4096) entry.ToolArgs = entry.ToolArgs[..4096] + "…[truncated]";

            db.Insert(entry);
            return Results.Ok(ApiResponseHelper.Success(new { entry_id = entry.EntryId }));
        });

        // 查詢用：admin / user 各自只看到自己 user_id 的紀錄
        audit.MapGet("/llm-reasoning", (HttpContext ctx, BrokerDb db, HttpRequest req) =>
        {
            var callerPrincipalId = RequestBodyHelper.GetPrincipalId(ctx);
            var isAdmin = RequestBodyHelper.IsAdmin(ctx);
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var lv) ? Math.Min(lv, 200) : 50;
            var filterUser = req.Query.TryGetValue("user_id", out var u) ? u.ToString() : null;
            if (!isAdmin) filterUser = callerPrincipalId;

            var sql = "SELECT * FROM llm_reasoning_audit WHERE 1=1";
            var args = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(filterUser))
            {
                sql += " AND user_id = $uid";
                args["$uid"] = filterUser;
            }
            sql += " ORDER BY entry_id DESC LIMIT $limit";
            args["$limit"] = limit;

            var rows = db.Query<LlmReasoningAuditEntry>(sql, args);
            return Results.Ok(ApiResponseHelper.Success(new { count = rows.Count, entries = rows }));
        });
    }
}
