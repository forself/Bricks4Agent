using System.Text.Json;
using TradingWorker.Exchange;
using TradingWorker.Models;
using TradingWorker.Storage;
using WorkerSdk;

namespace TradingWorker.Handlers;

/// <summary>
/// trading.perpetual capability — 統一處理永續合約相關呼叫。
/// 跟 spot 的 trading.account / trading.order 故意分開、避免兩種帳戶資料互相污染。
///
/// Routes:
///   - get_account     -> exchange's perpetual balance + margin + unrealized
///   - get_positions   -> open positions（雙向、含 leverage / liq price）
///   - place_order     -> 下單（open/close long/short）
///   - cancel_order    -> 取消未成交
///   - get_order       -> 查詢單筆訂單
///   - get_open_orders -> 列出 open orders
///   - set_leverage    -> 設定該 symbol 該方向槓桿
///   - get_mark_price  -> 取 mark price（不需簽名、給 dashboard 算 PnL 用）
///   - list_exchanges  -> 列出已啟用的 perpetual exchanges
/// </summary>
public class TradingPerpetualHandler : ICapabilityHandler
{
    private readonly Dictionary<string, IPerpetualClient> _clients;
    private readonly TradingDbStorage? _db;
    private readonly Microsoft.Extensions.Logging.ILoggerFactory? _loggerFactory;

    // ── ad-hoc per-user client cache（A2.5b 2026-05-10）──
    // 用 (exchange, sha256(apiKey)[:16], isDemo) 當 key、避免每筆請求 rebuild HttpClient + BingxClient。
    // 沒有 TTL——容器活著就快取在；容器重啟自動清。同 user 兩支 key 切換時各自一個 entry、互不干擾。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IPerpetualClient> _adhocClients = new();

    public string CapabilityId => "trading.perpetual";

    public TradingPerpetualHandler(Dictionary<string, IPerpetualClient> clients,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null,
        TradingDbStorage? db = null)
    {
        _clients = clients;
        _loggerFactory = loggerFactory;
        _db = db;
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var opts = string.IsNullOrWhiteSpace(payload)
            ? new JsonElement()
            : JsonDocument.Parse(payload).RootElement;

        return route switch
        {
            "get_account"      => await GetAccount(opts, ct),
            "get_positions"    => await GetPositions(opts, ct),
            "place_order"      => await PlaceOrder(opts, ct),
            "cancel_order"     => await CancelOrder(opts, ct),
            "get_order"        => await GetOrder(opts, ct),
            "get_open_orders"  => await GetOpenOrders(opts, ct),
            "set_leverage"     => await SetLeverage(opts, ct),
            "get_mark_price"   => await GetMarkPrice(opts, ct),
            "get_tickers_24h"  => await GetTickers24h(opts, ct),
            "list_exchanges"   => ListExchanges(),
            _ => (false, null, $"Unknown route: {route}")
        };
    }

