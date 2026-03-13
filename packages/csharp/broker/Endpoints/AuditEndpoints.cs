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
    }
}
