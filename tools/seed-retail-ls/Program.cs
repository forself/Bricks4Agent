// 一次性 backfill retail_ls_ratio 表(從 data.binance.vision daily zip)
// 避免 strategy 100 lookback 的 70 天 cold start
//
// 用法:
//   dotnet run --project tools/seed-retail-ls -- <db-path> [days] [symbols...]
//   範例:
//     dotnet run --project tools/seed-retail-ls -- ./quote.db 365 BTCUSDT ETHUSDT SOLUSDT
//
// 預設:days=180、symbols=8 主流幣
using Microsoft.Data.Sqlite;
using ToolsShared;

if (args.Length < 1)
{
    Console.WriteLine("用法: dotnet run -- <db-path> [days] [symbols...]");
    return 1;
}

string dbPath = args[0];
int days = args.Length >= 2 && int.TryParse(args[1], out var d) ? d : 180;
string[] symbols = args.Length >= 3 ? args[2..]
    : new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "LTCUSDT", "OPUSDT", "SUIUSDT", "APTUSDT", "INJUSDT" };

if (!File.Exists(dbPath))
{
    Console.WriteLine($"❌ DB 不存在: {dbPath}");
    return 1;
}

var endDate = DateTime.UtcNow.Date;
var startDate = endDate.AddDays(-days);
Console.WriteLine($"=== Retail L/S backfill ===");
Console.WriteLine($"DB: {dbPath}");
Console.WriteLine($"Window: {startDate:yyyy-MM-dd} → {endDate:yyyy-MM-dd} ({days} days)");
Console.WriteLine($"Symbols: {string.Join(", ", symbols)}");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();

// 確保 table 存在
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS retail_ls_ratio (
            symbol TEXT NOT NULL,
            sample_time TEXT NOT NULL,
            ls_ratio REAL NOT NULL,
            PRIMARY KEY (symbol, sample_time)
        );
        CREATE INDEX IF NOT EXISTS idx_retail_ls_lookup ON retail_ls_ratio(symbol, sample_time);
        """;
    cmd.ExecuteNonQuery();
}

int totalSaved = 0;
foreach (var sym in symbols)
{
    Console.Write($"\n{sym}: ");
    var snaps = await OiMetricsCache.FetchOrLoad(sym, startDate, endDate);
    if (snaps.Count == 0) { Console.WriteLine("無資料"); continue; }

    // Aggregate 5min → daily (取每日最後一筆 ratio 做 EOD snapshot,跟 strategy 用 1d bar 對齊)
    var byDay = snaps.GroupBy(s => s.Ts.Date)
        .Select(g => g.OrderBy(s => s.Ts).Last())
        .ToList();

    string normalized = sym.Replace("USDT", "").ToUpper();
    using var tx = conn.BeginTransaction();
    using var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = "INSERT OR REPLACE INTO retail_ls_ratio (symbol, sample_time, ls_ratio) VALUES ($s, $t, $r)";
    var pSym = cmd.Parameters.Add("$s", SqliteType.Text);
    var pTime = cmd.Parameters.Add("$t", SqliteType.Text);
    var pRatio = cmd.Parameters.Add("$r", SqliteType.Real);

    int saved = 0;
    foreach (var s in byDay)
    {
        pSym.Value = normalized;
        pTime.Value = s.Ts.ToString("o");
        pRatio.Value = (double)s.CountLsRatio;
        cmd.ExecuteNonQuery();
        saved++;
    }
    tx.Commit();
    Console.WriteLine($"{saved} daily rows saved");
    totalSaved += saved;
}

Console.WriteLine($"\n✅ 總共 {totalSaved} 筆寫入 retail_ls_ratio");
return 0;
