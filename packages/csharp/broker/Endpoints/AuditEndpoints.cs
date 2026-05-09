using Broker.Helpers;
using BrokerCore.Services;

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
    }
}
