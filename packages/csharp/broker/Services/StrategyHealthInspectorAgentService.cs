using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// 策略健康巡檢 agent — 每 6h 掃所有 active watch、檢查訊號頻率 / signal staleness / 上次出現訊號的方向。
///
/// 跟 StrategyHealthMonitor (BackgroundService) 的差別：
///   - StrategyHealthMonitor 是 hard-rule（連虧 5 筆或 winrate<30% 自動 pause）、無 LLM
///   - 這個 agent 是 LLM-narrated 健康巡檢、會解釋「為什麼這個 watch 看起來不對」
///   - 兩者互補：rule-based 攔災難、LLM-narrated 看細微異常
/// </summary>
public class StrategyHealthInspectorAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_strategy_health_inspector";
    private const string PrincipalIdConst = "prn_agent_strategy_health_inspector";
    private const int PollIntervalSeconds = 60;
    private const int AutoPushIntervalSeconds = 6 * 3600;

    private readonly IServiceProvider _sp;
    private readonly ILogger<StrategyHealthInspectorAgentService> _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    public StrategyHealthInspectorAgentService(IServiceProvider sp, ILogger<StrategyHealthInspectorAgentService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — poll={P}s, every 6h auto-push", AgentIdConst, PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if ((DateTime.UtcNow - _lastAutoPushAt).TotalSeconds > AutoPushIntervalSeconds)
                {
                    PushScheduled();
                    _lastAutoPushAt = DateTime.UtcNow;
                }
                await ProcessOneAsync(stoppingToken);
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
            DisplayName = "Strategy Health Inspector", Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow
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
            Seq = (maxSeq?.Seq ?? 0) + 1, Prompt = "{\"trigger\":\"6h-auto\"}",
            Status = "pending", RequestedBy = $"{nameof(StrategyHealthInspectorAgentService)} (auto)",
            CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] auto-pushed task", AgentIdConst);
    }

    private async Task ProcessOneAsync(CancellationToken ct)
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
            var llm = scope.ServiceProvider.GetRequiredService<ILlmProxyService>();

            var (snapshot, anomalies) = BuildHealthSnapshot(autoTrader);
            if (!llm.IsEnabled)
            {
                pending.Reply = snapshot;
                pending.Status = "done";
                pending.Model = "(no LLM)";
                pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
                pending.CompletedAt = DateTime.UtcNow;
                db.Update(pending);
                return;
            }

            var systemPrompt =
                "你是 Bricks4Agent 平台的策略健康巡檢員。給你所有監控標的的當前狀態 + 已偵測異常清單、" +
                "你要：\n" +
                "1. 用 markdown 列出每個異常的 watch、解釋可能原因\n" +
                "2. 若沒有異常、直接說「✓ 所有 watch 健康」、不要編造\n" +
                "3. 給 1 個具體下一步建議（如「重新 backtest」「pause 某個 watch」「等更多訊號」）\n\n" +
                "**繁體中文、簡潔、不要重複 raw data**。";
            var userPrompt =
                $"## 監控清單 snapshot\n{snapshot}\n\n" +
                $"## 偵測到的異常（{anomalies.Count} 筆）\n" +
                (anomalies.Count > 0 ? string.Join("\n", anomalies.Select(a => "- " + a)) : "（無）");

            var llmBody = JsonSerializer.SerializeToElement(new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                task_id = $"strategy_health_{pending.TaskId}",
                task_type = "strategy_health_inspector_agent"
            });
            var result = await llm.ChatAsync(llmBody, null, ct);
            var latencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;

            pending.Status = "done";
            pending.Reply = result.Content + "\n\n---\n\n" + snapshot;
            pending.Model = result.Model;
            pending.EvalTokens = result.EvalCount;
            pending.LatencyMs = latencyMs;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation(
                "[{Agent}] task={T} done · anomalies={A} · {L}ms",
                AgentIdConst, pending.TaskId, anomalies.Count, latencyMs);
        }
        catch (Exception ex)
        {
            pending.Status = "failed"; pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow; db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    /// <summary>掃 WatchList、回 (markdown snapshot, anomalies)</summary>
    internal static (string Snapshot, List<string> Anomalies) BuildHealthSnapshot(AutoTraderService autoTrader)
    {
        var sb = new StringBuilder();
        var anomalies = new List<string>();

        sb.AppendLine("| Symbol | Active | Strategy | Last Signal | Conf | Last Check | Staleness |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        var now = DateTime.UtcNow;
        foreach (var (_, w) in autoTrader.WatchList)
        {
            string staleness;
            if (w.LastCheck.HasValue)
            {
                var minsAgo = (now - w.LastCheck.Value).TotalMinutes;
                staleness = $"{minsAgo:F0}m";
                // 異常 1：active 但 60 分鐘沒檢查過
                if (w.Active && minsAgo > 60)
                    anomalies.Add($"`{w.Symbol}` ({w.Exchange}) Active 但已 {minsAgo:F0} 分鐘沒檢查、可能 worker 沒在跑");
            }
            else
            {
                staleness = "never";
                if (w.Active) anomalies.Add($"`{w.Symbol}` ({w.Exchange}) Active 但從未被檢查過、可能剛 spawn 還沒第一輪 sweep");
            }

            // 異常 2：active 但訊號是 HOLD 且 confidence < 20%
            if (w.Active && w.LastSignal == "HOLD" && w.LastConfidence < 0.20m)
                anomalies.Add($"`{w.Symbol}` 持續 HOLD 訊號 + 極低信心 ({w.LastConfidence:P0})、策略可能不適合當前 regime");

            // 異常 3：confidence 為 0（可能訊號計算失敗）
            if (w.Active && w.LastConfidence == 0m && w.LastSignal != null)
                anomalies.Add($"`{w.Symbol}` 訊號={w.LastSignal} 但 confidence=0、可能策略算錯");

            sb.AppendLine($"| {w.Symbol} | {(w.Active ? "✓" : "✗")} | {w.Strategy} | {w.LastSignal ?? "—"} | {w.LastConfidence:P0} | {(w.LastCheck?.ToString("HH:mm") ?? "—")} | {staleness} |");
        }

        if (autoTrader.WatchList.Count == 0)
            sb.AppendLine("| (no watches) |  |  |  |  |  |  |");

        return (sb.ToString(), anomalies);
    }

    private class MaxSeqRow { public int Seq { get; set; } }
}
