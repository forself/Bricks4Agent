using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 策略研究 agent — 把 StrategyResearchLoopService（LLM 生成參數 → walk-forward 回測 → 迭代）
/// 整條研究員迴圈包成 inbox-driven agent task。
///
/// 重點區別：
///   - 跟其他 agent 不同的是這條**不 auto-push**。一輪研究跑 generations × walk-forward 個回測 +
///     LLM 呼叫，會吃真實的 LLM token 預算。Admin 要主動 push 才會跑。
///   - prompt 一律 JSON: {"symbol":"BTC-USDT","family":"sma_cross","generations":3,"data_limit":300}
///   - 結果 reply 是 markdown 報告 — 最佳候選參數 + IS/OOS Sharpe + 完整世代血緣摘要
///
/// 跟 Benson 規範對齊：
///   - 研究 LLM 呼叫已經走 MeteredLlmProxyService（StrategyGeneratorService.GenerateAsync 內部）
///   - 回測走 IExecutionDispatcher → strategy-worker（broker 控的派發）
///   - 候選血緣存 StrategyCandidateRepository（完整 audit trail）
///   - 結果落 agent_inbox_tasks.reply（給 dashboard 看）
/// </summary>
public class StrategyResearchAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_strategy_research";
    private const string PrincipalIdConst = "prn_agent_strategy_research";
    private const int PollIntervalSeconds = 60;

    private readonly IServiceProvider _sp;
    private readonly ILogger<StrategyResearchAgentService> _logger;

    public StrategyResearchAgentService(IServiceProvider sp, ILogger<StrategyResearchAgentService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        EnsureAgentExists();
        _logger.LogInformation(
            "[{Agent}] started — manual-push only, poll={P}s",
            AgentIdConst, PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnePendingAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "[{Agent}] poll loop iteration failed", AgentIdConst); }

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
            PrincipalId = PrincipalIdConst,
            ActorType = ActorType.AI,
            DisplayName = "Strategy Researcher (on-demand)",
            Status = EntityStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] registered new principal: {P}", AgentIdConst, PrincipalIdConst);
    }

    private async Task ProcessOnePendingAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();

        var pending = db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks
              WHERE agent_id = @aid AND status = 'pending'
              ORDER BY seq ASC LIMIT 1", new { aid = AgentIdConst });
        if (pending == null) return;

        var rows = db.Execute(
            @"UPDATE agent_inbox_tasks SET status = 'processing', started_at = @ts
              WHERE task_id = @tid AND status = 'pending'",
            new { tid = pending.TaskId, ts = DateTime.UtcNow });
        if (rows == 0) return;

        var startMs = DateTime.UtcNow;
        try
        {
            // Parse prompt：必須是 JSON、不像 forensics 容忍純文字（研究跑要明確 symbol/family）
            var (symbol, family, generations, dataLimit) = ParsePrompt(pending.Prompt);

            var loop = scope.ServiceProvider.GetRequiredService<StrategyResearchLoopService>();
            if (!loop.IsEnabled)
            {
                MarkFailed(db, pending, "StrategyResearchLoopService disabled (LLM 或 worker 不可用)");
                return;
            }

            _logger.LogInformation(
                "[{Agent}] task={T} starting research: symbol={S} family={F} gen={G}",
                AgentIdConst, pending.TaskId, symbol, family, generations);

            // 真的跑（LLM × N + 回測 × N × windows）
            var run = await loop.RunAsync(symbol, family, generations, dataLimit, ct);

            var latencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            var report = FormatReport(run);
            var bestModel = run.Best?.LlmModel ?? "";

            pending.Status = run.Status == "completed" ? "done" : "failed";
            pending.Reply = report;
            pending.Model = bestModel;
            pending.LatencyMs = latencyMs;
            pending.CompletedAt = DateTime.UtcNow;
            // eval_tokens 走 LlmProxy 個別記錄、這條 task 是聚合任務、不單獨記
            db.Update(pending);
            _logger.LogInformation(
                "[{Agent}] task={T} done · run={R} candidates={C} · best OOS Sharpe={B:F2} · {L}ms",
                AgentIdConst, pending.TaskId, run.RunId, run.Candidates.Count,
                run.Best?.OutOfSampleSharpe ?? 0m, latencyMs);
        }
        catch (Exception ex)
        {
            MarkFailed(db, pending, ex.Message);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    private static (string Symbol, string Family, int Generations, int DataLimit) ParsePrompt(string prompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;
            var symbol = root.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()!.ToUpperInvariant() : "AAPL";
            var family = root.TryGetProperty("family", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()! : "sma_cross";
            var generations = root.TryGetProperty("generations", out var g) && g.TryGetInt32(out var gi)
                ? Math.Clamp(gi, 1, 10) : 3;
            var dataLimit = root.TryGetProperty("data_limit", out var dl) && dl.TryGetInt32(out var dli)
                ? Math.Clamp(dli, 100, 5000) : 300;
            return (symbol, family, generations, dataLimit);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "prompt 必須是 JSON、格式：{\"symbol\":\"BTC-USDT\",\"family\":\"sma_cross|rsi_oversold\",\"generations\":3,\"data_limit\":300}。" +
                $"解析錯誤：{ex.Message}");
        }
    }

    /// <summary>把 ResearchRun 結果壓成 markdown 報告塞進 inbox.reply</summary>
    private static string FormatReport(ResearchRun run)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 策略研究結果 — {run.Family} on {run.Symbol}");
        sb.AppendLine();
        sb.AppendLine($"- **Run ID**: `{run.RunId}`");
        sb.AppendLine($"- **狀態**: {run.Status}");
        sb.AppendLine($"- **世代數**: {run.Candidates.Count} / {run.TargetGenerations}");
        sb.AppendLine($"- **耗時**: {(run.CompletedAt - run.StartedAt)?.TotalSeconds:F1}s");
        if (!string.IsNullOrEmpty(run.Error))
            sb.AppendLine($"- **錯誤**: {run.Error}");
        sb.AppendLine();

        if (run.Best != null)
        {
            var b = run.Best;
            sb.AppendLine("## 🏆 最佳候選");
            sb.AppendLine();
            sb.AppendLine($"- **參數**: `{string.Join(", ", b.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}`");
            sb.AppendLine($"- **OOS Sharpe**: **{b.OutOfSampleSharpe:F3}** (主要 fitness)");
            sb.AppendLine($"- **IS Sharpe**: {b.InSampleSharpe:F3}");
            sb.AppendLine($"- **OOS 累積報酬**: {b.ReturnPct:F2}%");
            sb.AppendLine($"- **OOS Max DD**: {b.MaxDrawdownPct:F2}%");
            sb.AppendLine($"- **OOS 交易筆數**: {b.Trades}");
            sb.AppendLine($"- **Walk-forward windows**: {b.WindowCount}");
            if (!string.IsNullOrWhiteSpace(b.Rationale))
            {
                sb.AppendLine();
                sb.AppendLine("**LLM 提出理由**:");
                sb.AppendLine("> " + b.Rationale.Replace("\n", "\n> "));
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## ⚠️ 沒有可用候選");
            sb.AppendLine("所有世代均回測失敗或 LLM 解析失敗。");
            sb.AppendLine();
        }

        // 完整世代血緣（精簡）
        sb.AppendLine("## 世代血緣");
        sb.AppendLine();
        sb.AppendLine("| Gen | 參數 | OOS Sharpe | OOS Return % | Trades | 狀態 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in run.Candidates.OrderBy(c => c.Index))
        {
            var paramsStr = c.Parameters.Count > 0
                ? string.Join(",", c.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))
                : "(LLM fail)";
            var status = c.BacktestSuccess ? "✓" : $"✗ {c.BacktestError ?? "—"}";
            sb.AppendLine($"| {c.Index} | `{paramsStr}` | {c.OutOfSampleSharpe:F3} | {c.ReturnPct:F2} | {c.Trades} | {status} |");
        }
        return sb.ToString();
    }

    private static void MarkFailed(BrokerDb db, AgentInboxTask task, string err)
    {
        task.Status = "failed";
        task.Error = err;
        task.CompletedAt = DateTime.UtcNow;
        db.Update(task);
    }
}
