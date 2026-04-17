using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingWorker.Models;

namespace TradingWorker.Exchange;

/// <summary>
/// Binance Spot Trading API 客戶端（加密貨幣）。
/// 支援 testnet（模擬）和 live trading。
///
/// 需要設定：
/// - ApiKey / ApiSecret（從 https://www.binance.com/en/my/settings/api-management 取得）
/// - IsTestnet：true = testnet.binance.vision, false = api.binance.com
/// </summary>
public class BinanceClient : IExchangeClient
{
    private readonly HttpClient _http;
    private readonly ILogger<BinanceClient> _logger;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _baseUrl;

    public string ExchangeName => "binance";

    public BinanceClient(
        HttpClient http,
        ILogger<BinanceClient> logger,
        string apiKey,
        string apiSecret,
        bool isTestnet = true)
    {
        _http      = http;
        _logger    = logger;
        _apiKey    = apiKey;
        _apiSecret = apiSecret;
        _baseUrl   = isTestnet
            ? "https://testnet.binance.vision"
            : "https://api.binance.com";

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("X-MBX-APIKEY", apiKey);
    }

    // ── Account ─────────────────────────────────────────────────────

    public async Task<TradingAccount> GetAccountAsync(CancellationToken ct = default)
    {
        var json = await SignedGetAsync("/api/v3/account", "", ct);
        var root = JsonDocument.Parse(json).RootElement;

        decimal totalUsd = 0;
        var balances = root.GetProperty("balances");
        foreach (var b in balances.EnumerateArray())
        {
            var free   = decimal.Parse(b.GetProperty("free").GetString()!);
            var locked = decimal.Parse(b.GetProperty("locked").GetString()!);
            var asset  = b.GetProperty("asset").GetString();
            if (asset == "USDT" || asset == "BUSD" || asset == "USD")
                totalUsd += free + locked;
        }

        return new TradingAccount
        {
            Exchange       = ExchangeName,
            AccountId      = "binance-spot",
            Cash           = totalUsd,
            PortfolioValue = totalUsd, // 簡化：完整版需要取所有幣的 USD 估值
            BuyingPower    = totalUsd,
            Currency       = "USDT",
            IsPaper        = _baseUrl.Contains("testnet"),
            UpdatedAt      = DateTime.UtcNow,
        };
    }

    // ── Positions ───────────────────────────────────────────────────

    public async Task<List<Position>> GetPositionsAsync(CancellationToken ct = default)
    {
        var json = await SignedGetAsync("/api/v3/account", "", ct);
        var root = JsonDocument.Parse(json).RootElement;

        var list = new List<Position>();
        foreach (var b in root.GetProperty("balances").EnumerateArray())
        {
            var free   = decimal.Parse(b.GetProperty("free").GetString()!);
            var locked = decimal.Parse(b.GetProperty("locked").GetString()!);
            var total  = free + locked;
            var asset  = b.GetProperty("asset").GetString() ?? "";

            if (total <= 0 || asset == "USDT" || asset == "BUSD" || asset == "USD")
                continue;

            list.Add(new Position
            {
                Symbol        = asset,
                Exchange      = ExchangeName,
                Quantity      = total,
                AvgEntryPrice = 0, // Binance spot 不提供成本基準
                Side          = "long",
                UpdatedAt     = DateTime.UtcNow,
            });
        }
        return list;
    }

    // ── Place Order ─────────────────────────────────────────────────

    public async Task<TradingOrder> PlaceOrderAsync(TradingOrder order, CancellationToken ct = default)
    {
        var ps = new List<string>
        {
            $"symbol={order.Symbol}",
            $"side={order.Side.ToUpper()}",
            $"type={MapOrderType(order.OrderType)}",
            $"quantity={order.Quantity:G}",
        };

        if (order.OrderType == "limit" || order.OrderType == "stop_limit")
        {
            ps.Add($"price={order.LimitPrice:G}");
            ps.Add($"timeInForce={order.TimeInForce.ToUpper()}");
        }
        if (order.OrderType == "stop" || order.OrderType == "stop_limit")
            ps.Add($"stopPrice={order.StopPrice:G}");

        var queryString = string.Join("&", ps);
        var json = await SignedPostAsync("/api/v3/order", queryString, ct);
        var root = JsonDocument.Parse(json).RootElement;

        order.ExternalId = root.GetProperty("orderId").GetInt64().ToString();
        order.Status     = MapBinanceStatus(root.GetProperty("status").GetString() ?? "");
        order.UpdatedAt  = DateTime.UtcNow;

        if (root.TryGetProperty("executedQty", out var eq))
            order.FilledQty = decimal.Parse(eq.GetString()!);
        if (root.TryGetProperty("cummulativeQuoteQty", out var cq) && order.FilledQty > 0)
            order.FilledPrice = decimal.Parse(cq.GetString()!) / order.FilledQty;

        _logger.LogInformation("Binance order placed: {Id} {Side} {Qty} {Symbol} → {Status}",
            order.ExternalId, order.Side, order.Quantity, order.Symbol, order.Status);

        return order;
    }

