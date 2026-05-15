using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// RAG Ingest agent — manual-push only。給一個 query 關鍵字（broker 自己 web search） 或一組 URLs、
/// agent 走 broker `rag_import_web` route → 拉網頁 + chunk + embedding + 存 vector_entries 表。
///
/// 串 Benson 的 RAG 抽象（EmbeddingService + RagPipelineService + vector_entries 表）。
/// 之後 query rewrite / rerank 自動含這些文件。
///
/// Prompt JSON:
///   {"query":"比特幣減半"}                              ← 搜 + 抓前 5 頁
///   {"urls":["https://...","https://..."],"tag":"docs"} ← 指定 urls
/// </summary>
public class RagIngestAgentService : BackgroundService
{
    public const string AgentIdConst = "agent_rag_ingest";
    private const string PrincipalIdConst = "prn_agent_rag_ingest";
    private const int PollIntervalSeconds = 60;

    private readonly IServiceProvider _sp;
    private readonly ILogger<RagIngestAgentService> _logger;

    public RagIngestAgentService(IServiceProvider sp, ILogger<RagIngestAgentService> logger)
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
            DisplayName = "RAG Ingest (on-demand)",
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
            var (query, urls, tag) = ParsePrompt(pending.Prompt);
            if (string.IsNullOrEmpty(query) && (urls == null || urls.Count == 0))
            {
                pending.Status = "failed";
                pending.Error = "需要 query 或 urls 至少一項";
                pending.CompletedAt = DateTime.UtcNow;
                db.Update(pending);
                return;
            }

            var dispatcher = scope.ServiceProvider.GetRequiredService<IExecutionDispatcher>();
            var argsObj = new Dictionary<string, object?>
            {
                ["query"] = query ?? tag ?? "rag-ingest",
                ["tag"]   = tag ?? query ?? "rag-ingest",
                ["max_pages"] = 5,
            };
            if (urls != null && urls.Count > 0) argsObj["urls"] = urls;

            ApprovedRequest.WarnIfBypass();
            var req = new ApprovedRequest
            {
                RequestId = $"rag_ing_{Guid.NewGuid():N}"[..18],
                CapabilityId = "rag.import.web",
                Route = "rag_import_web",
                PrincipalId = PrincipalIdConst,
                TaskId = "rag-ingest",
                SessionId = "rag-ingest",
                Scope = "{}",
                Payload = JsonSerializer.Serialize(new { route = "rag_import_web", args = argsObj })
            };
            var result = await dispatcher.DispatchAsync(req);

            pending.Status = result.Success ? "done" : "failed";
            pending.Reply = result.Success
                ? $"# RAG Ingest 完成\n\n- query: `{query ?? "(none)"}`\n- urls: {urls?.Count ?? 0}\n- tag: `{tag ?? "(default)"}`\n\n## Result\n\n```json\n{result.ResultPayload}\n```"
                : null;
            pending.Error = result.Success ? null : result.ErrorMessage;
            pending.Model = "(dispatcher)";
            pending.LatencyMs = (int)(DateTime.UtcNow - startMs).TotalMilliseconds;
            pending.CompletedAt = DateTime.UtcNow;
            db.Update(pending);
            _logger.LogInformation("[{Agent}] task={T} {S}", AgentIdConst, pending.TaskId,
                result.Success ? "done" : "failed: " + result.ErrorMessage);
        }
        catch (Exception ex)
        {
            pending.Status = "failed"; pending.Error = ex.Message;
            pending.CompletedAt = DateTime.UtcNow; db.Update(pending);
            _logger.LogError(ex, "[{Agent}] task={T} failed", AgentIdConst, pending.TaskId);
        }
    }

    internal static (string? Query, List<string>? Urls, string? Tag) ParsePrompt(string prompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;
            var query = root.TryGetProperty("query", out var q) ? q.GetString() : null;
            var tag = root.TryGetProperty("tag", out var t) ? t.GetString() : null;
            List<string>? urls = null;
            if (root.TryGetProperty("urls", out var u) && u.ValueKind == JsonValueKind.Array)
            {
                urls = new List<string>();
                foreach (var x in u.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String) urls.Add(x.GetString()!);
            }
            return (query, urls, tag);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
