using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// 稽核鏈完整性驗證 agent — 每 30 分鐘掃過去 1h 所有 trace、驗 hash chain 防篡改。
///
/// 為什麼專門做一個：Benson 設計 audit_events 表用 (previous_event_hash, event_hash)
/// 串成 per-trace hash chain、每筆 hash = SHA256(prev_hash + trace_id + seq + event_type +
/// payload_digest + occurred_at)。任何 INSERT 後的 UPDATE / DELETE 都會破壞鏈。
///
/// 這個 agent 把 IAuditService.VerifyTraceIntegrity() 包成定時掃描、有問題的 trace 列出來。
/// 對 Benson demo：直接展示「你設計的 hash chain 真的被驗證、不是 paper-only」。
///
/// 不走 LlmProxy（純規則檢查、不需要 LLM 解釋）—— 但 reply 仍落 inbox table、可被前端看。
/// </summary>
public class AuditChainVerifierAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_audit_chain_verifier";
    private const string PrincipalIdConst = "prn_agent_audit_chain_verifier";
    private const int PollIntervalSeconds = 60;
    private const int AutoPushIntervalSeconds = 30 * 60;
    private const int RecentWindowHours = 1;

    private readonly IServiceProvider _sp;
    private readonly ILogger<AuditChainVerifierAgentService> _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    public AuditChainVerifierAgentService(IServiceProvider sp, ILogger<AuditChainVerifierAgentService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — verifying audit chain every {S}s", AgentIdConst, AutoPushIntervalSeconds);

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
        db.Insert(new Principal
        {
            PrincipalId = PrincipalIdConst, ActorType = ActorType.AI,
            DisplayName = "Audit Chain Verifier (30min)", Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] registered principal", AgentIdConst);
    }

    private void PushScheduled()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        var maxSeq = db.QueryFirst<MaxSeqRow>(
            "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
            new { aid = AgentIdConst });
        db.Insert(new AgentInboxTask
        {
            TaskId = $"inbox_{Guid.NewGuid():N}"[..20], AgentId = AgentIdConst,
            Seq = (maxSeq?.Seq ?? 0) + 1,
            Prompt = JsonSerializer.Serialize(new { window_hours = RecentWindowHours }),
            Status = "pending",
            RequestedBy = $"{nameof(AuditChainVerifierAgentService)} (auto)",
            CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] auto-pushed task", AgentIdConst);
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
            int windowHours = RecentWindowHours;
            try
            {
                using var doc = JsonDocument.Parse(pending.Prompt);
                if (doc.RootElement.TryGetProperty("window_hours", out var wh) && wh.TryGetInt32(out var whv))
                    windowHours = Math.Clamp(whv, 1, 168);
            }
            catch { /* ignore parse errors，用 default */ }

            var sinceStr = DateTime.UtcNow.AddHours(-windowHours).ToString("o");
            var distinctTraces = db.Query<TraceIdRow>(
                "SELECT DISTINCT trace_id FROM audit_events WHERE occurred_at >= @sinceStr ORDER BY trace_id",
                new { sinceStr });

            int total = distinctTraces.Count;
            int valid = 0;
            int totalEvents = 0;
            var brokenTraces = new List<string>();
            foreach (var row in distinctTraces)
            {
                var traceId = row.TraceId;
                if (string.IsNullOrEmpty(traceId)) continue;
                try
                {
                    if (auditSvc.VerifyTraceIntegrity(traceId)) valid++;
                    else brokenTraces.Add(traceId);
                }
                catch (Exception ex) { brokenTraces.Add($"{traceId} (verify error: {ex.Message})"); }
                // 順便數 events
                var cnt = db.QueryFirst<CountRow>(
                    "SELECT COUNT(*) AS Cnt FROM audit_events WHERE trace_id = @tid",
                    new { tid = traceId });
                totalEvents += cnt?.Cnt ?? 0;
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Audit Chain 完整性驗證報告");
            sb.AppendLine();
            sb.AppendLine($"- **時間窗**: 過去 {windowHours} 小時");
            sb.AppendLine($"- **掃描 trace 數**: {total}");
            sb.AppendLine($"- **總 events**: {totalEvents}");
            sb.AppendLine($"- **完整性 valid**: {valid} / {total}");
            sb.AppendLine($"- **broken traces**: {brokenTraces.Count}");
            sb.AppendLine();
            if (brokenTraces.Count == 0)
            {
                sb.AppendLine("## ✅ 所有 trace hash chain 完整、無篡改跡象");
                sb.AppendLine();
                sb.AppendLine("Benson 設計的 audit_events `previous_event_hash` → `event_hash` 鏈在過去時間窗內完整保持。");
                sb.AppendLine("hash 計算公式：`SHA256(previous_event_hash + trace_id + seq + event_type + payload_digest + occurred_at)`");
            }
            else
            {
                sb.AppendLine("## ⚠️ 偵測到完整性異常");
                foreach (var t in brokenTraces.Take(20))
                    sb.AppendLine($"- `{t}`");
                if (brokenTraces.Count > 20)
                    sb.AppendLine($"- … 還有 {brokenTraces.Count - 20} 條");
            }

            var latencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.Status = "done";
            pending.Reply = sb.ToString();
            pending.Model = "(rule-based, no LLM)";
            pending.LatencyMs = latencyMs;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation(
                "[{Agent}] task={T} verified {V}/{Total} traces ({E} events) · {L}ms",
                AgentIdConst, pending.TaskId, valid, total, totalEvents, latencyMs);
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

    private class MaxSeqRow { public int Seq { get; set; } }
    private class TraceIdRow { public string TraceId { get; set; } = ""; }
    private class CountRow { public int Cnt { get; set; } }
}
