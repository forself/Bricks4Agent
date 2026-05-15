using System.Text;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 每 6h 報告 broker capability 使用統計、從 audit_events 抽出 DISPATCH_STARTED / API_REQUEST 統計每個
/// capability_id / route 的呼叫次數、誰呼叫的、成功率。
///
/// 補 Benson capability 抽象的 visibility — 平台知道有哪些 capability、但沒人看「實際被用的程度」。
/// 跟 LLM 全景互補（那邊看 LLM 呼叫、這邊看 capability 派發）。
/// </summary>
public class CapabilityUsageReporterAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_capability_usage_reporter";
    private const string PrincipalIdConst = "prn_agent_capability_usage_reporter";
    private const int PollIntervalSeconds = 60;
    private const int AutoPushIntervalSeconds = 6 * 3600;

    private readonly IServiceProvider _sp;
    private readonly ILogger<CapabilityUsageReporterAgentService> _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    public CapabilityUsageReporterAgentService(IServiceProvider sp, ILogger<CapabilityUsageReporterAgentService> logger)
    { _sp = sp; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — every {S}s", AgentIdConst, AutoPushIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if ((DateTime.UtcNow - _lastAutoPushAt).TotalSeconds > AutoPushIntervalSeconds)
                { PushScheduled(); _lastAutoPushAt = DateTime.UtcNow; }
                ProcessOne();
            }
            catch (Exception ex) { _logger.LogError(ex, "[{Agent}] error", AgentIdConst); }
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
            DisplayName = "Capability Usage Reporter (6-hourly)",
            Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow
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
            TaskId = $"inbox_{Guid.NewGuid():N}"[..20], AgentId = AgentIdConst,
            Seq = (maxSeq?.Seq ?? 0) + 1, Prompt = "{\"window_hours\":6}",
            Status = "pending", RequestedBy = $"{nameof(CapabilityUsageReporterAgentService)} (auto)",
            CreatedAt = DateTime.UtcNow
        });
    }

    private void ProcessOne()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
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
            var since = DateTime.UtcNow.AddHours(-6);

            // capability 派發類事件統計
            var dispatchRows = db.Query<EventCountRow>(
                @"SELECT event_type AS EventType, COUNT(*) AS Cnt
                  FROM audit_events
                  WHERE occurred_at >= @since AND event_type LIKE '%DISPATCH%'
                  GROUP BY event_type ORDER BY Cnt DESC",
                new { since });

            // API request by path（前 15 個）
            var apiRows = db.Query<PathCountRow>(
                @"SELECT COALESCE(resource_ref, '') AS Path, COUNT(*) AS Cnt
                  FROM audit_events
                  WHERE occurred_at >= @since AND event_type = 'API_REQUEST' AND resource_ref IS NOT NULL
                  GROUP BY resource_ref ORDER BY Cnt DESC LIMIT 15",
                new { since });

            // principal usage（誰呼叫）
            var principalRows = db.Query<PrincipalCountRow>(
                @"SELECT COALESCE(principal_id, '(none)') AS Principal, COUNT(*) AS Cnt
                  FROM audit_events
                  WHERE occurred_at >= @since
                  GROUP BY principal_id ORDER BY Cnt DESC LIMIT 10",
                new { since });

            var sb = new StringBuilder();
            sb.AppendLine($"# Capability 使用統計 · 過去 6h");
            sb.AppendLine($"`{since:yyyy-MM-dd HH:mm}` ~ `{DateTime.UtcNow:yyyy-MM-dd HH:mm}` UTC");
            sb.AppendLine();
            sb.AppendLine("## Dispatch event 分佈");
            sb.AppendLine();
            sb.AppendLine("| Event Type | Count |");
            sb.AppendLine("|---|---|");
            foreach (var r in dispatchRows) sb.AppendLine($"| `{r.EventType}` | {r.Cnt} |");
            if (dispatchRows.Count == 0) sb.AppendLine("| (no dispatch events in window) | — |");
            sb.AppendLine();
            sb.AppendLine("## Top API paths");
            sb.AppendLine();
            sb.AppendLine("| Path | Hits |");
            sb.AppendLine("|---|---|");
            foreach (var r in apiRows) sb.AppendLine($"| `{r.Path}` | {r.Cnt} |");
            sb.AppendLine();
            sb.AppendLine("## Top principals (誰呼叫的)");
            sb.AppendLine();
            sb.AppendLine("| Principal | Calls |");
            sb.AppendLine("|---|---|");
            foreach (var r in principalRows) sb.AppendLine($"| `{r.Principal}` | {r.Cnt} |");

            pending.Status = "done";
            pending.Reply = sb.ToString();
            pending.Model = "(rule-based, no LLM)";
            pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation("[{Agent}] task={T} done · dispatch_types={D} top_paths={P}",
                AgentIdConst, pending.TaskId, dispatchRows.Count, apiRows.Count);
        }
        catch (Exception ex)
        {
            pending.Status = "failed"; pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow; db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    private class MaxSeqRow { public int Seq { get; set; } }
    private class EventCountRow { public string EventType { get; set; } = ""; public int Cnt { get; set; } }
    private class PathCountRow { public string Path { get; set; } = ""; public int Cnt { get; set; } }
    private class PrincipalCountRow { public string Principal { get; set; } = ""; public int Cnt { get; set; } }
}
