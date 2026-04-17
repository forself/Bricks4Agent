using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// 金融報價 Worker API — 透過 IExecutionDispatcher 轉發至 quote-worker
///
/// 所有端點均不需要加密 session（Bearer token 即可），
/// 適合儀表板直接以 getPlain() 輪詢。
/// </summary>
public static class QuoteWorkerEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var quote = group.MapGroup("/workers/quote");

        // ── GET /api/v1/workers/quote/prices — 最新報價 ───────────────
        quote.MapGet("/prices", async (
            IWorkerRegistry registry,
            IExecutionDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.prices"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest("quote.prices", "get_prices"));
            return ToResponse(result);
        });

        // ── GET /api/v1/workers/quote/history — Job 歷程 ─────────────
        quote.MapGet("/history", async (
            IWorkerRegistry registry,
            IExecutionDispatcher dispatcher,
            HttpRequest req,
            CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.history"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var take    = req.Query.TryGetValue("take", out var t) && int.TryParse(t, out var n) ? n : 20;
            var payload = JsonSerializer.Serialize(new { take });
            var result  = await dispatcher.DispatchAsync(BuildRequest("quote.history", "get_history", payload));
            return ToResponse(result);
        });

        // ── GET /api/v1/workers/quote/fetch — 立即觸發抓取 ──────────
        quote.MapGet("/fetch", async (
            IWorkerRegistry registry,
            IExecutionDispatcher dispatcher,
            CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.fetch_now"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest("quote.fetch_now", "trigger_fetch"));
            return ToResponse(result);
        });
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

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