    // ── Cancel Order ────────────────────────────────────────────────

    public async Task<TradingOrder> CancelOrderAsync(string externalId, CancellationToken ct = default)
    {
        // Binance 取消需要 symbol — 用 orderId 查詢所有 open orders 來找
        var json = await SignedDeleteAsync($"/api/v3/order", $"orderId={externalId}", ct);
        var root = JsonDocument.Parse(json).RootElement;

        return new TradingOrder
        {
            ExternalId = externalId,
            Symbol     = root.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "",
            Exchange   = ExchangeName,
            Status     = "cancelled",
            UpdatedAt  = DateTime.UtcNow,
        };
    }

    // ── Order Status ────────────────────────────────────────────────

    public async Task<TradingOrder?> GetOrderStatusAsync(string externalId, CancellationToken ct = default)
    {
        // 需要 symbol，先查 open + 最近的 all orders
        // 簡化：用 openOrders 查
        try
        {
            var json = await SignedGetAsync("/api/v3/allOrders", $"orderId={externalId}&limit=1", ct);
            var arr = JsonDocument.Parse(json).RootElement;
            if (arr.GetArrayLength() == 0) return null;

            var el = arr[0];
            return new TradingOrder
            {
                ExternalId = externalId,
                Symbol     = el.GetProperty("symbol").GetString() ?? "",
                Exchange   = ExchangeName,
                Side       = el.GetProperty("side").GetString()?.ToLower() ?? "",
                OrderType  = el.GetProperty("type").GetString()?.ToLower() ?? "",
                Quantity   = decimal.Parse(el.GetProperty("origQty").GetString()!),
                Status     = MapBinanceStatus(el.GetProperty("status").GetString() ?? ""),
                FilledQty  = decimal.Parse(el.GetProperty("executedQty").GetString()!),
                UpdatedAt  = DateTime.UtcNow,
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Recent Trades ───────────────────────────────────────────────

    public async Task<List<TradeRecord>> GetRecentTradesAsync(string? symbol = null, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(symbol))
            return new List<TradeRecord>(); // Binance 需要指定 symbol

        var json = await SignedGetAsync("/api/v3/myTrades", $"symbol={symbol}&limit={limit}", ct);
        var arr = JsonDocument.Parse(json).RootElement;

        var list = new List<TradeRecord>();
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(new TradeRecord
            {
                TradeId    = el.GetProperty("id").GetInt64().ToString(),
                OrderId    = el.GetProperty("orderId").GetInt64().ToString(),
                Symbol     = symbol,
                Exchange   = ExchangeName,
                Side       = el.GetProperty("isBuyer").GetBoolean() ? "buy" : "sell",
                Quantity   = decimal.Parse(el.GetProperty("qty").GetString()!),
                Price      = decimal.Parse(el.GetProperty("price").GetString()!),
                Fee        = decimal.Parse(el.GetProperty("commission").GetString()!),
                ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(el.GetProperty("time").GetInt64()).UtcDateTime,
            });
        }
        return list;
    }

    // ── Signed request helpers ──────────────────────────────────────

    private string Sign(string queryString)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var qs = string.IsNullOrEmpty(queryString)
            ? $"timestamp={ts}"
            : $"{queryString}&timestamp={ts}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(qs));
        var signature = Convert.ToHexString(hash).ToLower();
        return $"{qs}&signature={signature}";
    }

    private async Task<string> SignedGetAsync(string path, string query, CancellationToken ct)
    {
        var signed = Sign(query);
        var resp = await _http.GetAsync($"{_baseUrl}{path}?{signed}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> SignedPostAsync(string path, string query, CancellationToken ct)
    {
        var signed = Sign(query);
        var content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");
        var resp = await _http.PostAsync($"{_baseUrl}{path}?{signed}", content, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> SignedDeleteAsync(string path, string query, CancellationToken ct)
    {
        var signed = Sign(query);
        var resp = await _http.DeleteAsync($"{_baseUrl}{path}?{signed}", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private static string MapOrderType(string t) => t switch
    {
        "market"     => "MARKET",
        "limit"      => "LIMIT",
        "stop"       => "STOP_LOSS",
        "stop_limit" => "STOP_LOSS_LIMIT",
        _ => t.ToUpper()
    };

    private static string MapBinanceStatus(string s) => s switch
    {
        "NEW" or "PARTIALLY_FILLED" => s == "PARTIALLY_FILLED" ? "partial" : "submitted",
        "FILLED"                     => "filled",
        "CANCELED" or "EXPIRED" or "REJECTED" => s == "REJECTED" ? "rejected" : "cancelled",
        _ => s.ToLower()
    };
}
