using Broker.Helpers;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// 匯出 API — CSV 下載
/// GET /api/v1/export/orders?exchange=alpaca — 匯出訂單
/// GET /api/v1/export/trades?exchange=alpaca — 匯出成交
/// </summary>
public static class ExportEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var exp = group.MapGroup("/export");

        exp.MapGet("/orders", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            ExchangeCredentialService credSvc, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            // 2026-06-03 安全:原本 fail-open(無認證 + PrincipalId=system 回預設帳戶)→ 加 login 守衛 + 憑證隔離。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Text("Login required", "text/plain", statusCode: 401);
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Text("trading-worker not connected", "text/plain", statusCode: 503);

            var exchange = req.Query.TryGetValue("exchange", out var ex0) ? ex0.ToString() : "bingx";
            var (deny, p) = ScopedPayload(ctx, credSvc, exchange);
            if (deny != null) return Results.Text(deny, "text/plain", statusCode: 403);
            p["limit"] = 500;
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.order", "list_orders", JsonSerializer.Serialize(p)));
            if (!result.Success) return Results.Text("Failed", "text/plain", statusCode: 500);

            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            var sb = new StringBuilder();
            sb.AppendLine("order_id,symbol,exchange,side,order_type,quantity,limit_price,stop_price,status,filled_qty,filled_price,created_at,filled_at");

            if (doc.TryGetProperty("orders", out var orders))
            {
                foreach (var o in orders.EnumerateArray())
                {
                    sb.AppendLine(string.Join(",",
                        G(o, "order_id"), G(o, "symbol"), G(o, "exchange"), G(o, "side"), G(o, "order_type"),
                        G(o, "quantity"), G(o, "limit_price"), G(o, "stop_price"), G(o, "status"),
                        G(o, "filled_qty"), G(o, "filled_price"), G(o, "created_at"), G(o, "filled_at")));
                }
            }

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "orders.csv");
        });

        exp.MapGet("/trades", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            ExchangeCredentialService credSvc, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            // 2026-06-03 安全:原本 fail-open → 加 login 守衛 + 憑證隔離(只匯出 caller 自己帳戶的成交)。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Text("Login required", "text/plain", statusCode: 401);
            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "bingx";
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Text("trading-worker not connected", "text/plain", statusCode: 503);

            var (deny, p) = ScopedPayload(ctx, credSvc, exchange);
            if (deny != null) return Results.Text(deny, "text/plain", statusCode: 403);
            p["limit"] = 500;
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.account", "get_trades", JsonSerializer.Serialize(p)));
            if (!result.Success) return Results.Text("Failed", "text/plain", statusCode: 500);

            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            var sb = new StringBuilder();
            sb.AppendLine("trade_id,order_id,symbol,exchange,side,quantity,price,fee,realized_pnl,executed_at");

            if (doc.TryGetProperty("trades", out var trades))
            {
                foreach (var t in trades.EnumerateArray())
                {
                    sb.AppendLine(string.Join(",",
                        G(t, "trade_id"), G(t, "order_id"), G(t, "symbol"), G(t, "exchange"), G(t, "side"),
                        G(t, "quantity"), G(t, "price"), G(t, "fee"), G(t, "realized_pnl"), G(t, "executed_at")));
                }
            }

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "trades.csv");
        });
    }

    private static string G(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return "";
        var s = v.ToString() ?? "";
        return s.Contains(',') ? $"\"{s}\"" : s;
    }

    // 2026-06-03 安全:用真實 caller principal/role(原寫死 "system" 會繞過 PoolDispatcher approval gate)。
    private static ApprovedRequest BuildRequest(HttpContext ctx, string cap, string route, string payload = "{}")
    {
        var pid = RequestBodyHelper.GetPrincipalId(ctx);
        var role = RequestBodyHelper.GetRoleId(ctx);
        return new()
        {
            RequestId = Guid.NewGuid().ToString("N"), CapabilityId = cap, Route = route,
            Payload = payload, Scope = "{}",
            PrincipalId = string.IsNullOrEmpty(pid) ? "system" : pid,
            Role = string.IsNullOrEmpty(role) ? "system" : role,
            TaskId = "export", SessionId = "export",
        };
    }

    // 匯出按 caller 憑證隔離:有自有憑證→注入看自己;非 admin 無憑證→拒(不掉進共用預設帳戶);admin 無憑證→env 預設。
    private static (string? Deny, Dictionary<string, object?> Payload) ScopedPayload(
        HttpContext ctx, ExchangeCredentialService credSvc, string exchange)
    {
        var d = new Dictionary<string, object?> { ["exchange"] = exchange };
        var pid = RequestBodyHelper.GetPrincipalId(ctx);
        if (!string.IsNullOrEmpty(pid))
        {
            var dec = credSvc.Resolve(pid, exchange);
            if (dec != null) { d["__credentials"] = new { api_key = dec.ApiKey, api_secret = dec.ApiSecret, is_demo = dec.IsDemo }; return (null, d); }
        }
        if (!RequestBodyHelper.IsAdmin(ctx))
            return ($"未註冊 {exchange} 憑證:匯出只回你自己帳戶、不用共用/預設帳戶代查。", d);
        return (null, d);  // admin 無自有憑證 → env 預設(你的帳戶)
    }
}
