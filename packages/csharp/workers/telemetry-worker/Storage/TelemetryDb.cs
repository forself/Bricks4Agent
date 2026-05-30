using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace TelemetryWorker.Storage;

/// <summary>一筆健康取樣。</summary>
public record TelemetrySample(
    string SampledAt,        // ISO-8601 UTC
    double? HealthScore,     // null = 取不到
    string Status,           // healthy/degraded/critical/unreachable…
    int WorkersTotal,
    int WorkersHealthy,
    string Raw);             // 原始 JSON(截斷),保留以便事後鑑識

/// <summary>
/// SQLite 持久化 — broker 健康時間序列。單一連線 + lock(取樣頻率低、reader/writer 共用一條連線)。
/// 這個 telemetry.db 會持續長大 → 正好當 failover 暖備複製/還原的安全測試標的。
/// </summary>
public sealed class TelemetryDb : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private readonly ILogger<TelemetryDb> _logger;

    public TelemetryDb(string dbPath, ILogger<TelemetryDb> logger)
    {
        _logger = logger;
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        // WAL:讓 Litestream 能持續複製;也減少 reader/writer 互卡
        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }
        InitSchema();
        _logger.LogInformation("TelemetryDb opened: {Path}", dbPath);
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS telemetry_sample (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                sampled_at      TEXT    NOT NULL,
                health_score    REAL,
                status          TEXT    NOT NULL,
                workers_total   INTEGER NOT NULL DEFAULT 0,
                workers_healthy INTEGER NOT NULL DEFAULT 0,
                raw             TEXT    NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_telemetry_time
                ON telemetry_sample(sampled_at);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Insert(TelemetrySample s)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO telemetry_sample
                    (sampled_at, health_score, status, workers_total, workers_healthy, raw)
                VALUES ($at, $score, $status, $total, $healthy, $raw)
                """;
            cmd.Parameters.AddWithValue("$at", s.SampledAt);
            cmd.Parameters.AddWithValue("$score", (object?)s.HealthScore ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", s.Status);
            cmd.Parameters.AddWithValue("$total", s.WorkersTotal);
            cmd.Parameters.AddWithValue("$healthy", s.WorkersHealthy);
            cmd.Parameters.AddWithValue("$raw", s.Raw);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>回最近 sinceUtc 之後的取樣(新到舊、最多 limit 筆)。</summary>
    public List<TelemetrySample> QueryRecent(DateTime sinceUtc, int limit)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT sampled_at, health_score, status, workers_total, workers_healthy, raw
                FROM telemetry_sample
                WHERE sampled_at >= $since
                ORDER BY sampled_at DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$since", sinceUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$limit", limit);
            return ReadAll(cmd);
        }
    }

    public TelemetrySample? Latest()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT sampled_at, health_score, status, workers_total, workers_healthy, raw
                FROM telemetry_sample
                ORDER BY id DESC LIMIT 1
                """;
            return ReadAll(cmd).FirstOrDefault();
        }
    }

    /// <summary>取樣總數(demo 用:暖備還原後比對筆數有沒有完整轉移)。</summary>
    public long Count()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM telemetry_sample";
            return (long)(cmd.ExecuteScalar() ?? 0L);
        }
    }

    public int Prune(DateTime olderThanUtc)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM telemetry_sample WHERE sampled_at < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", olderThanUtc.ToString("o"));
            return cmd.ExecuteNonQuery();
        }
    }

    private static List<TelemetrySample> ReadAll(SqliteCommand cmd)
    {
        var rows = new List<TelemetrySample>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            rows.Add(new TelemetrySample(
                SampledAt:      r.GetString(0),
                HealthScore:    r.IsDBNull(1) ? null : r.GetDouble(1),
                Status:         r.GetString(2),
                WorkersTotal:   r.GetInt32(3),
                WorkersHealthy: r.GetInt32(4),
                Raw:            r.GetString(5)));
        }
        return rows;
    }

    public void Dispose() => _conn.Dispose();
}
