using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;

namespace FunctionPool.Diagnostics;

/// <summary>
/// 自動排程診斷服務 — 類似 SQL Server Agent Job
///
/// 資料保留策略：
///   • diagnostics.db       — 即時資料，保留 7 天，每次掃描後自動清理 + VACUUM
///   • diagnostics-archive.db — 錯誤/嚴重問題歷史，保留 90 天
///   • 清理時先將 Error/Critical 搬到 archive，再刪除過期資料
///
/// 排程頻率：預設每 15 分鐘（可設定）
/// </summary>
public class ScheduledDiagnosticsService : IAsyncDisposable
{
    private readonly IDiagnosticsService _diagnostics;
    private readonly ILogger<ScheduledDiagnosticsService> _logger;
    private readonly string _dbPath;
    private readonly string _archiveDbPath;
    private readonly int _intervalMinutes;
    private readonly int _retentionDays;
    private readonly int _archiveRetentionDays;

    private Timer? _timer;
    private int _runCount;
    private int _cleanupCounter;
    private bool _disposed;

    // 每 4 次掃描做一次清理（避免每次都清）
    private const int CleanupEveryNRuns = 4;

    public ScheduledDiagnosticsService(
        IDiagnosticsService diagnostics,
        ILogger<ScheduledDiagnosticsService> logger,
        string dbPath,
        int intervalMinutes = 15,
        int retentionDays = 7,
        int archiveRetentionDays = 90)
    {
        _diagnostics          = diagnostics;
        _logger               = logger;
        _dbPath               = dbPath;
        _archiveDbPath        = Path.Combine(
            Path.GetDirectoryName(dbPath) ?? ".",
            "diagnostics-archive.db");
        _intervalMinutes      = intervalMinutes;
        _retentionDays        = retentionDays;
        _archiveRetentionDays = archiveRetentionDays;

        InitDb(_dbPath);
        InitDb(_archiveDbPath);
    }

