using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Risk API — 透過 IExecutionDispatcher 轉發至 risk-worker
///
/// POST /api/v1/risk/check    — 下單前風控檢查
/// GET  /api/v1/risk/rules    — 列出風控規則
/// PUT  /api/v1/risk/rules    — 更新風控規則
/// </summary>
public static class RiskEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var risk = group.MapGroup("/risk");

        risk.MapPost("/check", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("risk.check"))
                return Results.Ok(ApiResponseHelper.Error("risk-worker not connected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest("risk.check", "pre_order", body));
            return ToResponse(result);
        });

        risk.MapGet("/rules", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("risk.check"))
                return Results.Ok(ApiResponseHelper.Error("risk-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest("risk.check", "get_rules"));
            return ToResponse(result);
        });

        risk.MapPut("/rules", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("risk.check"))
                return Results.Ok(ApiResponseHelper.Error("risk-worker not connected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest("risk.check", "set_rules", body));
            return ToResponse(result);
        });
    }

    private static ApprovedRequest BuildRequest(
        string capabilityId, string route, string payload = "{}")
        => new()
        {
            RequestId    = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId,
            Route        = route,
            Payload      = payload,
            Scope        = "{}",
            PrincipalId  = "system",
            TaskId       = "dashboard",
            SessionId    = "dashboard"
        };

    private static IResult ToResponse(ExecutionResult result)
    {
        if (!result.Success)
            return Results.Ok(ApiResponseHelper.Error(result.ErrorMessage ?? "dispatch failed"));

        try
        {
            var data = JsonDocument.Parse(result.ResultPayload ?? "{}");
            return Results.Ok(ApiResponseHelper.Success(data.RootElement));
        }
        catch
        {
            return Results.Ok(ApiResponseHelper.Success(result.ResultPayload ?? "{}"));
        }
    }
}
