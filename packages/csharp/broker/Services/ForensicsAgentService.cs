using System.Text.Json;
using Broker.Endpoints;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// ForensicsAgentService —— Bricks4Agent 平台第一個真正用 agent 抽象的 BackgroundService。
///
/// 為什麼存在：Agents tab + agent_inbox_tasks 表是 Benson 原本設計的「broker-managed AI worker」
/// 抽象、但 trading scenario 沒有對應 use case 沒被用起來。這個 service 給 agent abstraction
/// 一個有意義的真實任務：每小時自動產一份過去 1 小時的 broker 鑑識報告（pulled from audit
/// + approval + W13 reasoning timeline），結果落 agent_inbox_tasks.reply。
///
/// 流程跟 inbox 設計一致（不抄捷徑）：
///   1. 啟動：確保 principal `prn_agent_forensics_hourly` 存在
///   2. 每分鐘 poll：拉一筆 `agent_forensics_hourly` 名下的 pending task
///   3. 抓到 task → 解析 prompt（JSON 或 plain text）→ ForensicsEndpoints.BuildTimelineCore
///      → BuildLlmPrompts → ILlmProxyService.ChatAsync → 寫回 task.Reply + Status='done'
///   4. 每小時 push 自身一筆「總結過去 1 小時」task（cron-like）
///
/// 對 Benson 的價值：
///   - 給 agent 抽象一個真實使用案例、明天 demo 可以指 Agents tab 說「有個 forensics agent 在跑」
///   - agent 跑的 LLM 自己也走 LlmProxy、進 MeteredLlmProxyService → LLM 全景表
///   - propose / validate / record 三段都在 broker 內、bot-node 沒參與
/// </summary>
public class ForensicsAgentService : BackgroundService
{
    public const string AgentId = "agent_forensics_hourly";
    public const string PrincipalId = "prn_agent_forensics_hourly";
    private const int PollIntervalSeconds = 60;
    private const int HourlyAutoPushIntervalSeconds = 3600;

    private readonly IServiceProvider _sp;
    private readonly ILogger<ForensicsAgentService> _logger;
    private DateTime _lastHourlyPushAt = DateTime.MinValue;

    public ForensicsAgentService(IServiceProvider sp, ILogger<ForensicsAgentService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 啟動延遲讓 broker 其他服務先就緒
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        EnsureAgentExists();
        _logger.LogInformation(
            "[ForensicsAgent] started — polling inbox every {S}s, hourly auto-push every {H}s",
            PollIntervalSeconds, HourlyAutoPushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Auto-push 每小時：時間到就寫一筆新 task 到自己 inbox
                if ((DateTime.UtcNow - _lastHourlyPushAt).TotalSeconds > HourlyAutoPushIntervalSeconds)
                {
                    PushHourlyTask();
                    _lastHourlyPushAt = DateTime.UtcNow;
                }

                // 拉一筆處理（每次只處理一筆、避免 LLM 排隊壅塞）
                await ProcessOnePendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ForensicsAgent] poll loop iteration failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[ForensicsAgent] stopped");
    }

    private void EnsureAgentExists()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        if (db.Get<Principal>(PrincipalId) != null) return;

        db.Insert(new Principal
        {
            PrincipalId = PrincipalId,
            ActorType = ActorType.AI,
            DisplayName = "Forensics Investigator (hourly)",
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[ForensicsAgent] registered new principal: {P}", PrincipalId);
    }

    private void PushHourlyTask()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();

        var since = DateTime.UtcNow.AddHours(-1).ToString("o");
        var until = DateTime.UtcNow.ToString("o");
        var prompt = JsonSerializer.Serialize(new
        {
            since,
            until,
            question = "請說明過去 1 小時 broker 做了什麼、有哪些 gate 觸發、是否有異常"
        });

        var maxSeq = db.QueryFirst<MaxSeqRow>(
            "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
            new { aid = AgentId });

        var task = new AgentInboxTask
        {
            TaskId = $"inbox_{Guid.NewGuid():N}"[..20],
            AgentId = AgentId,
            Seq = (maxSeq?.Seq ?? 0) + 1,
            Prompt = prompt,
            Status = "pending",
            RequestedBy = "ForensicsAgentService (auto-hourly)",
            CreatedAt = DateTime.UtcNow
        };
        db.Insert(task);
        _logger.LogInformation("[ForensicsAgent] auto-pushed hourly task seq={S} id={T}", task.Seq, task.TaskId);
    }

