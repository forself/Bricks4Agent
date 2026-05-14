using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// 每日市場日報 agent — 每 24h 自動產一份「我的交易帳戶今天怎麼樣」報告。
///
/// 跟其他 forensics agent 的差別：
///   - 資料源不是 audit 表、是當前運行狀態（AutoTraderService + BalanceAnchorService）
///   - 內容是「市場 / 帳戶 / 持倉」的 snapshot、不是事件鏈
///
/// 推給 user 看的東西：anchor / live balance / 監控標的數 / 開倉部位 / 過去 24h 主要訊號。
/// LLM 把這堆數字壓成自然語言 markdown 日報。
/// </summary>
public class DailyMarketReportAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_daily_market_report";
    private const string PrincipalIdConst = "prn_agent_daily_market_report";
    private const int PollIntervalSeconds = 60;
    private const int AutoPushIntervalSeconds = 24 * 3600;

    private readonly IServiceProvider _sp;
    private readonly ILogger<DailyMarketReportAgentService> _logger;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    public DailyMarketReportAgentService(IServiceProvider sp, ILogger<DailyMarketReportAgentService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — poll={P}s, daily auto-push", AgentIdConst, PollIntervalSeconds);

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
            DisplayName = "Daily Market Report", Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] registered principal", AgentIdConst);
    }

    private void PushScheduled()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
        var prompt = "{\"trigger\":\"daily-auto\"}";
        var maxSeq = db.QueryFirst<MaxSeqRow>(
            "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
            new { aid = AgentIdConst });
        db.Insert(new AgentInboxTask
        {
            TaskId = $"inbox_{Guid.NewGuid():N}"[..20], AgentId = AgentIdConst,
            Seq = (maxSeq?.Seq ?? 0) + 1, Prompt = prompt, Status = "pending",
            RequestedBy = $"{nameof(DailyMarketReportAgentService)} (auto)", CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] auto-pushed daily task", AgentIdConst);
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
            var anchor = scope.ServiceProvider.GetRequiredService<BalanceAnchorService>();
            var llm = scope.ServiceProvider.GetRequiredService<ILlmProxyService>();

            var snapshot = BuildSnapshot(autoTrader, anchor);
            if (!llm.IsEnabled)
            {
                // LLM 沒開、至少落 raw snapshot
                pending.Reply = snapshot;
                pending.Status = "done";
                pending.Model = "(no LLM)";
                pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
                pending.CompletedAt = DateTime.UtcNow;
                db.Update(pending);
                return;
            }

            var (systemPrompt, userPrompt) = BuildPrompts(snapshot);
            var llmBody = JsonSerializer.SerializeToElement(new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                task_id = $"daily_market_report_{pending.TaskId}",
                task_type = "daily_market_report_agent"
            });
            var result = await llm.ChatAsync(llmBody, null, ct);
            var latencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;

            pending.Status = "done";
            pending.Reply = result.Content + "\n\n---\n\n## 原始 snapshot\n\n" + snapshot;
            pending.Model = result.Model;
            pending.EvalTokens = result.EvalCount;
            pending.LatencyMs = latencyMs;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation("[{Agent}] task={T} done · {L}ms", AgentIdConst, pending.TaskId, latencyMs);
        }
        catch (Exception ex)
        {
            pending.Status = "failed"; pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow; db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    /// <summary>把帳戶 + 監控 + 持倉壓成 markdown snapshot；prompt 跟 reply 都會用到</summary>
    private static string BuildSnapshot(AutoTraderService autoTrader, BalanceAnchorService anchor)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## 帳戶 (anchor 服務)");
        var anchorState = anchor.GetState("bingx");
        if (anchorState != null)
        {
            sb.AppendLine($"- BingX anchor: **{anchorState.CurrentAnchor:F2} USDT**");
            sb.AppendLine($"- 最新餘額: **{anchorState.LastSeenBalance:F2} USDT**");
            var delta = anchorState.CurrentAnchor > 0
                ? (anchorState.LastSeenBalance - anchorState.CurrentAnchor) / anchorState.CurrentAnchor * 100m
                : 0m;
            sb.AppendLine($"- 相對 anchor: {(delta >= 0 ? "+" : "")}{delta:F2}%");
            sb.AppendLine($"- 最後 anchor 變動: {anchorState.LastChangeAt:yyyy-MM-dd HH:mm} UTC ({anchorState.LastChangeReason})");
        }
        else
        {
            sb.AppendLine("- (no anchor state)");
        }

        sb.AppendLine();
        sb.AppendLine($"## AutoTrader 狀態");
        sb.AppendLine($"- 啟用: {(autoTrader.IsEnabled ? "✓" : "✗")}");
        sb.AppendLine($"- 監控標的: {autoTrader.WatchList.Count}");
        sb.AppendLine($"- 永續部位: {autoTrader.PerpetualPositionStates.Count}");

        if (autoTrader.WatchList.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## 監控清單");
            sb.AppendLine("| Symbol | Exch | Mode | Strategy | Last Signal | Conf | Last Check |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var (_, w) in autoTrader.WatchList.Take(20))
            {
                var lastCheck = w.LastCheck.HasValue
                    ? $"{(DateTime.UtcNow - w.LastCheck.Value).TotalMinutes:F0}m ago"
                    : "—";
                sb.AppendLine($"| {w.Symbol} | {w.Exchange} | {w.Mode} | {w.Strategy} | {w.LastSignal ?? "—"} | {w.LastConfidence:P0} | {lastCheck} |");
            }
        }

        if (autoTrader.PerpetualPositionStates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## 開倉中永續部位");
            sb.AppendLine("| Symbol | Side | Entry | Peak | SL | Liq | Lev |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var (_, p) in autoTrader.PerpetualPositionStates)
            {
                sb.AppendLine($"| {p.Symbol} | {p.Side} | {p.EntryPrice:F4} | {p.PeakMark:F4} | {p.SlPrice:F4} | {p.LiquidationPrice:F4} | {p.Leverage}x |");
            }
        }
        return sb.ToString();
    }

    private static (string, string) BuildPrompts(string snapshot)
    {
        var systemPrompt =
            "你是 Bricks4Agent 平台的「每日市場日報」助手。給你當前帳戶 + 監控 + 持倉的 markdown snapshot、" +
            "你要：\n" +
            "1. 用 3-5 句話描述帳戶當前狀態（anchor / 餘額 / 相對表現）\n" +
            "2. 描述今日監控標的（多少個、有沒有特別 active 的訊號）\n" +
            "3. 描述開倉持倉（如有）：哪些 symbol、方向、距離 SL/Liq 多遠\n" +
            "4. 整體判斷：穩定 / 注意 / 警示（一句話）\n\n" +
            "**用繁體中文、markdown 格式、簡潔不冗長**。不要重複資料本身（snapshot 會附在你的回覆下方）、" +
            "聚焦在 **解讀**。";
        var userPrompt = $"以下是今日 snapshot:\n\n{snapshot}";
        return (systemPrompt, userPrompt);
    }

    private class MaxSeqRow { public int Seq { get; set; } }
}
