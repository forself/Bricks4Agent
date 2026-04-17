using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TradingWorker.Models;

namespace TradingWorker.Storage;

/// <summary>
/// SQLite 持久化 — 訂單、成交紀錄、帳戶快照。
/// </summary>
public class TradingDbStorage : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ILogger<TradingDbStorage> _logger;

    public TradingDbStorage(string dbPath, ILogger<TradingDbStorage> logger)
    {
        _logger = logger;
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
        _logger.LogInformation("TradingDbStorage opened: {Path}", dbPath);
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS orders (
                order_id     TEXT PRIMARY KEY,
                symbol       TEXT NOT NULL,
                exchange     TEXT NOT NULL,
                side         TEXT NOT NULL,
                order_type   TEXT NOT NULL DEFAULT 'market',
                quantity     REAL NOT NULL,
                limit_price  REAL,
                stop_price   REAL,
                time_in_force TEXT NOT NULL DEFAULT 'gtc',
                status       TEXT NOT NULL DEFAULT 'pending',
                filled_qty   REAL NOT NULL DEFAULT 0,
                filled_price REAL,
                external_id  TEXT,
                error        TEXT,
                created_at   TEXT NOT NULL,
                filled_at    TEXT,
                updated_at   TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_orders_symbol ON orders(symbol);
            CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status);

            CREATE TABLE IF NOT EXISTS trades (
                trade_id     TEXT PRIMARY KEY,
                order_id     TEXT NOT NULL,
                symbol       TEXT NOT NULL,
                exchange     TEXT NOT NULL,
                side         TEXT NOT NULL,
                quantity     REAL NOT NULL,
                price        REAL NOT NULL,
                fee          REAL,
                realized_pnl REAL,
                executed_at  TEXT NOT NULL,
                FOREIGN KEY (order_id) REFERENCES orders(order_id)
            );

            CREATE INDEX IF NOT EXISTS idx_trades_symbol ON trades(symbol);

            CREATE TABLE IF NOT EXISTS account_snapshots (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                exchange        TEXT NOT NULL,
                account_id      TEXT NOT NULL,
                cash            REAL NOT NULL,
                portfolio_value REAL NOT NULL,
                buying_power    REAL NOT NULL,
                day_pnl         REAL NOT NULL DEFAULT 0,
                total_pnl       REAL NOT NULL DEFAULT 0,
                currency        TEXT NOT NULL DEFAULT 'USD',
                is_paper        INTEGER NOT NULL DEFAULT 1,
                snapshot_at     TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Orders ──────────────────────────────────────────────────────

    public void SaveOrder(TradingOrder order)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO orders
                (order_id, symbol, exchange, side, order_type, quantity, limit_price, stop_price,
                 time_in_force, status, filled_qty, filled_price, external_id, error, created_at, filled_at, updated_at)
            VALUES
                ($orderId, $symbol, $exchange, $side, $orderType, $qty, $limitPrice, $stopPrice,
                 $tif, $status, $filledQty, $filledPrice, $externalId, $error, $createdAt, $filledAt, $updatedAt)
            """;
        cmd.Parameters.AddWithValue("$orderId",    order.OrderId);
        cmd.Parameters.AddWithValue("$symbol",     order.Symbol);
        cmd.Parameters.AddWithValue("$exchange",   order.Exchange);
        cmd.Parameters.AddWithValue("$side",       order.Side);
        cmd.Parameters.AddWithValue("$orderType",  order.OrderType);
        cmd.Parameters.AddWithValue("$qty",        (double)order.Quantity);
        cmd.Parameters.AddWithValue("$limitPrice", order.LimitPrice.HasValue ? (object)(double)order.LimitPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$stopPrice",  order.StopPrice.HasValue ? (object)(double)order.StopPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$tif",        order.TimeInForce);
        cmd.Parameters.AddWithValue("$status",     order.Status);
        cmd.Parameters.AddWithValue("$filledQty",  (double)order.FilledQty);
        cmd.Parameters.AddWithValue("$filledPrice", order.FilledPrice.HasValue ? (object)(double)order.FilledPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$externalId", (object?)order.ExternalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$error",      (object?)order.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt",  order.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$filledAt",   order.FilledAt.HasValue ? (object)order.FilledAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("$updatedAt",  order.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public TradingOrder? GetOrder(string orderId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM orders WHERE order_id = $id";
        cmd.Parameters.AddWithValue("$id", orderId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadOrder(r) : null;
    }

    public List<TradingOrder> GetOrders(string? symbol = null, string? status = null, int limit = 50)
    {
        using var cmd = _conn.CreateCommand();
        var where = new List<string>();
        if (symbol != null) { where.Add("symbol = $symbol"); cmd.Parameters.AddWithValue("$symbol", symbol); }
        if (status != null) { where.Add("status = $status"); cmd.Parameters.AddWithValue("$status", status); }

        var clause = where.Count > 0 ? $"WHERE {string.Join(" AND ", where)}" : "";
        cmd.CommandText = $"SELECT * FROM orders {clause} ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<TradingOrder>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadOrder(r));
        return list;
    }

    // ── Trades ──────────────────────────────────────────────────────

    public void SaveTrade(TradeRecord trade)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO trades
                (trade_id, order_id, symbol, exchange, side, quantity, price, fee, realized_pnl, executed_at)
            VALUES
                ($tradeId, $orderId, $symbol, $exchange, $side, $qty, $price, $fee, $pnl, $executedAt)
            """;
        cmd.Parameters.AddWithValue("$tradeId",    trade.TradeId);
        cmd.Parameters.AddWithValue("$orderId",    trade.OrderId);
        cmd.Parameters.AddWithValue("$symbol",     trade.Symbol);
        cmd.Parameters.AddWithValue("$exchange",   trade.Exchange);
        cmd.Parameters.AddWithValue("$side",       trade.Side);
        cmd.Parameters.AddWithValue("$qty",        (double)trade.Quantity);
        cmd.Parameters.AddWithValue("$price",      (double)trade.Price);
        cmd.Parameters.AddWithValue("$fee",        trade.Fee.HasValue ? (object)(double)trade.Fee.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$pnl",        trade.RealizedPnl.HasValue ? (object)(double)trade.RealizedPnl.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$executedAt", trade.ExecutedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<TradeRecord> GetTrades(string? symbol = null, int limit = 100)
    {
        using var cmd = _conn.CreateCommand();
        var clause = symbol != null ? "WHERE symbol = $symbol" : "";
        if (symbol != null) cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.CommandText = $"SELECT * FROM trades {clause} ORDER BY executed_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<TradeRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TradeRecord
            {
                TradeId     = r.GetString(0),
                OrderId     = r.GetString(1),
                Symbol      = r.GetString(2),
                Exchange    = r.GetString(3),
                Side        = r.GetString(4),
                Quantity    = (decimal)r.GetDouble(5),
                Price       = (decimal)r.GetDouble(6),
                Fee         = r.IsDBNull(7) ? null : (decimal)r.GetDouble(7),
                RealizedPnl = r.IsDBNull(8) ? null : (decimal)r.GetDouble(8),
                ExecutedAt  = DateTime.Parse(r.GetString(9)),
            });
        }
        return list;
    }

    // ── Account Snapshots ───────────────────────────────────────────

    public void SaveAccountSnapshot(TradingAccount account)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO account_snapshots
                (exchange, account_id, cash, portfolio_value, buying_power, day_pnl, total_pnl, currency, is_paper, snapshot_at)
            VALUES
                ($exchange, $accountId, $cash, $pv, $bp, $dayPnl, $totalPnl, $currency, $isPaper, $snapshotAt)
            """;
        cmd.Parameters.AddWithValue("$exchange",   account.Exchange);
        cmd.Parameters.AddWithValue("$accountId",  account.AccountId);
        cmd.Parameters.AddWithValue("$cash",       (double)account.Cash);
        cmd.Parameters.AddWithValue("$pv",         (double)account.PortfolioValue);
        cmd.Parameters.AddWithValue("$bp",         (double)account.BuyingPower);
        cmd.Parameters.AddWithValue("$dayPnl",     (double)account.DayPnl);
        cmd.Parameters.AddWithValue("$totalPnl",   (double)account.TotalPnl);
        cmd.Parameters.AddWithValue("$currency",   account.Currency);
        cmd.Parameters.AddWithValue("$isPaper",    account.IsPaper ? 1 : 0);
        cmd.Parameters.AddWithValue("$snapshotAt", account.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static TradingOrder ReadOrder(SqliteDataReader r) => new()
    {
        OrderId     = r.GetString(r.GetOrdinal("order_id")),
        Symbol      = r.GetString(r.GetOrdinal("symbol")),
        Exchange    = r.GetString(r.GetOrdinal("exchange")),
        Side        = r.GetString(r.GetOrdinal("side")),
        OrderType   = r.GetString(r.GetOrdinal("order_type")),
        Quantity    = (decimal)r.GetDouble(r.GetOrdinal("quantity")),
        LimitPrice  = r.IsDBNull(r.GetOrdinal("limit_price")) ? null : (decimal)r.GetDouble(r.GetOrdinal("limit_price")),
        StopPrice   = r.IsDBNull(r.GetOrdinal("stop_price")) ? null : (decimal)r.GetDouble(r.GetOrdinal("stop_price")),
        TimeInForce = r.GetString(r.GetOrdinal("time_in_force")),
        Status      = r.GetString(r.GetOrdinal("status")),
        FilledQty   = (decimal)r.GetDouble(r.GetOrdinal("filled_qty")),
        FilledPrice = r.IsDBNull(r.GetOrdinal("filled_price")) ? null : (decimal)r.GetDouble(r.GetOrdinal("filled_price")),
        ExternalId  = r.IsDBNull(r.GetOrdinal("external_id")) ? null : r.GetString(r.GetOrdinal("external_id")),
        Error       = r.IsDBNull(r.GetOrdinal("error")) ? null : r.GetString(r.GetOrdinal("error")),
        CreatedAt   = DateTime.Parse(r.GetString(r.GetOrdinal("created_at"))),
        FilledAt    = r.IsDBNull(r.GetOrdinal("filled_at")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("filled_at"))),
        UpdatedAt   = DateTime.Parse(r.GetString(r.GetOrdinal("updated_at"))),
    };

    public void Dispose()
    {
        _conn.Close();
        _conn.Dispose();
    }
}
