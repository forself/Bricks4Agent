using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Broker.Endpoints;

/// <summary>
/// 永續合約 API（BingX perpetual swap）。
/// 跟既有 /api/v1/trading/* 故意分開：spot 跟 perp 是不同帳戶體系、不能混用。
///
/// Routes:
///   GET  /api/v1/perpetual/exchanges            列出已連線的 perpetual exchanges
///   GET  /api/v1/perpetual/account?exchange=bingx
///   GET  /api/v1/perpetual/positions?exchange=bingx
///   POST /api/v1/perpetual/order                {exchange, symbol, side, position_side, order_type, quantity, leverage, ...}
///   DELETE /api/v1/perpetual/order              ?exchange=bingx&symbol=BTC-USDT&order_id=12345
///   GET  /api/v1/perpetual/order                ?exchange&symbol&order_id (status query)
///   GET  /api/v1/perpetual/open-orders?exchange=bingx[&symbol=BTC-USDT]
///   POST /api/v1/perpetual/leverage             {exchange, symbol, position_side, leverage}
///   GET  /api/v1/perpetual/mark-price?exchange=bingx&symbol=BTC-USDT
/// </summary>
public static class PerpetualEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var p = group.MapGroup("/perpetual");

        p.MapGet("/exchanges", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpContext ctx) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected (or perpetual not enabled)"));
            var r = await dispatcher.DispatchAsync(Build(ctx, "trading.perpetual", "list_exchanges", "{}"));
            return ToResponse(r);
        });

        p.MapGet("/account", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            var ex = req.Query.TryGetValue("exchange", out var e) ? e.ToString() : "bingx";
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "get_account", JsonSerializer.Serialize(new { exchange = ex })));
            return ToResponse(r);
        });

        p.MapGet("/positions", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            var ex = req.Query.TryGetValue("exchange", out var e) ? e.ToString() : "bingx";
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "get_positions", JsonSerializer.Serialize(new { exchange = ex })));
            return ToResponse(r);
        });

        p.MapPost("/order", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "place_order", body));
            return ToResponse(r);
        });

        p.MapDelete("/order", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            var ex = req.Query.TryGetValue("exchange", out var e) ? e.ToString() : "bingx";
            var sym = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : "";
            var oid = req.Query.TryGetValue("order_id", out var o) ? o.ToString() : "";
            var payload = JsonSerializer.Serialize(new { exchange = ex, symbol = sym, order_id = oid });
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "cancel_order", payload));
            return ToResponse(r);
        });

        p.MapGet("/order", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            var ex = req.Query.TryGetValue("exchange", out var e) ? e.ToString() : "bingx";
            var sym = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : "";
            var oid = req.Query.TryGetValue("order_id", out var o) ? o.ToString() : "";
            var payload = JsonSerializer.Serialize(new { exchange = ex, symbol = sym, order_id = oid });
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "get_order", payload));
            return ToResponse(r);
        });

        p.MapGet("/open-orders", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            var ex = req.Query.TryGetValue("exchange", out var e) ? e.ToString() : "bingx";
            var sym = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : null;
            var payload = JsonSerializer.Serialize(new { exchange = ex, symbol = sym });
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "get_open_orders", payload));
            return ToResponse(r);
        });

        p.MapPost("/leverage", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "set_leverage", body));
            return ToResponse(r);
        });

        p.MapGet("/mark-price", async (IWorkerRegistry registry, IExecutionDispatcher dispatcher, HttpRequest req) =>
        {
            if (!registry.HasAvailableWorker("trading.perpetual"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));
            var ex = req.Query.TryGetValue("exchange", out var e) ? e.ToString() : "bingx";
            var sym = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : "";
            var payload = JsonSerializer.Serialize(new { exchange = ex, symbol = sym });
            var r = await dispatcher.DispatchAsync(Build(req.HttpContext, "trading.perpetual", "get_mark_price", payload));
            return ToResponse(r);
        });
    }

    /// <summary>
    /// 從 HttpContext 抓真實的 principal/role/task/session（由 BrokerAuthMiddleware /
    /// InternalBotAuthMiddleware 注入）。
    /// 重要：絕對不能硬寫 PrincipalId="system"——PoolDispatcher 的 approval gate 對
    /// "system" 完全放行（讓 AutoTrader 等內部背景任務跑），任何走 HTTP 進來的單都
    /// 必須帶真實身份、否則 admin 核准就被繞過了。
    /// 沒帶 auth 的 case（極少、純內部測試）才 fallback "system"。
    /// </summary>
    private static ApprovedRequest Build(HttpContext ctx, string capability, string route, string payload)
    {
        var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "system";
        var roleId      = ctx.Items[BrokerAuthMiddleware.RoleIdKey]      as string ?? "system";
        var taskId      = ctx.Items[BrokerAuthMiddleware.TaskIdKey]      as string ?? "perpetual-api";
        var sessionId   = ctx.Items[BrokerAuthMiddleware.SessionIdKey]   as string ?? "perpetual-api";
        return new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = capability, Route = route, Payload = payload,
            Scope = "{}", PrincipalId = principalId, Role = roleId,
            TaskId = taskId, SessionId = sessionId,
        };
    }

    private static IResult ToResponse(BrokerCore.Contracts.ExecutionResult result)
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
