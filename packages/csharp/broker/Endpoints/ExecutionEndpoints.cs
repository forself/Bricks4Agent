using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/execution-requests/*（核心 PEP 端點）</summary>
public static class ExecutionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var exec = group.MapGroup("/execution-requests");

        exec.MapPost("/submit", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = GetBody(ctx);
            var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var taskId = ctx.Items[BrokerAuthMiddleware.TaskIdKey] as string ?? "";
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? "";
            var traceId = ctx.Items.TryGetValue("audit_trace_id", out var t) ? t as string ?? "" : Guid.NewGuid().ToString("N");

            var capabilityId = body.GetProperty("capability_id").GetString() ?? "";
            var intent = body.TryGetProperty("intent", out var i) ? i.GetString() ?? "" : "";
            var payload = body.TryGetProperty("payload", out var p) ? p.GetRawText() : "{}";
            var idempotencyKey = body.GetProperty("idempotency_key").GetString() ?? "";

            var request = broker.SubmitExecutionRequest(
                principalId, taskId, sessionId, capabilityId,
                intent, payload, idempotencyKey, traceId);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                request_id = request.RequestId,
                execution_state = request.ExecutionState.ToString(),
                policy_decision = request.PolicyDecision?.ToString(),
                policy_reason = request.PolicyReason,
                result_payload = request.ResultPayload
            }));
        });

        exec.MapPost("/query", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = GetBody(ctx);
            var requestId = body.GetProperty("request_id").GetString() ?? "";

            var request = broker.GetExecutionRequest(requestId);
            if (request == null)
                return Results.NotFound(ApiResponseHelper.Error("Execution request not found.", 404));

            return Results.Ok(ApiResponseHelper.Success(request));
        });
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
