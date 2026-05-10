using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Trading API — 透過 IExecutionDispatcher 轉發至 trading-worker
///
/// Orders:
///   POST /api/v1/trading/order            — 下單
///   DELETE /api/v1/trading/order/{id}      — 取消訂單
///   GET  /api/v1/trading/order/{id}        — 查詢訂單
///   GET  /api/v1/trading/orders            — 列出訂單
///
/// Account:
///   GET  /api/v1/trading/account           — 帳戶摘要
///   GET  /api/v1/trading/positions         — 持倉
///   GET  /api/v1/trading/trades            — 成交紀錄
///   GET  /api/v1/trading/exchanges         — 可用交易所
/// </summary>
public static class TradingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var trading = group.MapGroup("/trading");

        // ── Orders ──────────────────────────────────────────────────

        trading.MapPost("/order", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "place_order", body));
            return ToResponse(result);
        });

        trading.MapDelete("/order/{externalId}", async (
            string externalId,
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var payload = JsonSerializer.Serialize(new { exchange, external_id = externalId });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "cancel_order", payload));
            return ToResponse(result);
        });

        trading.MapGet("/order/{orderId}", async (
            string orderId,
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new { order_id = orderId });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.order", "get_order", payload));
            return ToResponse(result);
        });

        trading.MapGet("/orders", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                symbol = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : (string?)null,
                status = req.Query.TryGetValue("status", out var st) ? st.ToString() : (string?)null,
                limit  = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50,
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "list_orders", payload));
            return ToResponse(result);
        });

        // ── Account ─────────────────────────────────────────────────

        trading.MapGet("/account", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var payload = JsonSerializer.Serialize(new { exchange });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_account", payload));
            return ToResponse(result);
        });

        trading.MapGet("/positions", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var payload = JsonSerializer.Serialize(new { exchange });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_positions", payload));
            return ToResponse(result);
        });

        trading.MapGet("/trades", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca",
                symbol   = req.Query.TryGetValue("symbol",   out var s)  ? s.ToString()  : (string?)null,
                limit    = req.Query.TryGetValue("limit",    out var l) && int.TryParse(l, out var n) ? n : 50,
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_trades", payload));
            return ToResponse(result);
        });

        trading.MapGet("/exchanges", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.account", "list_exchanges"));
            return ToResponse(result);
        });
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 從 HttpContext 抓真實的 principal/role（由 BrokerAuthMiddleware /
    /// InternalBotAuthMiddleware 注入）。硬寫 PrincipalId="system" 會讓
    /// PoolDispatcher approval gate 直接放行（system 是內部背景任務 exemption）、
    /// 任何走 HTTP 進來的單就繞過 admin 核准了——絕對不行。
    /// </summary>
    private static ApprovedRequest BuildRequest(
        HttpContext ctx, string capabilityId, string route, string payload = "{}")
    {
        var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "system";
        var roleId      = ctx.Items[BrokerAuthMiddleware.RoleIdKey]      as string ?? "system";
        var taskId      = ctx.Items[BrokerAuthMiddleware.TaskIdKey]      as string ?? "dashboard";
        var sessionId   = ctx.Items[BrokerAuthMiddleware.SessionIdKey]   as string ?? "dashboard";
        return new ApprovedRequest
        {
            RequestId    = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId,
            Route        = route,
            Payload      = payload,
            Scope        = "{}",
            PrincipalId  = principalId,
            Role         = roleId,
            TaskId       = taskId,
            SessionId    = sessionId,
        };
    }

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
