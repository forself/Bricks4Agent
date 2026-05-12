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
                strategy     TEXT,
                FOREIGN KEY (order_id) REFERENCES orders(order_id)
            );

            CREATE INDEX IF NOT EXISTS idx_trades_symbol ON trades(symbol);
            CREATE INDEX IF NOT EXISTS idx_trades_exchange_executed ON trades(exchange, executed_at);

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

        // 舊 DB 增量遷移：trades.strategy 欄位（idempotent、欄位已存在會吃掉例外）
        TryAddColumn("trades", "strategy", "TEXT");
    }

    private void TryAddColumn(string table, string column, string typeDecl)
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {typeDecl}";
            cmd.ExecuteNonQuery();
            _logger.LogInformation("Migrated: ALTER TABLE {Table} ADD COLUMN {Col}", table, column);
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // 欄位已存在、不是錯
        }
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

    /// <summary>
    /// 取仍可能成交的訂單（給 fill poller 用）。terminal status (filled/cancelled/rejected) 跳過。
    /// </summary>
    public List<TradingOrder> GetOpenOrders()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM orders
            WHERE status IN ('pending', 'submitted', 'partial')
              AND external_id IS NOT NULL
            ORDER BY created_at
            """;

        var list = new List<TradingOrder>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadOrder(r));
        return list;
    }

    // ── Trades ──────────────────────────────────────────────────────

    public void SaveTrade(TradeRecord trade)
    {
        // Best-effort：strategy 沒填 + 是 perp-income 補抓的 row，從同 (exchange, symbol) 最近一筆有
        // strategy 的紀錄繼承過來，讓策略績效歸屬有依據。手動下單沒給也保持 null。
        var strategy = trade.Strategy;
        if (string.IsNullOrEmpty(strategy) && trade.TradeId.StartsWith("perp-income-", StringComparison.Ordinal))
        {
            strategy = InferStrategy(trade.Exchange, trade.Symbol, trade.ExecutedAt);
        }

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO trades
                (trade_id, order_id, symbol, exchange, side, quantity, price, fee, realized_pnl, executed_at, strategy)
            VALUES
                ($tradeId, $orderId, $symbol, $exchange, $side, $qty, $price, $fee, $pnl, $executedAt, $strategy)
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
        cmd.Parameters.AddWithValue("$strategy",   string.IsNullOrEmpty(strategy) ? DBNull.Value : (object)strategy);
        cmd.ExecuteNonQuery();
    }

    private string? InferStrategy(string exchange, string symbol, DateTime beforeUtc)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT strategy FROM trades
            WHERE exchange = $exchange AND symbol = $symbol AND strategy IS NOT NULL
              AND executed_at <= $before
            ORDER BY executed_at DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$exchange", exchange);
        cmd.Parameters.AddWithValue("$symbol", symbol);
        cmd.Parameters.AddWithValue("$before", beforeUtc.ToString("o"));
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : v.ToString();
    }

    /// <summary>
    /// 取某交易所最後一筆 perp-income (FillPoller backfill) 寫入時間。
    /// 用來在啟動時把 income poll 的 cursor 推到正確位置、避免重撈 / 漏抓。
    /// 沒有任何紀錄就回 null、caller fallback 預設 lookback 視窗。
    /// </summary>
    public DateTime? GetLatestPerpIncomeTime(string exchange)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT MAX(executed_at) FROM trades
            WHERE exchange = $exchange AND trade_id LIKE 'perp-income-%'
            """;
        cmd.Parameters.AddWithValue("$exchange", exchange);
        var v = cmd.ExecuteScalar();
        if (v == null || v == DBNull.Value) return null;
        var s = v.ToString();
        if (string.IsNullOrEmpty(s)) return null;
        return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    /// <summary>
    /// 每個 (exchange:symbol) 最近一次成交時間。給 risk engine 的 cooldown_seconds 規則用——
    /// 防同 symbol 在短期內連續被觸發 signal（signal 抖動）造成多次下單。
    /// 回傳 key 格式："{exchange}:{symbol}"，跟 PortfolioSnapshot.LastTradeBySymbol 對齊。
    /// </summary>
    public Dictionary<string, DateTime> GetLastTradeTimePerSymbol()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT exchange, symbol, MAX(executed_at) AS last_at
            FROM trades
            GROUP BY exchange, symbol
            """;
        var dict = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var key = $"{r.GetString(0)}:{r.GetString(1)}";
            // RoundtripKind 保留 ISO 8601 的 Z 後綴 → DateTime.Kind=Utc，避免被當成 local time 偏移
            dict[key] = DateTime.Parse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
        return dict;
    }

    /// <summary>
    /// 從指定 UTC 時間以後的成交筆數（給 risk engine 的 max_daily_trades 規則用）。
    /// 預設用 UTC 0 點當「今天」的起點，跨時區呼叫者可自己算 fromUtc。
    /// </summary>
    public int GetDailyTradeCount(DateTime fromUtc, string? exchange = null)
    {
        using var cmd = _conn.CreateCommand();
        var sql = "SELECT COUNT(*) FROM trades WHERE executed_at >= $from";
        if (exchange != null) { sql += " AND exchange = $exchange"; cmd.Parameters.AddWithValue("$exchange", exchange); }
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$from", fromUtc.ToString("o"));
        var raw = cmd.ExecuteScalar();
        return raw == null ? 0 : Convert.ToInt32(raw);
    }

    public List<TradeRecord> GetTrades(string? symbol = null, int limit = 100, string? exchange = null, DateTime? sinceUtc = null)
    {
        using var cmd = _conn.CreateCommand();
        var conditions = new List<string>();
        if (symbol   != null) { conditions.Add("symbol = $symbol");      cmd.Parameters.AddWithValue("$symbol", symbol); }
        if (exchange != null) { conditions.Add("exchange = $exchange");  cmd.Parameters.AddWithValue("$exchange", exchange); }
        if (sinceUtc.HasValue){ conditions.Add("executed_at >= $since"); cmd.Parameters.AddWithValue("$since", sinceUtc.Value.ToString("o")); }
        var clause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        cmd.CommandText = $"SELECT * FROM trades {clause} ORDER BY executed_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<TradeRecord>();
        using var r = cmd.ExecuteReader();
        // 用欄位名拿值、不依賴 ordinal——舊 DB 沒 strategy 欄、新 DB 有，欄位順序穩定
        var strategyOrd = r.GetOrdinal("strategy");
        while (r.Read())
        {
            list.Add(new TradeRecord
            {
                TradeId     = r.GetString(r.GetOrdinal("trade_id")),
                OrderId     = r.GetString(r.GetOrdinal("order_id")),
                Symbol      = r.GetString(r.GetOrdinal("symbol")),
                Exchange    = r.GetString(r.GetOrdinal("exchange")),
                Side        = r.GetString(r.GetOrdinal("side")),
                Quantity    = (decimal)r.GetDouble(r.GetOrdinal("quantity")),
                Price       = (decimal)r.GetDouble(r.GetOrdinal("price")),
                Fee         = r.IsDBNull(r.GetOrdinal("fee")) ? null : (decimal)r.GetDouble(r.GetOrdinal("fee")),
                RealizedPnl = r.IsDBNull(r.GetOrdinal("realized_pnl")) ? null : (decimal)r.GetDouble(r.GetOrdinal("realized_pnl")),
                ExecutedAt  = DateTime.Parse(r.GetString(r.GetOrdinal("executed_at"))),
                Strategy    = r.IsDBNull(strategyOrd) ? null : r.GetString(strategyOrd),
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
