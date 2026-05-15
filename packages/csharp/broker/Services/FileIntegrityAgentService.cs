using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// W14 P4 — Agent #14：File Integrity Monitor
///
/// 防護情境：攻擊者 SSH 進 VPS、偷改 `.env.trading` 把 BingX API key 換成自己的、broker 不知情繼續用、
/// 真錢被掏空。或者改 `access.json` 把自己加進 admin allowlist。
///
/// 解法：每小時 hash 一份 sensitive file 清單，跟前一次比、有變動就：
///   1. 寫一筆 `FILE_INTEGRITY_CHANGED` 到 audit_events（hash chain 防止再被偷塗銷）
///   2. 在 inbox reply 列出哪幾個檔變了、admin 在 dashboard 一眼看到
///   3. log warning 到 stdout（docker logs / journalctl 抓得到）
///
/// 設計選擇：baseline 存在 agent process 記憶體（broker restart 會清掉）。
/// 為什麼不持久化：第一次跑記錄 baseline 就好、broker 自己重啟（我手動 deploy）會清空 baseline、
/// 下一輪重新記。如果攻擊者剛好在 broker 重啟那刻偷改、會逃過 baseline 比對——但 audit_events 還是會
/// 記錄首輪 baseline 不同於上次、做事後 forensics 可以對比 git history 抓出來。
/// 持久化 baseline 反而會讓「我自己改 .env」變難——每次 deploy 都會被告警。
///
/// 監控清單：env `FILE_INTEGRITY_PATHS`（逗號分隔絕對路徑）覆蓋；預設 broker working dir 的 appsettings 系列。
/// </summary>
public class FileIntegrityAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_file_integrity";
    private const string PrincipalIdConst = "prn_agent_file_integrity";
    private const int PollIntervalSeconds = 60;
    private const int AutoPushIntervalSeconds = 60 * 60;   // 每 1h 跑一次

    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<FileIntegrityAgentService> _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    private readonly Dictionary<string, string> _baseline = new();
    private readonly object _baselineLock = new();

    public FileIntegrityAgentService(IServiceProvider sp, IConfiguration config,
        ILogger<FileIntegrityAgentService> logger)
    {
        _sp = sp; _config = config; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        _logger.LogInformation("[{Agent}] started — file hash check every {S}s", AgentIdConst, AutoPushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if ((DateTime.UtcNow - _lastAutoPushAt).TotalSeconds > AutoPushIntervalSeconds)
                {
                    PushScheduled();
                    _lastAutoPushAt = DateTime.UtcNow;
                }
                ProcessOnePending();
            }
            catch (Exception ex) { _logger.LogError(ex, "[{Agent}] poll error", AgentIdConst); }
            try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void EnsureAgentExists()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        if (db.Get<Principal>(PrincipalIdConst) != null) return;
        db.Insert(new Principal {
            PrincipalId = PrincipalIdConst, ActorType = ActorType.AI,
            DisplayName = "File Integrity Monitor (1h)",
            Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow,
        });
    }

    private void PushScheduled()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        var maxSeq = db.QueryFirst<MaxSeqRow>(
            "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
            new { aid = AgentIdConst });
        db.Insert(new AgentInboxTask {
            TaskId = $"inbox_{Guid.NewGuid():N}"[..20],
            AgentId = AgentIdConst,
            Seq = (maxSeq?.Seq ?? 0) + 1,
            Prompt = "{}",
            Status = "pending",
            RequestedBy = $"{nameof(FileIntegrityAgentService)} (auto)",
            CreatedAt = DateTime.UtcNow,
        });
    }

    private void ProcessOnePending()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        var auditSvc = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var pending = db.QueryFirst<AgentInboxTask>(
            "SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND status = 'pending' ORDER BY seq ASC LIMIT 1",
            new { aid = AgentIdConst });
        if (pending == null) return;
        var rows = db.Execute(
            "UPDATE agent_inbox_tasks SET status='processing', started_at=@ts WHERE task_id=@tid AND status='pending'",
            new { tid = pending.TaskId, ts = DateTime.UtcNow });
        if (rows == 0) return;

        var startMs = DateTime.UtcNow;
        try
        {
            var paths = ResolvePaths();
            var sb = new StringBuilder();
            sb.AppendLine("# File Integrity Check");
            sb.AppendLine();
            sb.AppendLine($"- 時間: {DateTime.UtcNow:o}");
            sb.AppendLine($"- 監控檔案數: {paths.Count}");
            sb.AppendLine();

            var firstRun = false;
            lock (_baselineLock) firstRun = _baseline.Count == 0;

            var changes = new List<(string Path, string OldHash, string NewHash, string Status)>();
            var missing = new List<string>();

            foreach (var p in paths)
            {
                if (!File.Exists(p))
                {
                    missing.Add(p);
                    string? prevHash = null;
                    lock (_baselineLock) _baseline.TryGetValue(p, out prevHash);
                    if (prevHash != null && !firstRun)
                    {
                        changes.Add((p, prevHash, "(missing)", "DELETED"));
                        lock (_baselineLock) _baseline.Remove(p);
                    }
                    continue;
                }

                var hash = HashFile(p);
                string? prev = null;
                lock (_baselineLock) _baseline.TryGetValue(p, out prev);

                if (prev == null)
                {
                    lock (_baselineLock) _baseline[p] = hash;
                    if (!firstRun)
                        changes.Add((p, "(new)", hash, "ADDED"));
                }
                else if (prev != hash)
                {
                    lock (_baselineLock) _baseline[p] = hash;
                    changes.Add((p, prev, hash, "CHANGED"));
                }
            }

            if (firstRun)
            {
                sb.AppendLine("## 🟢 首輪 baseline 已記錄");
                sb.AppendLine();
                sb.AppendLine("第一輪不告警；下一輪起若任何 hash 變動會列出 + 寫 audit_events。");
                sb.AppendLine();
                sb.AppendLine("### 已記錄 baseline 檔案");
                foreach (var (p, h) in SnapshotBaseline().OrderBy(x => x.Key))
                    sb.AppendLine($"- `{p}` → `{h.Substring(0, 12)}…`");
                if (missing.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("### 找不到（可能是部署環境差異）");
                    foreach (var p in missing) sb.AppendLine($"- `{p}`");
                }
            }
            else if (changes.Count == 0)
            {
                sb.AppendLine("## ✅ 全部檔案 hash 不變、無篡改跡象");
                sb.AppendLine();
                sb.AppendLine($"檢查 {paths.Count} 個 path、{paths.Count - missing.Count} 個存在、全部 hash 跟上輪一致。");
            }
            else
            {
                sb.AppendLine("## 🚨 偵測到檔案變動");
                sb.AppendLine();
                foreach (var c in changes)
                {
                    sb.AppendLine($"- **{c.Status}** `{c.Path}`");
                    sb.AppendLine($"  - 上次: `{Snip(c.OldHash)}`");
                    sb.AppendLine($"  - 本次: `{Snip(c.NewHash)}`");
                }
                sb.AppendLine();
                sb.AppendLine("**確認：** 這是合法 deploy 還是入侵？比對 git log + 上次部署時間。");

                // 寫稽核（hash chain 防被改）
                var traceId = BrokerCore.IdGen.New("trc");
                auditSvc.RecordEvent(traceId, "FILE_INTEGRITY_CHANGED",
                    principalId: PrincipalIdConst,
                    resourceRef: "broker.file_integrity",
                    details: JsonSerializer.Serialize(new {
                        changes = changes.Select(c => new { path = c.Path, status = c.Status, old_hash = c.OldHash, new_hash = c.NewHash }),
                        at = DateTime.UtcNow,
                    }));
                _logger.LogWarning("[{Agent}] {N} file(s) changed — see audit trace {T}",
                    AgentIdConst, changes.Count, traceId);
            }

            pending.Status = "done";
            pending.Reply = sb.ToString();
            pending.Model = "(SHA-256 hash check, no LLM)";
            pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation("[{Agent}] task={T} files={F} changes={C}",
                AgentIdConst, pending.TaskId, paths.Count, changes.Count);
        }
        catch (Exception ex)
        {
            pending.Status = "failed";
            pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    private List<string> ResolvePaths()
    {
        // 優先：env / appsettings 覆蓋
        var fromEnv = Environment.GetEnvironmentVariable("FILE_INTEGRITY_PATHS")
                   ?? _config["FileIntegrity:Paths"];
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => Path.GetFullPath(p))
                .ToList();
        }

        // 預設清單：broker working dir 的 appsettings + 常見 sensitive
        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(cwd, "appsettings.json"),
            Path.Combine(cwd, "appsettings.Production.json"),
            Path.Combine(cwd, "data", "access.json"),
            Path.Combine(cwd, ".env.trading"),
        };
        return candidates.Distinct().ToList();
    }

    private static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var bytes = sha.ComputeHash(fs);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Snip(string s) => s.Length > 12 ? s.Substring(0, 12) + "…" : s;

    private Dictionary<string, string> SnapshotBaseline()
    {
        lock (_baselineLock) return new Dictionary<string, string>(_baseline);
    }

    private class MaxSeqRow { public int Seq { get; set; } }
}