    public void Start()
    {
        _logger.LogInformation(
            "Scheduled diagnostics: interval={Interval}min, retention={Ret}d, archive={Arc}d, db={Db}",
            _intervalMinutes, _retentionDays, _archiveRetentionDays, _dbPath);

        _timer = new Timer(
            async _ => await RunScheduledScan(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(_intervalMinutes));
    }

    // ═══════════════════════════════════════════════════════════════
    // 排程掃描
    // ═══════════════════════════════════════════════════════════════

    private async Task RunScheduledScan()
    {
        if (_disposed) return;

        var runId = $"sched_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        _runCount++;

        try
        {
            var report = await _diagnostics.ScanAsync();
            SaveToDb(runId, report);

            if (!report.Healthy)
                _logger.LogWarning(
                    "Scan #{N}: UNHEALTHY — {C} critical, {E} errors, {W} warnings",
                    _runCount, report.CriticalCount, report.ErrorCount, report.WarningCount);
            else
                _logger.LogInformation(
                    "Scan #{N}: healthy — {Ct} containers, {Wk} workers",
                    _runCount, report.RunningContainers, report.ReadyWorkers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan #{N} failed", _runCount);
            SaveErrorRun(runId, ex.Message);
        }

        // 定期清理
        _cleanupCounter++;
        if (_cleanupCounter >= CleanupEveryNRuns)
        {
            _cleanupCounter = 0;
            RunRetentionCleanup();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 資料保留策略
    // ═══════════════════════════════════════════════════════════════

    private void RunRetentionCleanup()
    {
        try
        {
            var cutoff        = DateTime.UtcNow.AddDays(-_retentionDays).ToString("O");
            var archiveCutoff = DateTime.UtcNow.AddDays(-_archiveRetentionDays).ToString("O");

            // Step 1: 將即將過期但有錯誤的資料搬到 archive
            ArchiveErrors(cutoff);

            // Step 2: 刪除 diagnostics.db 中過期資料
            int deletedRuns, deletedIssues;
            using (var conn = Open(_dbPath))
            {
                deletedIssues = ExecuteScalar(conn,
                    "DELETE FROM diagnostic_issues WHERE run_id IN (SELECT run_id FROM diagnostic_runs WHERE scanned_at < $cut)",
                    ("$cut", cutoff));
                deletedRuns = ExecuteScalar(conn,
                    "DELETE FROM diagnostic_runs WHERE scanned_at < $cut",
                    ("$cut", cutoff));

                // VACUUM 壓縮（只在有刪除時）
                if (deletedRuns > 0)
                    Execute(conn, "VACUUM");
            }

            // Step 3: 刪除 archive 中超過 90 天的資料
            int archivedDeleted;
            using (var conn = Open(_archiveDbPath))
            {
                ExecuteScalar(conn,
                    "DELETE FROM diagnostic_issues WHERE run_id IN (SELECT run_id FROM diagnostic_runs WHERE scanned_at < $cut)",
                    ("$cut", archiveCutoff));
                archivedDeleted = ExecuteScalar(conn,
                    "DELETE FROM diagnostic_runs WHERE scanned_at < $cut",
                    ("$cut", archiveCutoff));

                if (archivedDeleted > 0)
                    Execute(conn, "VACUUM");
            }

            if (deletedRuns > 0 || archivedDeleted > 0)
                _logger.LogInformation(
                    "Retention cleanup: deleted {R} runs + {I} issues from active, {A} runs from archive",
                    deletedRuns, deletedIssues, archivedDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention cleanup failed");
        }
    }

    private void ArchiveErrors(string cutoff)
    {
        // 找出即將過期且有 Error/Critical 的 runs
        using var srcConn = Open(_dbPath);
        var runs = new List<(string RunId, string ScannedAt, int Critical, int Error, int Warning)>();

        var findCmd = srcConn.CreateCommand();
        findCmd.CommandText = """
            SELECT run_id, scanned_at, critical_count, error_count, warning_count
            FROM diagnostic_runs
            WHERE scanned_at < $cut AND (critical_count > 0 OR error_count > 0)
        """;
        findCmd.Parameters.AddWithValue("$cut", cutoff);

        using (var r = findCmd.ExecuteReader())
            while (r.Read())
                runs.Add((r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));

        if (runs.Count == 0) return;

        // 寫入 archive
        using var archConn = Open(_archiveDbPath);
        using var tx = archConn.BeginTransaction();

        foreach (var run in runs)
        {
            // 檢查是否已存在
            var existCmd = archConn.CreateCommand();
            existCmd.CommandText = "SELECT COUNT(*) FROM diagnostic_runs WHERE run_id = $rid";
            existCmd.Parameters.AddWithValue("$rid", run.RunId);
            if (Convert.ToInt32(existCmd.ExecuteScalar()) > 0) continue;

            // 複製 run
            var copyRunCmd = archConn.CreateCommand();
            copyRunCmd.CommandText = """
                INSERT INTO diagnostic_runs
                (run_id, scanned_at, healthy, runtime_available, total_containers,
                 running_containers, total_workers, ready_workers,
                 critical_count, error_count, warning_count, scan_type)
                SELECT run_id, scanned_at, healthy, runtime_available, total_containers,
                       running_containers, total_workers, ready_workers,
                       critical_count, error_count, warning_count, 'archived'
                FROM diagnostic_runs WHERE run_id = $rid
            """;
            // 用 src 讀、arch 寫 — 需要用 srcConn 讀
            var srcRunCmd = srcConn.CreateCommand();
            srcRunCmd.CommandText = """
                SELECT run_id, scanned_at, healthy, runtime_available, total_containers,
                       running_containers, total_workers, ready_workers,
                       critical_count, error_count, warning_count
                FROM diagnostic_runs WHERE run_id = $rid
            """;
            srcRunCmd.Parameters.AddWithValue("$rid", run.RunId);
            using var srcReader = srcRunCmd.ExecuteReader();
            if (srcReader.Read())
            {
                var insertCmd = archConn.CreateCommand();
                insertCmd.CommandText = """
                    INSERT INTO diagnostic_runs
                    (run_id, scanned_at, healthy, runtime_available, total_containers,
                     running_containers, total_workers, ready_workers,
                     critical_count, error_count, warning_count, scan_type)
                    VALUES ($a,$b,$c,$d,$e,$f,$g,$h,$i,$j,$k,'archived')
                """;
                insertCmd.Parameters.AddWithValue("$a", srcReader.GetString(0));
                insertCmd.Parameters.AddWithValue("$b", srcReader.GetString(1));
                insertCmd.Parameters.AddWithValue("$c", srcReader.GetInt32(2));
                insertCmd.Parameters.AddWithValue("$d", srcReader.GetInt32(3));
                insertCmd.Parameters.AddWithValue("$e", srcReader.GetInt32(4));
                insertCmd.Parameters.AddWithValue("$f", srcReader.GetInt32(5));
                insertCmd.Parameters.AddWithValue("$g", srcReader.GetInt32(6));
                insertCmd.Parameters.AddWithValue("$h", srcReader.GetInt32(7));
                insertCmd.Parameters.AddWithValue("$i", srcReader.GetInt32(8));
                insertCmd.Parameters.AddWithValue("$j", srcReader.GetInt32(9));
                insertCmd.Parameters.AddWithValue("$k", srcReader.GetInt32(10));
                insertCmd.ExecuteNonQuery();
            }
            srcReader.Close();

            // 複製 issues（只保留 Error/Critical）
            var srcIssueCmd = srcConn.CreateCommand();
            srcIssueCmd.CommandText = """
                SELECT severity, category, entity_id, message, detected_at
                FROM diagnostic_issues
                WHERE run_id = $rid AND severity IN ('Critical','Error')
            """;
            srcIssueCmd.Parameters.AddWithValue("$rid", run.RunId);

            using var issueReader = srcIssueCmd.ExecuteReader();
            while (issueReader.Read())
            {
                var archIssueCmd = archConn.CreateCommand();
                archIssueCmd.CommandText = """
                    INSERT INTO diagnostic_issues (run_id, severity, category, entity_id, message, detected_at)
                    VALUES ($rid, $sev, $cat, $eid, $msg, $det)
                """;
                archIssueCmd.Parameters.AddWithValue("$rid", run.RunId);
                archIssueCmd.Parameters.AddWithValue("$sev", issueReader.GetString(0));
                archIssueCmd.Parameters.AddWithValue("$cat", issueReader.GetString(1));
                archIssueCmd.Parameters.AddWithValue("$eid", issueReader.IsDBNull(2) ? DBNull.Value : issueReader.GetString(2));
                archIssueCmd.Parameters.AddWithValue("$msg", issueReader.GetString(3));
                archIssueCmd.Parameters.AddWithValue("$det", issueReader.GetString(4));
                archIssueCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        _logger.LogInformation("Archived {Count} error runs to {Path}", runs.Count, _archiveDbPath);
    }

    // ═══════════════════════════════════════════════════════════════
    // SQLite 初始化與工具
    // ═══════════════════════════════════════════════════════════════

    private void InitDb(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = Open(path);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS diagnostic_runs (
                run_id              TEXT PRIMARY KEY,
                scanned_at          TEXT NOT NULL,
                healthy             INTEGER NOT NULL,
                runtime_available   INTEGER NOT NULL,
                total_containers    INTEGER NOT NULL,
                running_containers  INTEGER NOT NULL,
                total_workers       INTEGER NOT NULL,
                ready_workers       INTEGER NOT NULL,
                critical_count      INTEGER NOT NULL,
                error_count         INTEGER NOT NULL,
                warning_count       INTEGER NOT NULL,
                scan_type           TEXT NOT NULL DEFAULT 'scheduled'
            );
        """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS diagnostic_issues (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id          TEXT NOT NULL,
                severity        TEXT NOT NULL,
                category        TEXT NOT NULL,
                entity_id       TEXT,
                message         TEXT NOT NULL,
                detected_at     TEXT NOT NULL,
                FOREIGN KEY (run_id) REFERENCES diagnostic_runs(run_id)
            );
        """);

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS idx_diag_issues_run ON diagnostic_issues(run_id);
            CREATE INDEX IF NOT EXISTS idx_diag_issues_sev ON diagnostic_issues(severity);
            CREATE INDEX IF NOT EXISTS idx_diag_runs_time  ON diagnostic_runs(scanned_at);
        """);

        // WAL 模式 — 提升讀寫並行效能
        Execute(conn, "PRAGMA journal_mode=WAL");
    }

    private void SaveToDb(string runId, DiagnosticReport report)
    {
        using var conn = Open(_dbPath);
        using var tx   = conn.BeginTransaction();

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO diagnostic_runs
            (run_id, scanned_at, healthy, runtime_available, total_containers,
             running_containers, total_workers, ready_workers,
             critical_count, error_count, warning_count, scan_type)
            VALUES ($rid, $at, $h, $rt, $tc, $rc, $tw, $rw, $cc, $ec, $wc, 'scheduled')
        """;
        cmd.Parameters.AddWithValue("$rid", runId);
        cmd.Parameters.AddWithValue("$at",  report.ScannedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$h",   report.Healthy ? 1 : 0);
        cmd.Parameters.AddWithValue("$rt",  report.RuntimeAvailable ? 1 : 0);
        cmd.Parameters.AddWithValue("$tc",  report.TotalContainers);
        cmd.Parameters.AddWithValue("$rc",  report.RunningContainers);
        cmd.Parameters.AddWithValue("$tw",  report.TotalWorkers);
        cmd.Parameters.AddWithValue("$rw",  report.ReadyWorkers);
        cmd.Parameters.AddWithValue("$cc",  report.CriticalCount);
        cmd.Parameters.AddWithValue("$ec",  report.ErrorCount);
        cmd.Parameters.AddWithValue("$wc",  report.WarningCount);
        cmd.ExecuteNonQuery();

        if (report.Issues.Count > 0)
        {
            var issueCmd = conn.CreateCommand();
            issueCmd.CommandText = """
                INSERT INTO diagnostic_issues (run_id, severity, category, entity_id, message, detected_at)
                VALUES ($rid, $sev, $cat, $eid, $msg, $det)
            """;
            foreach (var issue in report.Issues)
            {
                issueCmd.Parameters.Clear();
                issueCmd.Parameters.AddWithValue("$rid", runId);
                issueCmd.Parameters.AddWithValue("$sev", issue.Severity.ToString());
                issueCmd.Parameters.AddWithValue("$cat", issue.Category);
                issueCmd.Parameters.AddWithValue("$eid", (object?)issue.EntityId ?? DBNull.Value);
                issueCmd.Parameters.AddWithValue("$msg", issue.Message);
                issueCmd.Parameters.AddWithValue("$det", issue.DetectedAt.ToString("O"));
                issueCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    private void SaveErrorRun(string runId, string errorMessage)
    {
        try
        {
            using var conn = Open(_dbPath);
            Execute(conn, $"""
                INSERT INTO diagnostic_runs
                (run_id, scanned_at, healthy, runtime_available, total_containers,
                 running_containers, total_workers, ready_workers,
                 critical_count, error_count, warning_count, scan_type)
                VALUES ('{runId}', '{DateTime.UtcNow:O}', 0, 0, 0, 0, 0, 0, 1, 0, 0, 'scheduled')
            """);
            var issueCmd = conn.CreateCommand();
            issueCmd.CommandText = """
                INSERT INTO diagnostic_issues (run_id, severity, category, entity_id, message, detected_at)
                VALUES ($rid, 'Critical', 'ScheduledScan', 'system', $msg, $det)
            """;
            issueCmd.Parameters.AddWithValue("$rid", runId);
            issueCmd.Parameters.AddWithValue("$msg", errorMessage);
            issueCmd.Parameters.AddWithValue("$det", DateTime.UtcNow.ToString("O"));
            issueCmd.ExecuteNonQuery();
        }
        catch { /* best effort */ }
    }

    // ═══════════════════════════════════════════════════════════════
    // 查詢 API（給 endpoint 用）
    // ═══════════════════════════════════════════════════════════════

    public List<DiagnosticRunSummary> GetRecentRuns(int take = 24)
    {
        using var conn = Open(_dbPath);
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT run_id, scanned_at, healthy, runtime_available,
                   running_containers, ready_workers,
                   critical_count, error_count, warning_count
            FROM diagnostic_runs ORDER BY scanned_at DESC LIMIT $take
        """;
        cmd.Parameters.AddWithValue("$take", take);

        var list = new List<DiagnosticRunSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DiagnosticRunSummary
            {
                RunId             = r.GetString(0),
                ScannedAt         = r.GetString(1),
                Healthy           = r.GetInt32(2) == 1,
                RuntimeAvailable  = r.GetInt32(3) == 1,
                RunningContainers = r.GetInt32(4),
                ReadyWorkers      = r.GetInt32(5),
                CriticalCount     = r.GetInt32(6),
                ErrorCount        = r.GetInt32(7),
                WarningCount      = r.GetInt32(8)
            });
        return list;
    }

    public List<DiagnosticRunSummary> GetArchiveRuns(int take = 50)
    {
        using var conn = Open(_archiveDbPath);
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT run_id, scanned_at, healthy, runtime_available,
                   running_containers, ready_workers,
                   critical_count, error_count, warning_count
            FROM diagnostic_runs ORDER BY scanned_at DESC LIMIT $take
        """;
        cmd.Parameters.AddWithValue("$take", take);

        var list = new List<DiagnosticRunSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DiagnosticRunSummary
            {
                RunId             = r.GetString(0),
                ScannedAt         = r.GetString(1),
                Healthy           = r.GetInt32(2) == 1,
                RuntimeAvailable  = r.GetInt32(3) == 1,
                RunningContainers = r.GetInt32(4),
                ReadyWorkers      = r.GetInt32(5),
                CriticalCount     = r.GetInt32(6),
                ErrorCount        = r.GetInt32(7),
                WarningCount      = r.GetInt32(8)
            });
        return list;
    }

    public List<DiagnosticIssueSummary> GetIssuesForRun(string runId)
    {
        // 先查 active，再查 archive
        var list = QueryIssues(_dbPath, runId);
        if (list.Count == 0)
            list = QueryIssues(_archiveDbPath, runId);
        return list;
    }

    private List<DiagnosticIssueSummary> QueryIssues(string dbPath, string runId)
    {
        using var conn = Open(dbPath);
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT severity, category, entity_id, message, detected_at
            FROM diagnostic_issues WHERE run_id = $rid ORDER BY id
        """;
        cmd.Parameters.AddWithValue("$rid", runId);

        var list = new List<DiagnosticIssueSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DiagnosticIssueSummary
            {
                Severity   = r.GetString(0),
                Category   = r.GetString(1),
                EntityId   = r.IsDBNull(2) ? null : r.GetString(2),
                Message    = r.GetString(3),
                DetectedAt = r.GetString(4)
            });
        return list;
    }

    /// <summary>取得空間使用摘要</summary>
    public DbSpaceInfo GetSpaceInfo()
    {
        return new DbSpaceInfo
        {
            ActiveDbSizeBytes  = File.Exists(_dbPath) ? new FileInfo(_dbPath).Length : 0,
            ArchiveDbSizeBytes = File.Exists(_archiveDbPath) ? new FileInfo(_archiveDbPath).Length : 0,
            ActiveRunCount     = CountRows(_dbPath, "diagnostic_runs"),
            ArchiveRunCount    = CountRows(_archiveDbPath, "diagnostic_runs"),
            RetentionDays      = _retentionDays,
            ArchiveRetentionDays = _archiveRetentionDays
        };
    }

    private int CountRows(string dbPath, string table)
    {
        if (!File.Exists(dbPath)) return 0;
        try
        {
            using var conn = Open(dbPath);
            return ExecuteScalar(conn, $"SELECT COUNT(*) FROM {table}");
        }
        catch { return 0; }
    }

    // ── SQLite 工具 ──────────────────────────────────────────────

    private SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int ExecuteScalar(SqliteConnection conn, string sql, params (string Name, object Value)[] parms)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parms)
            cmd.Parameters.AddWithValue(name, value);
        var result = cmd.ExecuteNonQuery();
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_timer != null) await _timer.DisposeAsync();
    }
}

// ═══════════════════════════════════════════════════════════════
// DTO
// ═══════════════════════════════════════════════════════════════

public class DiagnosticRunSummary
{
    public string RunId             { get; set; } = "";
    public string ScannedAt         { get; set; } = "";
    public bool   Healthy           { get; set; }
    public bool   RuntimeAvailable  { get; set; }
    public int    RunningContainers { get; set; }
    public int    ReadyWorkers      { get; set; }
    public int    CriticalCount     { get; set; }
    public int    ErrorCount        { get; set; }
    public int    WarningCount      { get; set; }
}

public class DiagnosticIssueSummary
{
    public string  Severity   { get; set; } = "";
    public string  Category   { get; set; } = "";
    public string? EntityId   { get; set; }
    public string  Message    { get; set; } = "";
    public string  DetectedAt { get; set; } = "";
}

public class DbSpaceInfo
{
    public long ActiveDbSizeBytes    { get; set; }
    public long ArchiveDbSizeBytes   { get; set; }
    public int  ActiveRunCount       { get; set; }
    public int  ArchiveRunCount      { get; set; }
    public int  RetentionDays        { get; set; }
    public int  ArchiveRetentionDays { get; set; }
}
