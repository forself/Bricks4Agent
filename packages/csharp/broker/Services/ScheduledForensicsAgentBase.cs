using System.Text.Json;
using Broker.Endpoints;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// 排程鑑識 agent 的共用 BackgroundService 基底。
///
/// 子類只需要提供常數（AgentId / PrincipalId / DisplayName / AutoPushIntervalSeconds /
/// DefaultWindow / DefaultQuestion / TaskType）就能跑出一個獨立的 inbox-driven agent。
///
/// 共用邏輯：
///   1. 啟動：確保 Principal 存在
///   2. 主迴圈：每 PollIntervalSeconds 跑一輪
///   3. Auto-push：若距離上次 push 超過 AutoPushIntervalSeconds、寫一筆 JSON prompt
///   4. Atomic pull：拉一筆 pending task、UPDATE 鎖 processing 狀態
///   5. 處理：ForensicsEndpoints.BuildTimelineCore + BuildLlmPrompts + ChatAsync
///   6. 寫回 Reply / Model / EvalTokens / LatencyMs / CompletedAt
///
/// 所有 agent 的 LLM 呼叫都走 broker LlmProxy（MeteredLlmProxyService）— 沒有 client-side
/// LLM、沒有外部 API direct call、全部進 LLM 全景觀測。
/// </summary>
public abstract class ScheduledForensicsAgentBase : BackgroundService
{
    protected abstract string AgentId { get; }
    protected abstract string PrincipalId { get; }
    protected abstract string DisplayName { get; }
    protected abstract int AutoPushIntervalSeconds { get; }
    protected abstract TimeSpan DefaultWindow { get; }
    protected abstract string DefaultQuestion { get; }
    protected abstract string TaskType { get; }
    protected virtual int PollIntervalSeconds => 60;
    protected virtual int StartupDelaySeconds => 10;

    private readonly IServiceProvider _sp;
    private readonly ILogger _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    protected ScheduledForensicsAgentBase(IServiceProvider sp, ILogger logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Principal 立刻 register（不等 StartupDelay）；不然啟動瞬間 push 會找不到 agent
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), stoppingToken);

        _logger.LogInformation(
            "[{Agent}] started — poll={P}s, auto-push={A}s, window={W}",
            AgentId, PollIntervalSeconds, AutoPushIntervalSeconds, DefaultWindow);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if ((DateTime.UtcNow - _lastAutoPushAt).TotalSeconds > AutoPushIntervalSeconds)
                {
                    PushScheduledTask();
                    _lastAutoPushAt = DateTime.UtcNow;
                }

                await ProcessOnePendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Agent}] poll loop iteration failed", AgentId);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[{Agent}] stopped", AgentId);
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
            DisplayName = DisplayName,
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] registered new principal: {P}", AgentId, PrincipalId);
    }

    private void PushScheduledTask()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();

        var until = DateTime.UtcNow;
        var since = until.Subtract(DefaultWindow);
        var prompt = JsonSerializer.Serialize(new
        {
            since = since.ToString("o"),
            until = until.ToString("o"),
            question = DefaultQuestion
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
            RequestedBy = $"{GetType().Name} (auto)",
            CreatedAt = DateTime.UtcNow
        };
        db.Insert(task);
        _logger.LogInformation("[{Agent}] auto-pushed task seq={S} id={T}", AgentId, task.Seq, task.TaskId);
    }

    private async Task ProcessOnePendingAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        var llm = scope.ServiceProvider.GetRequiredService<ILlmProxyService>();

        var pending = db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks
              WHERE agent_id = @aid AND status = 'pending'
              ORDER BY seq ASC LIMIT 1", new { aid = AgentId });
        if (pending == null) return;

        var rows = db.Execute(
            @"UPDATE agent_inbox_tasks SET status = 'processing', started_at = @ts
              WHERE task_id = @tid AND status = 'pending'",
            new { tid = pending.TaskId, ts = DateTime.UtcNow });
        if (rows == 0) return;

        if (!llm.IsEnabled)
        {
            MarkFailed(db, pending, "LlmProxy disabled");
            return;
        }

        var startMs = DateTime.UtcNow;
        try
        {
            var (since, until, symbol, question) = ParsePrompt(pending.Prompt);

            var (timeline, statSummary, _) = ForensicsEndpoints.BuildTimelineCore(
                db, since, until, traceId: null, symbol, limit: 80,
                callerPrincipalId: PrincipalId, isAdmin: true);

            var (systemPrompt, userPrompt) = ForensicsEndpoints.BuildLlmPrompts(timeline, statSummary, question);
            var llmBody = JsonSerializer.SerializeToElement(new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                task_id = $"{TaskType}_{pending.TaskId}",
                task_type = TaskType
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
                "[{Agent}] task={T} done · events={E} · {L}ms · model={M}",
                AgentId, pending.TaskId,
                statSummary.AuditCount + statSummary.ApprovalCount + statSummary.LlmCount,
                latencyMs, result.Model);
        }
        catch (Exception ex)
        {
            MarkFailed(db, pending, ex.Message);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentId, pending.TaskId);
        }
    }

    private static void MarkFailed(BrokerDb db, AgentInboxTask task, string err)
    {
        task.Status = "failed";
        task.Error = err;
        task.CompletedAt = DateTime.UtcNow;
        db.Update(task);
    }

    internal (DateTime since, DateTime until, string? symbol, string question) ParsePrompt(string prompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;
            var since = root.TryGetProperty("since", out var s) && DateTime.TryParse(s.GetString(), out var sd)
                ? sd.ToUniversalTime() : DateTime.UtcNow.Subtract(DefaultWindow);
            var until = root.TryGetProperty("until", out var u) && DateTime.TryParse(u.GetString(), out var ud)
                ? ud.ToUniversalTime() : DateTime.UtcNow;
            var symbol = root.TryGetProperty("symbol", out var sym) ? sym.GetString() : null;
            var question = root.TryGetProperty("question", out var q) ? q.GetString() ?? DefaultQuestion : DefaultQuestion;
            return (since, until, symbol, question);
        }
        catch
        {
            return (DateTime.UtcNow.Subtract(DefaultWindow), DateTime.UtcNow, null, prompt);
        }
    }

    private class MaxSeqRow
    {
        public int Seq { get; set; }
    }
}
