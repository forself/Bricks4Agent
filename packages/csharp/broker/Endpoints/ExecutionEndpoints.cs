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

        exec.MapPost("/submit", async (HttpContext ctx, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            var taskId = RequestBodyHelper.GetTaskId(ctx);
            var sessionId = RequestBodyHelper.GetSessionId(ctx);
            var traceId = ctx.Items.TryGetValue("audit_trace_id", out var t) ? t as string ?? "" : Guid.NewGuid().ToString("N");

            // M-1 修復：驗證必填欄位
            if (!RequestBodyHelper.TryGetRequired(body, "capability_id", out var capabilityId, out var err))
                return err!;
            if (!RequestBodyHelper.TryGetRequired(body, "idempotency_key", out var idempotencyKey, out err))
                return err!;

            var intent = body.TryGetProperty("intent", out var i) ? i.GetString() ?? "" : "";
            var payload = body.TryGetProperty("payload", out var p) ? p.GetRawText() : "{}";

            // H-3 修復：proper await，消除 sync-over-async
            var request = await broker.SubmitExecutionRequestAsync(
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
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "request_id", out var requestId, out var err))
                return err!;

            var request = broker.GetExecutionRequest(requestId);
            if (request == null)
                return Results.NotFound(ApiResponseHelper.Error("Execution request not found.", 404));

            return Results.Ok(ApiResponseHelper.Success(request));
        });
    }
}