    private async Task<(bool, string?, string?)> GetTickers24h(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        try
        {
            var list = await c!.GetTickers24hAsync(ct);
            var json = JsonSerializer.Serialize(new
            {
                count = list.Count,
                snapshot_at = list.FirstOrDefault()?.SnapshotAt ?? DateTime.UtcNow,
                tickers = list.Select(t => new
                {
                    symbol = t.Symbol,
                    last_price = t.LastPrice,
                    high = t.HighPrice,
                    low = t.LowPrice,
                    open = t.OpenPrice,
                    volume = t.Volume,
                    quote_volume = t.QuoteVolume,
                    price_change = t.PriceChange,
                    price_change_pct = t.PriceChangePercent,
                })
            });
            return (true, json, null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private (bool, string?, string?) ListExchanges()
    {
        var exchanges = _clients.Select(kv => new
        {
            exchange = kv.Key,
            is_demo  = kv.Value.IsDemo,
        }).ToList();
        return (true, JsonSerializer.Serialize(new { exchanges }), null);
    }

    private async Task<(bool, string?, string?)> GetAccount(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        try
        {
            var acc = await c!.GetAccountAsync(ct);
            var json = JsonSerializer.Serialize(new
            {
                exchange = acc.Exchange,
                account_id = acc.AccountId,
                currency = acc.Currency,
                balance = acc.Balance,
                equity = acc.Equity,
                unrealized_pnl = acc.UnrealizedPnl,
                margin_used = acc.MarginUsed,
                available_margin = acc.AvailableMargin,
                open_positions_count = acc.OpenPositionsCount,
                is_demo = acc.IsDemo,
                updated_at = acc.UpdatedAt,
            });
            return (true, json, null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<(bool, string?, string?)> GetPositions(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        try
        {
            var list = await c!.GetPositionsAsync(ct);
            var json = JsonSerializer.Serialize(new
            {
                positions = list.Select(p => new
                {
                    symbol = p.Symbol, exchange = p.Exchange, side = p.Side,
                    quantity = p.Quantity, avg_entry_price = p.AvgEntryPrice,
                    mark_price = p.MarkPrice, unrealized_pnl = p.UnrealizedPnl,
                    unrealized_pnl_pct = p.UnrealizedPnlPercent,
                    leverage = p.Leverage, margin_mode = p.MarginMode, margin_used = p.MarginUsed,
                    liquidation_price = p.LiquidationPrice,
                    liquidation_distance_pct = p.LiquidationDistancePct,
                    updated_at = p.UpdatedAt,
                })
            });
            return (true, json, null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<(bool, string?, string?)> PlaceOrder(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);

        var symbol       = opts.TryGetProperty("symbol", out var s)       ? s.GetString() ?? "" : "";
        var side         = opts.TryGetProperty("side", out var sd)        ? sd.GetString() ?? "" : "";
        var positionSide = opts.TryGetProperty("position_side", out var ps) ? ps.GetString() ?? "" : "";
        var orderType    = opts.TryGetProperty("order_type", out var ot)  ? ot.GetString() ?? "market" : "market";
        var qty          = opts.TryGetProperty("quantity", out var q)     ? q.GetDecimal() : 0m;
        var leverage     = opts.TryGetProperty("leverage", out var lv) && lv.TryGetInt32(out var lvI) ? lvI : 1;
        var reduceOnly   = opts.TryGetProperty("reduce_only", out var ro) && ro.GetBoolean();
        decimal? limitPrice = opts.TryGetProperty("limit_price", out var lp) && lp.ValueKind == JsonValueKind.Number ? lp.GetDecimal() : null;
        decimal? stopPrice  = opts.TryGetProperty("stop_price", out var sp) && sp.ValueKind == JsonValueKind.Number ? sp.GetDecimal() : null;

        if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(side) || string.IsNullOrEmpty(positionSide) || qty <= 0m)
            return (false, null, "missing required: symbol/side/position_side/quantity");

        try
        {
            // 開倉前先設 leverage（BingX 是 sticky 的、設一次就生效；但 idempotent、再設沒副作用）
            if (!reduceOnly && leverage > 1)
                await c!.SetLeverageAsync(symbol, positionSide, leverage, ct);

            var order = new PerpetualOrder
            {
                OrderId = $"perp-{Guid.NewGuid():N}"[..16],
                Symbol = symbol, Exchange = c!.ExchangeName, Side = side, PositionSide = positionSide,
                OrderType = orderType, Quantity = qty,
                LimitPrice = limitPrice, StopPrice = stopPrice,
                Leverage = leverage, ReduceOnly = reduceOnly,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };

            var result = await c.PlaceOrderAsync(order, ct);

            // Persist 永續成交到 local DB（給 get_trade_history / pnl-summary 用）。
            // BingX market order 通常即時成交、result 會帶 FilledQty + FilledPrice。
            // 開倉只有手續費沒 realized_pnl；reduce_only 平倉才有 realized_pnl。
            if (_db != null && result.Status == "filled" && result.FilledQty > 0 && result.FilledPrice.HasValue)
            {
                try
                {
                    _db.SaveTrade(new TradeRecord
                    {
                        TradeId    = $"perp-{result.ExternalId}",
                        OrderId    = result.OrderId,
                        Symbol     = result.Symbol,
                        Exchange   = result.Exchange,
                        Side       = result.Side,
                        Quantity   = result.FilledQty,
                        Price      = result.FilledPrice.Value,
                        Fee        = null,                 // BingX 預設不在 place_order response 帶 fee
                        RealizedPnl = reduceOnly ? null : null,  // realized PnL 通常要從 /user/positions/closeAll 或單 fill detail 拿、不在 place 回傳
                        ExecutedAt = result.FilledAt ?? DateTime.UtcNow,
                    });
                }
                catch { /* persist 失敗別影響 caller */ }
            }

            var json = JsonSerializer.Serialize(SerializeOrder(result));
            return (true, json, null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<(bool, string?, string?)> CancelOrder(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        var symbol = opts.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        var orderId = opts.TryGetProperty("order_id", out var oid) ? oid.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(orderId))
            return (false, null, "missing required: symbol/order_id");
        try
        {
            var r = await c!.CancelOrderAsync(symbol, orderId, ct);
            return (true, JsonSerializer.Serialize(SerializeOrder(r)), null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<(bool, string?, string?)> GetOrder(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        var symbol = opts.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        var orderId = opts.TryGetProperty("order_id", out var oid) ? oid.GetString() ?? "" : "";
        try
        {
            var r = await c!.GetOrderStatusAsync(symbol, orderId, ct);
            if (r == null) return (false, null, "order not found");
            return (true, JsonSerializer.Serialize(SerializeOrder(r)), null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<(bool, string?, string?)> GetOpenOrders(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        var symbol = opts.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        try
        {
            var list = await c!.GetOpenOrdersAsync(symbol, ct);
            return (true, JsonSerializer.Serialize(new { orders = list.Select(SerializeOrder) }), null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private async Task<(bool, string?, string?)> SetLeverage(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        var symbol = opts.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        var positionSide = opts.TryGetProperty("position_side", out var ps) ? ps.GetString() ?? "long" : "long";
        var leverage = opts.TryGetProperty("leverage", out var lv) && lv.TryGetInt32(out var i) ? i : 1;
        if (string.IsNullOrEmpty(symbol) || leverage < 1) return (false, null, "missing/invalid: symbol/leverage");
        var ok = await c!.SetLeverageAsync(symbol, positionSide, leverage, ct);
        return (true, JsonSerializer.Serialize(new { symbol, position_side = positionSide, leverage, ok }), null);
    }

    private async Task<(bool, string?, string?)> GetMarkPrice(JsonElement opts, CancellationToken ct)
    {
        if (!TryGetClient(opts, out var c, out var err)) return (false, null, err);
        var symbol = opts.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(symbol)) return (false, null, "missing: symbol");
        try
        {
            var price = await c!.GetMarkPriceAsync(symbol, ct);
            return (true, JsonSerializer.Serialize(new { symbol, mark_price = price }), null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    private bool TryGetClient(JsonElement opts, out IPerpetualClient? c, out string? err)
    {
        var exchange = opts.TryGetProperty("exchange", out var e) ? e.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(exchange)) exchange = _clients.Keys.FirstOrDefault() ?? "";

        // A2.5b：payload 帶 __credentials 就用 user 自己的 key 建 ad-hoc client；不帶就 fallback env-config 預設。
        if (opts.TryGetProperty("__credentials", out var creds) && creds.ValueKind == JsonValueKind.Object)
        {
            var apiKey = creds.TryGetProperty("api_key", out var k) ? k.GetString() ?? "" : "";
            var apiSecret = creds.TryGetProperty("api_secret", out var s) ? s.GetString() ?? "" : "";
            var isDemo = creds.TryGetProperty("is_demo", out var d) && d.GetBoolean();
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
            {
                c = ResolveAdhocClient(exchange, apiKey, apiSecret, isDemo);
                err = null;
                return c != null || (err = $"no ad-hoc support for exchange '{exchange}'") == null;
            }
        }

        if (!_clients.TryGetValue(exchange, out var found))
        {
            c = null; err = $"perpetual exchange not configured: {exchange}";
            return false;
        }
        c = found; err = null;
        return true;
    }

    private IPerpetualClient? ResolveAdhocClient(string exchange, string apiKey, string apiSecret, bool isDemo)
    {
        // 不洩漏 api_key 完整值給 cache key——hash 一次取前 16 byte hex 就夠抗碰撞
        using var sha = System.Security.Cryptography.SHA256.Create();
        var keyHash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(apiKey)))[..16].ToLowerInvariant();
        var cacheKey = $"{exchange.ToLowerInvariant()}:{keyHash}:{isDemo}";

        return _adhocClients.GetOrAdd(cacheKey, _ => BuildAdhocClient(exchange, apiKey, apiSecret, isDemo))!;
    }

    private IPerpetualClient? BuildAdhocClient(string exchange, string apiKey, string apiSecret, bool isDemo)
    {
        var ex = exchange.ToLowerInvariant();
        if (ex == "bingx")
        {
            // 重新 new 一個 HttpClient — BingxPerpetualClient 自己持有、不跟其他 client 共享 connection pool
            // 是必要的（不然 cancel 一個會牽連到別人）
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var logger = _loggerFactory != null
                ? Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<TradingWorker.Exchange.BingxPerpetualClient>(_loggerFactory)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingWorker.Exchange.BingxPerpetualClient>.Instance;
            return new TradingWorker.Exchange.BingxPerpetualClient(http, logger, apiKey, apiSecret, isDemo);
        }
        // binance / alpaca: 之後實作（Phase A2.5c 之類）
        return null;
    }

    private static object SerializeOrder(PerpetualOrder o) => new
    {
        order_id = o.OrderId, symbol = o.Symbol, exchange = o.Exchange,
        side = o.Side, position_side = o.PositionSide,
        order_type = o.OrderType, quantity = o.Quantity,
        limit_price = o.LimitPrice, stop_price = o.StopPrice,
        leverage = o.Leverage, reduce_only = o.ReduceOnly,
        status = o.Status, filled_qty = o.FilledQty, filled_price = o.FilledPrice,
        external_id = o.ExternalId, error = o.Error,
        created_at = o.CreatedAt, filled_at = o.FilledAt, updated_at = o.UpdatedAt,
    };
}
