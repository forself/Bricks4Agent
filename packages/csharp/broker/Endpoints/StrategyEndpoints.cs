using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Strategy API — 透過 IExecutionDispatcher 轉發至 strategy-worker
///
/// POST /api/v1/strategy/signal   — 產生訊號（body: {strategy, bars, symbol, ...}）
/// GET  /api/v1/strategy/list     — 列出可用策略
/// </summary>
public static class StrategyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var strategy = group.MapGroup("/strategy");

        strategy.MapPost("/signal", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest("strategy.signal", "evaluate", body));
            return ToResponse(result);
        });

        strategy.MapGet("/list", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest("strategy.signal", "list"));
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
