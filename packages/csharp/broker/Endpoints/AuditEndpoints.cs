using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/audit/*</summary>
public static class AuditEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var audit = group.MapGroup("/audit");

        audit.MapPost("/query", (HttpContext ctx, IAuditService auditService) =>
        {
            var body = GetBody(ctx);
            var eventType = body.TryGetProperty("event_type", out var et) ? et.GetString() : null;
            var principalId = body.TryGetProperty("principal_id", out var pid) ? pid.GetString() : null;
            var taskId = body.TryGetProperty("task_id", out var tid) ? tid.GetString() : null;
            var offset = body.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
            var limit = body.TryGetProperty("limit", out var l) ? l.GetInt32() : 50;

            var events = auditService.QueryEvents(eventType, principalId, taskId, offset, limit);
            return Results.Ok(ApiResponseHelper.Success(events));
        });

        audit.MapPost("/trace", (HttpContext ctx, IAuditService auditService) =>
        {
            var body = GetBody(ctx);
            var traceId = body.GetProperty("trace_id").GetString() ?? "";

            var events = auditService.GetTraceEvents(traceId);
            return Results.Ok(ApiResponseHelper.Success(events));
        });

        audit.MapPost("/verify", (HttpContext ctx, IAuditService auditService) =>
        {
            var body = GetBody(ctx);
            var traceId = body.GetProperty("trace_id").GetString() ?? "";

            var isValid = auditService.VerifyTraceIntegrity(traceId);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                trace_id = traceId,
                integrity_valid = isValid
            }));
        });
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
