using BrokerCore.Data;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// Auto-trader 相關 schema 的小規模 migration——專門處理「model 加了新欄位、
/// 但既有 DB 是舊版本」的場景。BaseOrm 的 EnsureTable 是 CREATE TABLE IF NOT EXISTS、
/// 對既有表不會 ALTER、所以新欄位需要這裡手動補。
///
/// 目前處理：
///   - auto_trade_watchlist：補 mode (Phase 3) / leverage (Phase 3)
///
/// 為什麼不弄正規 migration framework：目前只有兩三條欄位、寫個 lib 划不來；
/// 之後如果累積到 10+ 條再考慮 FluentMigrator / EF Migrations。
/// </summary>
public static class AutoTraderDbMigrations
{
    public static void Apply(BrokerDb db, ILogger logger)
    {
        AddColumnIfMissing(db, logger,
            table: "auto_trade_watchlist",
            column: "mode",
            sqlType: "TEXT NOT NULL DEFAULT 'spot'");

        AddColumnIfMissing(db, logger,
            table: "auto_trade_watchlist",
            column: "leverage",
            sqlType: "INTEGER NOT NULL DEFAULT 5");
    }

    private static void AddColumnIfMissing(BrokerDb db, ILogger logger,
        string table, string column, string sqlType)
    {
        // SQLite 沒 IF NOT EXISTS for ADD COLUMN，先用 PRAGMA 查存在性
        try
        {
            var existing = db.Query<ColumnInfo>($"PRAGMA table_info({table})");
            if (existing.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
                return;

            db.Execute($"ALTER TABLE {table} ADD COLUMN {column} {sqlType}");
            logger.LogInformation("Migration: added column {Column} to {Table}", column, table);
        }
        catch (Exception ex)
        {
            // 表不存在就跳過（首次啟動會由 EnsureTable 建好；migration 在 EnsureTable 之後跑、正常不會走到這）
            logger.LogWarning(ex, "Migration skipped for {Table}.{Column} ({Reason})", table, column, ex.Message);
        }
    }

    // PRAGMA table_info 回的形狀：cid, name, type, notnull, dflt_value, pk
    private class ColumnInfo
    {
        public int Cid { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int NotNull { get; set; }
        public object? DfltValue { get; set; }
        public int Pk { get; set; }
    }
}
