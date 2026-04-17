using System.Text.Json;
using WorkerSdk;
using TradingWorker.Exchange;
using TradingWorker.Models;
using TradingWorker.Storage;

namespace TradingWorker.Handlers;

/// <summary>
/// trading.order — 下單、取消、查詢訂單。
///
/// Routes:
///   place_order   — 下單（參數：exchange, symbol, side, quantity, order_type, limit_price, stop_price, time_in_force）
///   cancel_order  — 取消（參數：exchange, external_id）
///   get_order     — 查詢單一訂單（參數：order_id 或 external_id + exchange）
///   list_orders   — 列出訂單（參數：symbol, status, limit）
/// </summary>
public class TradingOrderHandler : ICapabilityHandler
{
    private readonly Dictionary<string, IExchangeClient> _clients;
    private readonly TradingDbStorage _db;
    public string CapabilityId => "trading.order";

    public TradingOrderHandler(Dictionary<string, IExchangeClient> clients, TradingDbStorage db)
    {
        _clients = clients;
        _db      = db;
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var opts = string.IsNullOrWhiteSpace(payload)
            ? new JsonElement()
            : JsonDocument.Parse(payload).RootElement;

        return route switch
        {
            "place_order"  => await PlaceOrder(opts, ct),
            "cancel_order" => await CancelOrder(opts, ct),
            "get_order"    => GetOrder(opts),
            "list_orders"  => ListOrders(opts),
            _ => (false, null, $"Unknown route: {route}")
        };
    }

    private async Task<(bool, string?, string?)> PlaceOrder(JsonElement opts, CancellationToken ct)
    {
        var exchange = opts.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "";
        if (!_clients.TryGetValue(exchange, out var client))
            return (false, null, $"Unknown exchange: {exchange}. Available: {string.Join(", ", _clients.Keys)}");

        var order = new TradingOrder
        {
            OrderId     = $"ord-{Guid.NewGuid():N}"[..16],
            Symbol      = opts.TryGetProperty("symbol",      out var s)   ? s.GetString() ?? ""       : "",
            Exchange    = exchange,
            Side        = opts.TryGetProperty("side",         out var sd)  ? sd.GetString() ?? "buy"   : "buy",
            OrderType   = opts.TryGetProperty("order_type",   out var ot)  ? ot.GetString() ?? "market" : "market",
            Quantity    = opts.TryGetProperty("quantity",      out var q)   ? q.GetDecimal()            : 0,
            LimitPrice  = opts.TryGetProperty("limit_price",  out var lp)  ? lp.GetDecimal()           : null,
            StopPrice   = opts.TryGetProperty("stop_price",   out var sp)  ? sp.GetDecimal()           : null,
            TimeInForce = opts.TryGetProperty("time_in_force", out var tf) ? tf.GetString() ?? "gtc"   : "gtc",
        };

        if (string.IsNullOrEmpty(order.Symbol))
            return (false, null, "Missing required parameter: symbol");
        if (order.Quantity <= 0)
            return (false, null, "Quantity must be > 0");

        try
        {
            var result = await client.PlaceOrderAsync(order, ct);
            _db.SaveOrder(result);
            return (true, SerializeOrder(result), null);
        }
        catch (Exception orderEx)
        {
            order.Status = "rejected";
            order.Error  = orderEx.Message;
            _db.SaveOrder(order);
            return (false, null, $"Order failed: {orderEx.Message}");
        }
    }

    private async Task<(bool, string?, string?)> CancelOrder(JsonElement opts, CancellationToken ct)
    {
        var exchange   = opts.TryGetProperty("exchange",    out var ex) ? ex.GetString() ?? "" : "";
        var externalId = opts.TryGetProperty("external_id", out var ei) ? ei.GetString() ?? "" : "";

        if (!_clients.TryGetValue(exchange, out var client))
            return (false, null, $"Unknown exchange: {exchange}");
        if (string.IsNullOrEmpty(externalId))
            return (false, null, "Missing required parameter: external_id");

        try
        {
            var result = await client.CancelOrderAsync(externalId, ct);
            _db.SaveOrder(result);
            return (true, SerializeOrder(result), null);
        }
        catch (Exception cancelEx)
        {
            return (false, null, $"Cancel failed: {cancelEx.Message}");
        }
    }

    private (bool, string?, string?) GetOrder(JsonElement opts)
    {
        var orderId = opts.TryGetProperty("order_id", out var oi) ? oi.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(orderId))
            return (false, null, "Missing required parameter: order_id");

        var order = _db.GetOrder(orderId);
        if (order == null) return (false, null, $"Order not found: {orderId}");
        return (true, SerializeOrder(order), null);
    }

    private (bool, string?, string?) ListOrders(JsonElement opts)
    {
        var symbol = opts.TryGetProperty("symbol", out var s)  ? s.GetString()  : null;
        var status = opts.TryGetProperty("status", out var st) ? st.GetString() : null;
        var limit  = opts.TryGetProperty("limit",  out var l)  ? l.GetInt32()   : 50;

        var orders = _db.GetOrders(symbol, status, limit);
        var json = JsonSerializer.Serialize(new
        {
            count = orders.Count,
            orders = orders.Select(o => new
            {
                order_id = o.OrderId, external_id = o.ExternalId, symbol = o.Symbol,
                exchange = o.Exchange, side = o.Side, order_type = o.OrderType,
                quantity = o.Quantity, limit_price = o.LimitPrice, stop_price = o.StopPrice,
                status = o.Status, filled_qty = o.FilledQty, filled_price = o.FilledPrice,
                error = o.Error, created_at = o.CreatedAt, filled_at = o.FilledAt,
            })
        });
        return (true, json, null);
    }

    private static string SerializeOrder(TradingOrder o) => JsonSerializer.Serialize(new
    {
        order_id = o.OrderId, external_id = o.ExternalId, symbol = o.Symbol,
        exchange = o.Exchange, side = o.Side, order_type = o.OrderType,
        quantity = o.Quantity, limit_price = o.LimitPrice, stop_price = o.StopPrice,
        status = o.Status, filled_qty = o.FilledQty, filled_price = o.FilledPrice,
        error = o.Error, created_at = o.CreatedAt, filled_at = o.FilledAt,
    });
}