    private async Task ProcessOnePendingAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        var llm = scope.ServiceProvider.GetRequiredService<ILlmProxyService>();

        // 1. atomic pull
        var pending = db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks
              WHERE agent_id = @aid AND status = 'pending'
              ORDER BY seq ASC LIMIT 1", new { aid = AgentId });
        if (pending == null) return;

        var rows = db.Execute(
            @"UPDATE agent_inbox_tasks SET status = 'processing', started_at = @ts
              WHERE task_id = @tid AND status = 'pending'",
            new { tid = pending.TaskId, ts = DateTime.UtcNow });
        if (rows == 0) return;   // 別人搶到了

        if (!llm.IsEnabled)
        {
            MarkFailed(db, pending, "LlmProxy disabled");
            return;
        }

        var startMs = DateTime.UtcNow;
        try
        {
            // 2. parse prompt
            var (since, until, symbol, question) = ParsePrompt(pending.Prompt);

            // 3. timeline aggregation（system scope = isAdmin=true 看全部）
            var (timeline, statSummary, _) = ForensicsEndpoints.BuildTimelineCore(
                db, since, until, traceId: null, symbol, limit: 80,
                callerPrincipalId: PrincipalId, isAdmin: true);

            // 4. LLM compose narrative via broker LlmProxy（合規重點：agent 自己不會偷打外部）
            var (systemPrompt, userPrompt) = ForensicsEndpoints.BuildLlmPrompts(timeline, statSummary, question);
            var llmBody = JsonSerializer.SerializeToElement(new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                task_id = $"forensics_agent_{pending.TaskId}",
                task_type = "forensics_agent_scheduled"
            });

            var result = await llm.ChatAsync(llmBody, null, ct);
            var latencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;

            pending.Status = "done";
            pending.Reply = result.Content;
            pending.Model = result.Model;
            pending.EvalTokens = result.EvalCount;
            pending.LatencyMs = latencyMs;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation(
                "[ForensicsAgent] task={T} done · events={E} · {L}ms · model={M}",
                pending.TaskId, statSummary.AuditCount + statSummary.ApprovalCount + statSummary.LlmCount,
                latencyMs, result.Model);
        }
        catch (Exception ex)
        {
            MarkFailed(db, pending, ex.Message);
            _logger.LogError(ex, "[ForensicsAgent] task={T} failed", pending.TaskId);
        }
    }

    private static void MarkFailed(BrokerDb db, AgentInboxTask task, string err)
    {
        task.Status = "failed";
        task.Error = err;
        task.CompletedAt = DateTime.UtcNow;
        db.Update(task);
    }

    private static (DateTime since, DateTime until, string? symbol, string question) ParsePrompt(string prompt)
    {
        // 容忍 JSON 跟純文字兩種 prompt
        try
        {
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;
            var since = root.TryGetProperty("since", out var s) && DateTime.TryParse(s.GetString(), out var sd)
                ? sd.ToUniversalTime() : DateTime.UtcNow.AddHours(-1);
            var until = root.TryGetProperty("until", out var u) && DateTime.TryParse(u.GetString(), out var ud)
                ? ud.ToUniversalTime() : DateTime.UtcNow;
            var symbol = root.TryGetProperty("symbol", out var sym) ? sym.GetString() : null;
            var question = root.TryGetProperty("question", out var q) ? q.GetString() ?? "" : "";
            return (since, until, symbol, question);
        }
        catch
        {
            // plain text fallback：把整段當問題、time window 預設 1h
            return (DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, null, prompt);
        }
    }

    private class MaxSeqRow
    {
        public int Seq { get; set; }
    }
}
