using System.Text.Json;
using Broker.Endpoints;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// Trade Journal agent — manual-push only。給一個 symbol + 時間、agent 拉那段時間的
/// audit / approval / W13 reasoning timeline、用 LLM 組「這筆 trade 為什麼賺/賠 + 學到什麼」報告。
///
/// 跟 ForensicsAgent 差別：
///   - Forensics: 通用事件鏈鑑識、回答「broker 做了什麼」
///   - Journal: 聚焦單筆 trade、回答「為什麼這個結果、下次怎麼改」
///
/// Prompt JSON:
///   {"symbol":"BTC-USDT","time":"2026-05-15T10:00:00Z","window_min":60}
/// </summary>
public class TradeJournalAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_trade_journal";
    private const string PrincipalIdConst = "prn_agent_trade_journal";
    private const int PollIntervalSeconds = 60;

    private readonly IServiceProvider _sp;
    private readonly ILogger<TradeJournalAgentService> _logger;

    public TradeJournalAgentService(IServiceProvider sp, ILogger<TradeJournalAgentService> logger)
    { _sp = sp; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — manual-push only", AgentIdConst);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
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
            DisplayName = "Trade Journal (on-demand)",
            Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow
        });
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
            var (symbol, centerTime, windowMin) = ParsePrompt(pending.Prompt);
            var since = centerTime.AddMinutes(-windowMin);
            var until = centerTime.AddMinutes(windowMin);

            var (timeline, summary, _) = ForensicsEndpoints.BuildTimelineCore(
                db, since, until, traceId: null, symbol: symbol, limit: 100,
                callerPrincipalId: PrincipalIdConst, isAdmin: true);

            var llm = scope.ServiceProvider.GetRequiredService<ILlmProxyService>();
            string finalReply;
            string model = "(no LLM)";
            int evalTokens = 0;

            if (llm.IsEnabled)
            {
                var systemPrompt =
                    "你是 Bricks4Agent 的交易日誌分析師。給你一筆 trade 周圍的 audit timeline（含 strategy 訊號、" +
                    "approval、dispatch、LLM reasoning），你要用繁體中文寫 trade post-mortem：\n" +
                    "1. 重建這筆 trade 的決策鏈（觸發訊號 → gate 通過 → approval → 下單 → 結果）\n" +
                    "2. 標出關鍵時間點（信號出現 / approval pending 多久 / 異常 etc）\n" +
                    "3. 1-2 句「下次可以怎麼改」（如收緊 SL / 跳過某 confidence 閾值 / etc）\n" +
                    "4. 若資料不足、明確說「資料不足以推斷」、不要編造\n\n" +
                    "**Markdown bullet list、簡潔有用、5-10 句**。";
                var lines = timeline.Take(80).Select(e => $"- [{e.Ts:HH:mm:ss}] {e.Type} · {e.Summary}").ToList();
                var userPrompt =
                    $"## Trade focus\nsymbol: `{symbol ?? "(any)"}`\ncenter time: `{centerTime:yyyy-MM-dd HH:mm}` UTC\nwindow: ±{windowMin} min\n\n" +
                    $"## Timeline events (共 {timeline.Count} 筆)\n" +
                    string.Join("\n", lines);

                var llmBody = JsonSerializer.SerializeToElement(new {
                    messages = new object[] {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    },
                    task_id = $"trade_journal_{pending.TaskId}",
                    task_type = "trade_journal_agent"
                });
                var result = await llm.ChatAsync(llmBody, null, ct);
                finalReply = $"# Trade Journal · {symbol ?? "(any)"} · {centerTime:yyyy-MM-dd HH:mm} UTC\n\n" +
                             $"_window: ±{windowMin}min · {timeline.Count} events_\n\n" +
                             result.Content;
                model = result.Model; evalTokens = result.EvalCount;
            }
            else
            {
                finalReply = $"# Trade Journal raw timeline · {timeline.Count} events\n\n" +
                             string.Join("\n", timeline.Take(50).Select(e =>
                                 $"- [{e.Ts:HH:mm:ss}] {e.Type} · {e.Summary}"));
            }

            pending.Status = "done";
            pending.Reply = finalReply;
            pending.Model = model;
            pending.EvalTokens = evalTokens > 0 ? evalTokens : null;
            pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation("[{Agent}] task={T} done · symbol={S} events={E}",
                AgentIdConst, pending.TaskId, symbol ?? "(any)", timeline.Count);
        }
        catch (Exception ex)
        {
            pending.Status = "failed"; pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow; db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    internal static (string? Symbol, DateTime CenterTime, int WindowMin) ParsePrompt(string prompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;
            var symbol = root.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString() : null;
            var centerTime = root.TryGetProperty("time", out var t) && DateTime.TryParse(t.GetString(), out var td)
                ? td.ToUniversalTime() : DateTime.UtcNow;
            var windowMin = root.TryGetProperty("window_min", out var w) && w.TryGetInt32(out var wi)
                ? Math.Clamp(wi, 5, 720) : 60;
            return (symbol, centerTime, windowMin);
        }
        catch
        {
            // 無法 parse、用 default：聚焦現在 ±60min、不限 symbol
            return (null, DateTime.UtcNow, 60);
        }
    }
}
