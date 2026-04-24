using System.Diagnostics;
using System.Text.RegularExpressions;
using FunctionPool.Container;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FunctionPool.ContainerLogs;

/// <summary>
/// 背景服務：每 N 秒掃描所有 running 容器，將 Error / Warn 等級的日誌行搬進 SQLite。
///
/// 設計理念與 ScheduledDiagnosticsService 一致：獨立 DB + Timer + 保留天數 + VACUUM。
/// 只收錯誤等級的行（不是全量 log），避免塞爆磁碟。
///
/// 資料流：
///   docker logs --since {window}s --timestamps {id}
///     → 每行 "2026-04-24T13:42:01.123Z stdout ERROR: Something failed"
///     → Regex 判等級 → Insert SQLite
/// </summary>
public class ContainerLogTailService : IAsyncDisposable
{
    private readonly IContainerManager _containerMgr;
    private readonly ILogger<ContainerLogTailService> _logger;
    private readonly string _dbPath;
    private readonly string _runtime;
    private readonly int _pollSeconds;
    private readonly int _retentionDays;

    private Timer? _timer;
    private int _tickCount;
    private bool _disposed;

    // Regex：抓錯誤等級 — 對齊 .NET/Python/Node/Bash 常見 log format
    //  涵蓋：ERROR / ERR: / FATAL / Exception / unhandled / panic / traceback /
    //        fail(ed|ure) / denied / refused / timeout / crashed / aborted
    private static readonly Regex ErrorPattern = new(
        @"\b(ERROR|ERRORS|ERR|FATAL|CRITICAL|Exception|unhandled|panic|traceback|stack\s*trace|fail(ed|ure)?|denied|refused|timed\s*out|timeout|crashed|aborted)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WarnPattern = new(
        @"\b(WARN|WARNING|deprecated)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 誤報守門 — 常見的「成功但內文包含 ERROR/FAIL 字樣」pattern：
    //   "0 errors" / "no errors" / "without errors" / "no failures"
    //   "successfully" / "success: " 也一律視為成功
    private static readonly Regex FalsePositivePattern = new(
        @"\b(0\s+errors?|no\s+errors?|without\s+errors?|0\s+failures?|no\s+failures?)\b|\bsuccessfully\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // docker logs --timestamps 的行前綴格式: "2026-04-24T13:42:01.123Z "
    private static readonly Regex TimestampPrefix = new(
        @"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z?)\s+(.*)$",
        RegexOptions.Compiled);

    // ANSI escape 序列（色碼、游標移動等）— 清掉讓 UI 顯示乾淨文字
    private static readonly Regex AnsiEscape = new(
        @"\x1b\[[0-9;]*[A-Za-z]",
        RegexOptions.Compiled);

    private const int CleanupEveryNTicks = 60;      // 每 60 次 tick = 每 10min（若 poll=10s）清一次
    private const int MaxLinesPerContainerPerTick = 200;  // 上限，避免突發暴增

    public ContainerLogTailService(
        IContainerManager containerMgr,
        ContainerConfig containerConfig,
        ILogger<ContainerLogTailService> logger,
        string dbPath,
        int pollSeconds = 10,
        int retentionDays = 7)
    {
        _containerMgr  = containerMgr;
        _runtime       = containerConfig.Runtime;
        _logger        = logger;
        _dbPath        = dbPath;
        _pollSeconds   = pollSeconds;
        _retentionDays = retentionDays;

        InitDb();
        PruneFalsePositivesOnStartup();
    }

    /// <summary>
    /// 啟動時清掉歷史上被誤抓的 "0 errors" / "successfully" 等成功訊息。
    /// 這是 regex 改良後的回補，避免舊資料留著讓 UI 看起來都是假紅條。
    /// </summary>
    private void PruneFalsePositivesOnStartup()
    {
        try
        {
            using var conn = Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM container_log_entries
                WHERE message LIKE '%0 error%' COLLATE NOCASE
                   OR message LIKE '%no error%' COLLATE NOCASE
                   OR message LIKE '%0 failure%' COLLATE NOCASE
                   OR message LIKE '%no failure%' COLLATE NOCASE
                   OR message LIKE '%successfully%' COLLATE NOCASE";
            var pruned = cmd.ExecuteNonQuery();
            if (pruned > 0)
                _logger.LogInformation("Container log pruned {N} false-positive rows on startup", pruned);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "False-positive prune failed (non-critical)");
        }
    }

    public void Start()
    {
        _logger.LogInformation(
            "Container log tail: runtime={Rt}, poll={Poll}s, retention={Ret}d, db={Db}",
            _runtime, _pollSeconds, _retentionDays, _dbPath);

        _timer = new Timer(
            async _ => await TickAsync(),
            null,
            TimeSpan.FromSeconds(15),                 // 首次延遲 15s 避開啟動壅塞
            TimeSpan.FromSeconds(_pollSeconds));
    }

    private async Task TickAsync()
    {
        if (_disposed) return;
        _tickCount++;

        try
        {
            var containers = await _containerMgr.ListManagedAsync();
            var running = containers.Where(c => c.State == ContainerState.Running).ToList();

            foreach (var c in running)
                await CollectFromContainer(c.ContainerId, c.WorkerType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Log tail tick failed (non-critical)");
        }

        if (_tickCount % CleanupEveryNTicks == 0)
            RunRetentionCleanup();
    }

    private async Task CollectFromContainer(string containerId, string workerType)
    {
        try
        {
            // Docker CLI 的 `--since {n}s/m` 在某些 WSL / Docker 29.x 組合下會回空；
            // 改用 `--tail N` 一次拉最近 N 行，靠 msg_hash 去重避免重複插入。
            var tailN = Math.Max(100, _pollSeconds * 10);  // 每 tick 看最近 N 行
            var args = $"logs --tail {tailN} --timestamps {containerId}";

            var psi = new ProcessStartInfo(_runtime, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var exited = await Task.Run(() => proc.WaitForExit(TimeSpan.FromSeconds(8)));
            if (!exited) { try { proc.Kill(); } catch { } return; }

            // docker logs 將 stdout / stderr 分別寫入兩個流，合併成一份處理（stderr 裡的行給 isStderr=true）
            var stdoutText = await stdoutTask;
            var stderrText = await stderrTask;

            var lines = new List<(string raw, bool isStderr)>();
            foreach (var ln in stdoutText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                lines.Add((ln, false));
            foreach (var ln in stderrText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                lines.Add((ln, true));

            if (lines.Count == 0) return;

            var hitLines = new List<(DateTime ts, string level, string msg, bool stderr)>();
            foreach (var (raw, isStderr) in lines)
            {
                var match = TimestampPrefix.Match(raw);
                if (!match.Success) continue;

                var tsText = match.Groups[1].Value;
                var content = AnsiEscape.Replace(match.Groups[2].Value, "");

                string? level = null;
                if (ErrorPattern.IsMatch(content)) level = "ERROR";
                else if (WarnPattern.IsMatch(content)) level = "WARN";
                else if (isStderr) level = "ERROR";  // stderr 一律視為錯誤

                if (level == null) continue;

                // 守門：誤報模式（0 errors / successfully 等）直接丟掉
                if (FalsePositivePattern.IsMatch(content)) continue;
                if (!DateTime.TryParse(tsText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                    ts = DateTime.UtcNow;

                hitLines.Add((ts, level, content.Trim(), isStderr));
                if (hitLines.Count >= MaxLinesPerContainerPerTick) break;
            }

            if (hitLines.Count == 0) return;

            InsertEntries(containerId, workerType, hitLines);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Collect logs failed for {Id}", containerId);
        }
    }

    private void InsertEntries(string containerId, string workerType,
        List<(DateTime ts, string level, string msg, bool stderr)> entries)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        // 去重：同一 (container_id, ts, msg_hash) 只存一次
        foreach (var (ts, level, msg, stderr) in entries)
        {
            var hash = ComputeHash(msg);

            var existCmd = conn.CreateCommand();
            existCmd.CommandText = @"
                SELECT COUNT(*) FROM container_log_entries
                WHERE container_id = $cid AND ts = $ts AND msg_hash = $h";
            existCmd.Parameters.AddWithValue("$cid", containerId);
            existCmd.Parameters.AddWithValue("$ts", ts.ToString("O"));
            existCmd.Parameters.AddWithValue("$h", hash);
            if (Convert.ToInt32(existCmd.ExecuteScalar()) > 0) continue;

            var insCmd = conn.CreateCommand();
            insCmd.CommandText = @"
                INSERT INTO container_log_entries
                    (container_id, worker_type, ts, level, stderr, message, msg_hash)
                VALUES ($cid, $wt, $ts, $lv, $se, $msg, $h)";
            insCmd.Parameters.AddWithValue("$cid", containerId);
            insCmd.Parameters.AddWithValue("$wt", workerType ?? "");
            insCmd.Parameters.AddWithValue("$ts", ts.ToString("O"));
            insCmd.Parameters.AddWithValue("$lv", level);
            insCmd.Parameters.AddWithValue("$se", stderr ? 1 : 0);
            insCmd.Parameters.AddWithValue("$msg", msg.Length > 4000 ? msg[..4000] : msg);
            insCmd.Parameters.AddWithValue("$h", hash);
            insCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void RunRetentionCleanup()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays).ToString("O");
            using var conn = Open();

            var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM container_log_entries WHERE ts < $cut";
            delCmd.Parameters.AddWithValue("$cut", cutoff);
            var deleted = delCmd.ExecuteNonQuery();

            if (deleted > 0)
            {
                var vacCmd = conn.CreateCommand();
                vacCmd.CommandText = "VACUUM";
                vacCmd.ExecuteNonQuery();
                _logger.LogInformation("Container log retention: deleted {N} entries older than {D}d", deleted, _retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Container log retention cleanup failed");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 查詢 API（給 endpoint 用）
    // ═══════════════════════════════════════════════════════════════

    public List<ContainerLogEntryDto> Query(string? containerId, string? level, int limit = 200)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();

        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(containerId)) clauses.Add("container_id = $cid");
        if (!string.IsNullOrEmpty(level))        clauses.Add("level = $lv");
        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";

        cmd.CommandText = $@"
            SELECT container_id, worker_type, ts, level, stderr, message
            FROM container_log_entries
            {where}
            ORDER BY ts DESC
            LIMIT $lim";
        if (!string.IsNullOrEmpty(containerId)) cmd.Parameters.AddWithValue("$cid", containerId);
        if (!string.IsNullOrEmpty(level))        cmd.Parameters.AddWithValue("$lv", level);
        cmd.Parameters.AddWithValue("$lim", limit);

        var list = new List<ContainerLogEntryDto>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ContainerLogEntryDto
            {
                ContainerId = r.GetString(0),
                WorkerType  = r.GetString(1),
                Ts          = r.GetString(2),
                Level       = r.GetString(3),
                Stderr      = r.GetInt32(4) == 1,
                Message     = r.GetString(5),
            });
        return list;
    }

    public ContainerLogSpaceInfo GetSpaceInfo()
    {
        var size = File.Exists(_dbPath) ? new FileInfo(_dbPath).Length : 0;
        int rows = 0;
        if (File.Exists(_dbPath))
        {
            try
            {
                using var conn = Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM container_log_entries";
                rows = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { }
        }
        return new ContainerLogSpaceInfo
        {
            DbSizeBytes   = size,
            EntryCount    = rows,
            RetentionDays = _retentionDays,
        };
    }

    // ── SQLite 工具 ──────────────────────────────────────────────

    private void InitDb()
    {
        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var conn = Open();

        var createCmd = conn.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS container_log_entries (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                container_id    TEXT NOT NULL,
                worker_type     TEXT NOT NULL,
                ts              TEXT NOT NULL,
                level           TEXT NOT NULL,
                stderr          INTEGER NOT NULL,
                message         TEXT NOT NULL,
                msg_hash        TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_cle_cid ON container_log_entries(container_id);
            CREATE INDEX IF NOT EXISTS idx_cle_ts  ON container_log_entries(ts);
            CREATE INDEX IF NOT EXISTS idx_cle_lv  ON container_log_entries(level);
            CREATE INDEX IF NOT EXISTS idx_cle_dedup ON container_log_entries(container_id, ts, msg_hash);";
        createCmd.ExecuteNonQuery();

        var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL";
        walCmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static string ComputeHash(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes)[..16];
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_timer != null) await _timer.DisposeAsync();
    }
}

public class ContainerLogEntryDto
{
    public string ContainerId { get; set; } = "";
    public string WorkerType  { get; set; } = "";
    public string Ts          { get; set; } = "";
    public string Level       { get; set; } = "";
    public bool   Stderr      { get; set; }
    public string Message     { get; set; } = "";
}

public class ContainerLogSpaceInfo
{
    public long DbSizeBytes   { get; set; }
    public int  EntryCount    { get; set; }
    public int  RetentionDays { get; set; }
}
