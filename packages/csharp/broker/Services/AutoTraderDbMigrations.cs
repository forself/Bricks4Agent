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

        // 2026-06-04:per-leg「用 strategy emit 的結構性停損」開關(諧波 PRZ 失效價);default 0 = 既有行為
        AddColumnIfMissing(db, logger,
            table: "auto_trade_watchlist",
            column: "use_signal_sl",
            sqlType: "INTEGER NOT NULL DEFAULT 0");

        // Batch C+++ Phase 2：HTF（higher timeframe）大週期確認週期
        // null = 不做 HTF 確認、跟既有 watch entry 行為一致
        AddColumnIfMissing(db, logger,
            table: "auto_trade_watchlist",
            column: "htf_interval",
            sqlType: "TEXT NULL");

        // Shadow 模式:評估訊號但「只記錄、絕不下單」。預設 0 = 既有 watch 全是真交易、行為不變。
        // 安全關鍵:必須持久化,否則 shadow watch 重啟後會變回真交易 watch。
        AddColumnIfMissing(db, logger,
            table: "auto_trade_watchlist",
            column: "shadow",
            sqlType: "INTEGER NOT NULL DEFAULT 0");

        // Phase A2: 加 owner 欄位、既有資料一律標 prn_dashboard（admin）。
        // 之後 user 自己加的 watch 會用各自 principal_id；admin 看全部、user 看自己。
        AddColumnIfMissing(db, logger,
            table: "auto_trade_watchlist",
            column: "owner_principal_id",
            sqlType: "TEXT NOT NULL DEFAULT 'prn_dashboard'");

        AddColumnIfMissing(db, logger,
            table: "backtest_results",
            column: "owner_principal_id",
            sqlType: "TEXT NOT NULL DEFAULT 'prn_dashboard'");

        // B3 walk-forward：補 OOS 過擬合監測欄位。早期 VPS broker.db 在這 5 欄位加進
        // BacktestResultEntry model 前就建好 backtest_results 表了、ALTER 才能補齊。
        // 不補：/lab/overfit 查 ABS(is_oos_gap) 直接 'no such column: wf_folds' 500。
        AddColumnIfMissing(db, logger, "backtest_results", "wf_folds",       "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(db, logger, "backtest_results", "oos_return_pct", "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(db, logger, "backtest_results", "oos_sharpe",     "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(db, logger, "backtest_results", "oos_win_rate",   "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(db, logger, "backtest_results", "is_oos_gap",     "TEXT NOT NULL DEFAULT '0'");

        // B 後加的 regime 欄位（per-regime ranking 用）— 同樣可能舊表沒有
        AddColumnIfMissing(db, logger, "backtest_results", "regime", "TEXT NULL");

        // A2.5b PASS 2：perp_position_state 加 owner、讓不同 user 同 (exchange, symbol, side) 部位獨立
        AddColumnIfMissing(db, logger,
            table: "perp_position_state",
            column: "owner_principal_id",
            sqlType: "TEXT NOT NULL DEFAULT 'prn_dashboard'");

        // A4 PASS 1：principal_credentials 加 failed-login 追蹤 + 鎖定欄位
        AddColumnIfMissing(db, logger,
            table: "principal_credentials",
            column: "failed_login_attempts",
            sqlType: "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(db, logger,
            table: "principal_credentials",
            column: "first_failed_at",
            sqlType: "TEXT NULL");
        AddColumnIfMissing(db, logger,
            table: "principal_credentials",
            column: "locked_until",
            sqlType: "TEXT NULL");

        // A4 PASS 2：principal_credentials 加 TOTP 2FA 欄位
        AddColumnIfMissing(db, logger, "principal_credentials", "totp_secret_enc", "TEXT NULL");
        AddColumnIfMissing(db, logger, "principal_credentials", "totp_enrolled_at", "TEXT NULL");
        AddColumnIfMissing(db, logger, "principal_credentials", "backup_codes_enc", "TEXT NULL");

        // Scanner Hybrid Phase 1 B.4(2026-05-27)— scanner_active_legs close-side lifecycle
        // soft close 設計:row 不刪、改填 closed_at + exit_price + realized_pnl_pct + close_reason
        // 之前 B.3 進 DB 的 row 沒這幾欄、得 ALTER 補上
        AddColumnIfMissing(db, logger, "scanner_active_legs", "closed_at",        "TEXT NULL");
        AddColumnIfMissing(db, logger, "scanner_active_legs", "exit_price",       "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(db, logger, "scanner_active_legs", "realized_pnl_pct", "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(db, logger, "scanner_active_legs", "close_reason",     "TEXT NOT NULL DEFAULT ''");

        // 多市場支援(236ad05)— ScannerLegEntry 後加 exchange 欄,但 scanner_legs 是 Phase 1(6c57049)建的。
        // EnsureTable 是 CREATE TABLE IF NOT EXISTS、不補欄,既有 broker.db 沒這欄 → INSERT exchange 會 500。
        // 預設 binance 保留既有 crypto scanner 行為;美股 scanner 用 exchange='alpaca'。
        AddColumnIfMissing(db, logger, "scanner_legs", "exchange", "TEXT NOT NULL DEFAULT 'binance'");
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
