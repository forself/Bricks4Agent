using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// 風控壓測 agent — manual-push only。
///
/// 設想 scenario、對當前永續持倉做 what-if 模擬、回每倉預估損益 + 是否會強平。
///
/// Prompt JSON 範例：
///   { "drop_pct": 10 }                 → 全部 mark price 下跌 10%
///   { "drop_pct": -5 }                 → 全部 mark price 上漲 5%（負值）
///   { "symbol_drops": {"BTC-USDT": 10, "ETH-USDT": 15} }   → per-symbol
///
/// 沒走 strategy.signal、純算術——broker 端拿 AutoTraderService 的 PerpetualPositionState
/// 直接用 entry / leverage / sl / liquidation_price 算出新 PnL 跟風險變化。
///
/// 為什麼這支也走 LlmProxy：raw 數字 user 看不出意義、LLM 把它包成「壞情況評估報告」更可讀。
/// </summary>
public class RiskStressTestAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_risk_stress_test";
    private const string PrincipalIdConst = "prn_agent_risk_stress_test";
    private const int PollIntervalSeconds = 60;

    private readonly IServiceProvider _sp;
    private readonly ILogger<RiskStressTestAgentService> _logger;

    public RiskStressTestAgentService(IServiceProvider sp, ILogger<RiskStressTestAgentService> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureAgentExists();
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        _logger.LogInformation("[{Agent}] started — manual-push only, poll={P}s", AgentIdConst, PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
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
            DisplayName = "Risk Stress Tester (on-demand)", Status = EntityStatus.Active, CreatedAt = DateTime.UtcNow
        });
        _logger.LogInformation("[{Agent}] registered principal", AgentIdConst);
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
            var (uniformDrop, perSymbolDrops) = ParsePrompt(pending.Prompt);
            var autoTrader = scope.ServiceProvider.GetRequiredService<AutoTraderService>();
            var report = BuildStressReport(autoTrader, uniformDrop, perSymbolDrops);

            // 附帶 LLM 評語（只在有部位時值得 LLM）
            var llm = scope.ServiceProvider.GetRequiredService<ILlmProxyService>();
            string finalReply;
            string model = "(no LLM)";
            int evalTokens = 0;
            if (llm.IsEnabled && autoTrader.PerpetualPositionStates.Count > 0)
            {
                var systemPrompt =
                    "你是 Bricks4Agent 平台的風控分析師。給你一份壓測情境下每倉預估損益 + 強平距離報告、" +
                    "你要：\n" +
                    "1. 一句話總結整個帳戶風險（穩定 / 留意 / 嚴重）\n" +
                    "2. 列出最危險的 1-3 個部位、為什麼（接近 liq、損益 -X%、etc）\n" +
                    "3. 給 1-2 個具體緩解建議（reduce / move SL / hedge）\n\n" +
                    "**繁體中文、3-5 行、精簡有用**。";
                var userPrompt = $"## 壓測報告\n{report}";
                var llmBody = JsonSerializer.SerializeToElement(new
                {
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    },
                    task_id = $"risk_stress_{pending.TaskId}",
                    task_type = "risk_stress_test_agent"
                });
                var result = await llm.ChatAsync(llmBody, null, ct);
                finalReply = "## 風控分析師評語\n\n" + result.Content + "\n\n---\n\n" + report;
                model = result.Model;
                evalTokens = result.EvalCount;
            }
            else
            {
                finalReply = report;
            }

            var latencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.Status = "done";
            pending.Reply = finalReply;
            pending.Model = model;
            pending.EvalTokens = evalTokens > 0 ? evalTokens : null;
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

    /// <summary>
    /// 從 prompt JSON 抽 scenario。
    /// 回 (uniformDropPct, perSymbolDrops)：兩者擇一、uniform 是 fallback。
    /// </summary>
    private static (decimal? UniformDrop, Dictionary<string, decimal> PerSymbol) ParsePrompt(string prompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;

            if (root.TryGetProperty("symbol_drops", out var sd) && sd.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in sd.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Number)
                        dict[p.Name] = p.Value.GetDecimal();
                }
                return (null, dict);
            }

            if (root.TryGetProperty("drop_pct", out var dp) && dp.ValueKind == JsonValueKind.Number)
                return (dp.GetDecimal(), new());

            // 預設情境：全跌 10%
            return (10m, new());
        }
        catch
        {
            return (10m, new());
        }
    }

    /// <summary>
    /// 對每個永續部位算壓測。
    ///
    /// 假設 mark = entry × (1 - drop_pct/100)。
    /// long: 跌就虧、unrealized PnL = (mark - entry) × qty × leverage 比例
    /// short: 跌就賺、unrealized PnL = (entry - mark) × qty × leverage 比例
    ///
    /// 不知道精確 qty、用 leverage × notional 估算（這個 demo 演示用、不是真執行）。
    /// 主要看「mark 是否觸發 SL / liq」這個離散事件。
    /// </summary>
    private static string BuildStressReport(
        AutoTraderService autoTrader, decimal? uniformDrop, Dictionary<string, decimal> perSymbol)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 風控壓測報告");
        sb.AppendLine();
        sb.AppendLine("**情境**:");
        if (perSymbol.Count > 0)
            foreach (var (sym, pct) in perSymbol)
                sb.AppendLine($"- `{sym}`: 下跌 {pct:F1}%");
        else
            sb.AppendLine($"- 全部標的下跌 {uniformDrop ?? 10m:F1}%");
        sb.AppendLine();

        var positions = autoTrader.PerpetualPositionStates;
        if (positions.Count == 0)
        {
            sb.AppendLine("⚠️ 當前無永續部位、無法執行壓測。");
            return sb.ToString();
        }

        sb.AppendLine("## 每倉壓測結果");
        sb.AppendLine();
        sb.AppendLine("| Symbol | Side | Lev | Entry | 壓後 Mark | PnL% | 觸發 SL? | 觸發 Liq? |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        int slHits = 0;
        int liqHits = 0;
        foreach (var (_, p) in positions)
        {
            var dropPct = perSymbol.TryGetValue(p.Symbol, out var pSpecific) ? pSpecific
                : (uniformDrop ?? 10m);
            var stressedMark = p.EntryPrice * (1m - dropPct / 100m);

            // 對 long、跌就虧；short 是反向
            // PnL% = (stressedMark - entry) / entry × leverage（簡化、不算 funding）
            var pnlPct = p.Side == "long"
                ? (stressedMark - p.EntryPrice) / p.EntryPrice * p.Leverage * 100m
                : (p.EntryPrice - stressedMark) / p.EntryPrice * p.Leverage * 100m;

            // SL 觸發判斷
            bool slHit = p.Side == "long" ? stressedMark <= p.SlPrice : stressedMark >= p.SlPrice;
            // Liq 觸發判斷
            bool liqHit = p.LiquidationPrice > 0 &&
                          (p.Side == "long" ? stressedMark <= p.LiquidationPrice : stressedMark >= p.LiquidationPrice);

            if (slHit) slHits++;
            if (liqHit) liqHits++;

            sb.AppendLine(
                $"| {p.Symbol} | {p.Side} | {p.Leverage}x | {p.EntryPrice:F4} | {stressedMark:F4} | " +
                $"{pnlPct:F2}% | {(slHit ? "🟡" : "—")} | {(liqHit ? "🔴" : "—")} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**彙總**: {positions.Count} 倉、{slHits} 觸發 SL、**{liqHits} 觸發強平**");
        return sb.ToString();
    }
}
