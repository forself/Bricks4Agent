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
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Text("trading-worker not connected", "text/plain", statusCode: 503);

            var payload = JsonSerializer.Serialize(new { limit = 500 });
            var result = await dispatcher.DispatchAsync(BuildRequest("trading.order", "list_orders", payload));
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
            HttpRequest req, CancellationToken ct) =>
        {
            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Text("trading-worker not connected", "text/plain", statusCode: 503);

            var payload = JsonSerializer.Serialize(new { exchange, limit = 500 });
            var result = await dispatcher.DispatchAsync(BuildRequest("trading.account", "get_trades", payload));
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

    private static ApprovedRequest BuildRequest(string cap, string route, string payload = "{}") => new()
    {
        RequestId = Guid.NewGuid().ToString("N"), CapabilityId = cap, Route = route,
        Payload = payload, Scope = "{}", PrincipalId = "system", TaskId = "export", SessionId = "export"
    };
}
