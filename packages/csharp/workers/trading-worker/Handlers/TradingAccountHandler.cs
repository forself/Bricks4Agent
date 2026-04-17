using System.Text.Json;
using WorkerSdk;
using TradingWorker.Exchange;
using TradingWorker.Storage;

namespace TradingWorker.Handlers;

/// <summary>
/// trading.account — 查詢帳戶餘額、持倉、成交紀錄。
///
/// Routes:
///   get_account    — 帳戶摘要（參數：exchange）
///   get_positions  — 持倉列表（參數：exchange）
///   get_trades     — 成交紀錄（參數：exchange, symbol, limit）
/// </summary>
public class TradingAccountHandler : ICapabilityHandler
{
    private readonly Dictionary<string, IExchangeClient> _clients;
    private readonly TradingDbStorage _db;
    public string CapabilityId => "trading.account";

    public TradingAccountHandler(Dictionary<string, IExchangeClient> clients, TradingDbStorage db)
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
            "get_account"   => await GetAccount(opts, ct),
            "get_positions" => await GetPositions(opts, ct),
            "get_trades"    => await GetTrades(opts, ct),
            "list_exchanges" => ListExchanges(),
            _ => (false, null, $"Unknown route: {route}")
        };
    }

    private async Task<(bool, string?, string?)> GetAccount(JsonElement opts, CancellationToken ct)
    {
        var exchange = opts.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "";
        if (!_clients.TryGetValue(exchange, out var client))
            return (false, null, $"Unknown exchange: {exchange}. Available: {string.Join(", ", _clients.Keys)}");

        try
        {
            var account = await client.GetAccountAsync(ct);
            _db.SaveAccountSnapshot(account);

            var json = JsonSerializer.Serialize(new
            {
                exchange = account.Exchange, account_id = account.AccountId,
                cash = account.Cash, portfolio_value = account.PortfolioValue,
                buying_power = account.BuyingPower, day_pnl = account.DayPnl,
                total_pnl = account.TotalPnl, currency = account.Currency,
                is_paper = account.IsPaper, updated_at = account.UpdatedAt,
            });
            return (true, json, null);
        }
        catch (Exception accEx)
        {
            return (false, null, $"Failed to get account: {accEx.Message}");
        }
    }

    private async Task<(bool, string?, string?)> GetPositions(JsonElement opts, CancellationToken ct)
    {
        var exchange = opts.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "";
        if (!_clients.TryGetValue(exchange, out var client))
            return (false, null, $"Unknown exchange: {exchange}");

        try
        {
            var positions = await client.GetPositionsAsync(ct);
            var json = JsonSerializer.Serialize(new
            {
                exchange,
                count = positions.Count,
                positions = positions.Select(p => new
                {
                    symbol = p.Symbol, quantity = p.Quantity, avg_entry_price = p.AvgEntryPrice,
                    current_price = p.CurrentPrice, market_value = p.MarketValue,
                    unrealized_pnl = p.UnrealizedPnl, unrealized_pnl_pct = p.UnrealizedPnlPercent,
                    side = p.Side,
                })
            });
            return (true, json, null);
        }
        catch (Exception posEx)
        {
            return (false, null, $"Failed to get positions: {posEx.Message}");
        }
    }

    private async Task<(bool, string?, string?)> GetTrades(JsonElement opts, CancellationToken ct)
    {
        var exchange = opts.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "" : "";
        var symbol   = opts.TryGetProperty("symbol",   out var s)  ? s.GetString()       : null;
        var limit    = opts.TryGetProperty("limit",    out var l)  ? l.GetInt32()         : 50;

        if (!_clients.TryGetValue(exchange, out var client))
            return (false, null, $"Unknown exchange: {exchange}");

        try
        {
            var trades = await client.GetRecentTradesAsync(symbol, limit, ct);
            foreach (var t in trades)
                _db.SaveTrade(t);

            var json = JsonSerializer.Serialize(new
            {
                exchange, count = trades.Count,
                trades = trades.Select(t => new
                {
                    trade_id = t.TradeId, order_id = t.OrderId, symbol = t.Symbol,
                    side = t.Side, quantity = t.Quantity, price = t.Price,
                    fee = t.Fee, realized_pnl = t.RealizedPnl, executed_at = t.ExecutedAt,
                })
            });
            return (true, json, null);
        }
        catch (Exception tradeEx)
        {
            return (false, null, $"Failed to get trades: {tradeEx.Message}");
        }
    }

    private (bool, string?, string?) ListExchanges()
    {
        var json = JsonSerializer.Serialize(new
        {
            exchanges = _clients.Keys.ToList()
        });
        return (true, json, null);
    }
}
