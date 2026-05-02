using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingWorker.Models;

namespace TradingWorker.Exchange;

/// <summary>
/// Alpaca Trading API 客戶端（美股）。
/// 支援 paper trading 和 live trading。
///
/// 需要設定：
/// - ApiKey / ApiSecret（從 https://app.alpaca.markets 取得）
/// - IsPaper：true = paper-api.alpaca.markets, false = api.alpaca.markets
/// </summary>
public class AlpacaClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AlpacaClient> _logger;
    private readonly string _baseUrl;
    private readonly bool _isPaper;

    public string ExchangeName => "alpaca";

    public AlpacaClient(
        HttpClient http,
        ILogger<AlpacaClient> logger,
        string apiKey,
        string apiSecret,
        bool isPaper = true)
    {
        _http    = http;
        _logger  = logger;
        _isPaper = isPaper;
        _baseUrl = isPaper
            ? "https://paper-api.alpaca.markets"
            : "https://api.alpaca.markets";

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKey);
        _http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecret);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ── Account ─────────────────────────────────────────────────────

    public async Task<TradingAccount> GetAccountAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("/v2/account", ct);
        var root = JsonDocument.Parse(json).RootElement;

        return new TradingAccount
        {
            Exchange       = ExchangeName,
            AccountId      = root.GetProperty("id").GetString() ?? "",
            Cash           = decimal.Parse(root.GetProperty("cash").GetString()!),
            PortfolioValue = decimal.Parse(root.GetProperty("portfolio_value").GetString()!),
            BuyingPower    = decimal.Parse(root.GetProperty("buying_power").GetString()!),
            Currency       = root.GetProperty("currency").GetString() ?? "USD",
            IsPaper        = _isPaper,
            UpdatedAt      = DateTime.UtcNow,
        };
    }

    // ── Positions ───────────────────────────────────────────────────

    public async Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("/v2/positions", ct);
        var arr = JsonDocument.Parse(json).RootElement;

        var list = new List<Position>();
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new Position
            {
                Symbol          = el.GetProperty("symbol").GetString() ?? "",
                Exchange        = ExchangeName,
                Quantity        = decimal.Parse(el.GetProperty("qty").GetString()!),
                AvgEntryPrice   = decimal.Parse(el.GetProperty("avg_entry_price").GetString()!),
                CurrentPrice    = decimal.Parse(el.GetProperty("current_price").GetString()!),
                MarketValue     = decimal.Parse(el.GetProperty("market_value").GetString()!),
                UnrealizedPnl   = decimal.Parse(el.GetProperty("unrealized_pl").GetString()!),
                UnrealizedPnlPercent = decimal.Parse(el.GetProperty("unrealized_plpc").GetString()!),
                Side            = el.GetProperty("side").GetString() ?? "long",
                UpdatedAt       = DateTime.UtcNow,
            });
        }
        return list;
    }

    // ── Place Order ─────────────────────────────────────────────────

    public async Task<TradingOrder> PlaceOrderAsync(TradingOrder order, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["symbol"]        = order.Symbol,
            ["qty"]           = order.Quantity.ToString("G"),
            ["side"]          = order.Side,
            ["type"]          = order.OrderType,
            ["time_in_force"] = order.TimeInForce,
        };

        // Send our local OrderId as Alpaca's client_order_id — exchange-side dedup safety net.
        // 送同樣 client_order_id 第二次，Alpaca 回 422 + "client_order_id must be unique"，
        // handler 端的 DB 檢查理論上會先擋下重複，但這層是萬一 DB 漏網的最後一道。
        if (!string.IsNullOrWhiteSpace(order.OrderId))
            body["client_order_id"] = order.OrderId;

        if (order.LimitPrice.HasValue)
            body["limit_price"] = order.LimitPrice.Value.ToString("G");
        if (order.StopPrice.HasValue)
            body["stop_price"] = order.StopPrice.Value.ToString("G");

        var json = await PostAsync("/v2/orders", JsonSerializer.Serialize(body), ct);
        var root = JsonDocument.Parse(json).RootElement;

        order.ExternalId = root.GetProperty("id").GetString();
        order.Status     = MapAlpacaStatus(root.GetProperty("status").GetString() ?? "");
        order.UpdatedAt  = DateTime.UtcNow;

        if (root.TryGetProperty("filled_qty", out var fq) && fq.GetString() is string fqs)
            order.FilledQty = decimal.TryParse(fqs, out var fqv) ? fqv : 0;
        if (root.TryGetProperty("filled_avg_price", out var fp) && fp.GetString() is string fps)
            order.FilledPrice = decimal.TryParse(fps, out var fpv) ? fpv : null;

        _logger.LogInformation("Alpaca order placed: {Id} {Side} {Qty} {Symbol} → {Status}",
            order.ExternalId, order.Side, order.Quantity, order.Symbol, order.Status);

        return order;
    }

    // ── Cancel Order ────────────────────────────────────────────────

    public async Task<TradingOrder> CancelOrderAsync(string externalId, CancellationToken ct = default)
    {
        await DeleteAsync($"/v2/orders/{externalId}", ct);
        var updated = await GetOrderStatusAsync(externalId, ct);
        return updated ?? new TradingOrder { ExternalId = externalId, Status = "cancelled" };
    }

    // ── Order Status ────────────────────────────────────────────────

    public async Task<TradingOrder?> GetOrderStatusAsync(string externalId, CancellationToken ct = default)
    {
        var json = await GetAsync($"/v2/orders/{externalId}", ct);
        var root = JsonDocument.Parse(json).RootElement;

        return new TradingOrder
        {
            OrderId     = root.TryGetProperty("client_order_id", out var co) ? co.GetString() ?? "" : "",
            ExternalId  = root.GetProperty("id").GetString(),
            Symbol      = root.GetProperty("symbol").GetString() ?? "",
            Exchange    = ExchangeName,
            Side        = root.GetProperty("side").GetString() ?? "",
            OrderType   = root.GetProperty("type").GetString() ?? "",
            Quantity    = decimal.Parse(root.GetProperty("qty").GetString()!),
            Status      = MapAlpacaStatus(root.GetProperty("status").GetString() ?? ""),
            FilledQty   = decimal.TryParse(root.TryGetProperty("filled_qty", out var fq) ? fq.GetString() : "0", out var fqv) ? fqv : 0,
            FilledPrice = root.TryGetProperty("filled_avg_price", out var fp) && fp.GetString() is string fps && decimal.TryParse(fps, out var fpv) ? fpv : null,
            CreatedAt   = root.TryGetProperty("created_at", out var ca) ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow,
            FilledAt    = root.TryGetProperty("filled_at", out var fa) && fa.ValueKind != JsonValueKind.Null ? DateTime.Parse(fa.GetString()!) : null,
            UpdatedAt   = DateTime.UtcNow,
        };
    }

    // ── Recent Trades (via filled orders) ────────────────────────────

    public async Task<List<TradeRecord>> GetRecentTradesAsync(string? symbol = null, int limit = 50, CancellationToken ct = default)
    {
        var url = $"/v2/orders?status=filled&limit={limit}&direction=desc";
        if (symbol != null) url += $"&symbols={symbol}";

        var json = await GetAsync(url, ct);
        var arr = JsonDocument.Parse(json).RootElement;

        var list = new List<TradeRecord>();
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new TradeRecord
            {
                TradeId    = el.GetProperty("id").GetString() ?? "",
                OrderId    = el.TryGetProperty("client_order_id", out var co) ? co.GetString() ?? "" : "",
                Symbol     = el.GetProperty("symbol").GetString() ?? "",
                Exchange   = ExchangeName,
                Side       = el.GetProperty("side").GetString() ?? "",
                Quantity   = decimal.TryParse(el.TryGetProperty("filled_qty", out var fq) ? fq.GetString() : "0", out var fqv) ? fqv : 0,
                Price      = decimal.TryParse(el.TryGetProperty("filled_avg_price", out var fp) ? fp.GetString() : "0", out var fpv) ? fpv : 0,
                ExecutedAt = el.TryGetProperty("filled_at", out var fa) && fa.ValueKind != JsonValueKind.Null ? DateTime.Parse(fa.GetString()!) : DateTime.UtcNow,
            });
        }
        return list;
    }

    // ── HTTP helpers ────────────────────────────────────────────────

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"{_baseUrl}{path}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> PostAsync(string path, string body, CancellationToken ct)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{_baseUrl}{path}", content, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        var resp = await _http.DeleteAsync($"{_baseUrl}{path}", ct);
        resp.EnsureSuccessStatusCode();
    }

    private static string MapAlpacaStatus(string s) => s switch
    {
        "new" or "accepted" or "pending_new" => "submitted",
        "filled"                              => "filled",
        "partially_filled"                    => "partial",
        "canceled" or "expired" or "replaced" => "cancelled",
        "rejected" or "stopped" or "suspended" => "rejected",
        _ => s
    };
}
