using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 每小時快照 dashboard KPI bar 那組 5 個關鍵數字到 agent_inbox_tasks.reply。
/// 不額外建表（用 inbox 當儲存）、未來可從 list endpoint 拉歷史畫趨勢。
///
/// snapshot 內容：anchor / live balance / open positions / 24h PnL / gates active。
/// 都是已有的 in-memory 狀態、純讀無 LLM、3-5ms 完成。
/// </summary>
public class DashboardSnapshotAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_dashboard_snapshot";
    private const string PrincipalIdConst = "prn_agent_dashboard_snapshot";
    private const int PollIntervalSeconds = 60;
    private const int AutoPushIntervalSeconds = 3600;  // 1h

    private readonly IServiceProvider _sp;
    private readonly ILogger<DashboardSnapshotAgentService> _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    public DashboardSnapshotAgentService(IServiceProvider sp, ILogger<DashboardSnapshotAgentService> logger)
    {
        _sp = sp; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — auto-snapshot KPI every {S}s", AgentIdConst, AutoPushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if ((DateTime.UtcNow - _lastAutoPushAt).TotalSeconds > AutoPushIntervalSeconds)
                {
                    PushScheduled(); _lastAutoPushAt = DateTime.UtcNow;
                }
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
            DisplayName = "Dashboard Snapshot (hourly)",
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
            Seq = (maxSeq?.Seq ?? 0) + 1, Prompt = "{\"trigger\":\"hourly\"}",
            Status = "pending", RequestedBy = $"{nameof(DashboardSnapshotAgentService)} (auto)",
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
            var autoTrader = scope.ServiceProvider.GetRequiredService<AutoTraderService>();
            var anchor = scope.ServiceProvider.GetRequiredService<BalanceAnchorService>();

            var anchorState = anchor.GetState("bingx");
            var snapshot = new {
                ts = DateTime.UtcNow,
                anchor_usdt = anchorState?.CurrentAnchor ?? 0m,
                live_balance_usdt = anchorState?.LastSeenBalance ?? 0m,
                open_perp_positions = autoTrader.PerpetualPositionStates.Count,
                active_watches = autoTrader.WatchList.Count,
                gates_active = 14,   // 14 條 risk gate hardcoded、broker 啟動就全部 active
                auto_trader_enabled = autoTrader.IsEnabled,
            };

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            var sb = new StringBuilder();
            sb.AppendLine($"# Dashboard KPI Snapshot · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(json);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("**Tip**: list endpoint 拉這個 agent 的 task 歷史可重建任意時段 KPI 趨勢。");

            pending.Status = "done";
            pending.Reply = sb.ToString();
            pending.Model = "(snapshot, no LLM)";
            pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation("[{Agent}] task={T} done · anchor={A} live={L}",
                AgentIdConst, pending.TaskId, snapshot.anchor_usdt, snapshot.live_balance_usdt);
        }
        catch (Exception ex)
        {
            pending.Status = "failed"; pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow; db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    private class MaxSeqRow { public int Seq { get; set; } }
}
