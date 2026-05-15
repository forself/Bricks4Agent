using System.Text;
using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// H4 — Audit RAG：把自然語言問題對 audit_events 做檢索 + LLM 摘要。
///
/// POST /api/v1/audit/search
///   body: { query: "誰按過 KILL_SWITCH？", limit: 30, since_hours: 168 }
///   resp: { events: [...], match_strategy: "fts5"|"like" }
///
/// POST /api/v1/audit/rag-summary
///   body: { query: ..., events: [...] }   ← events 通常從 search 接力過來
///   resp: { summary: "過去一週共觸發 3 次..." , model: "...", citations: [...] }
///
/// 為什麼不用 vector embedding：
/// - 需要 Ollama / OpenAI embedding service 在線、demo 環境未必跑得起來
/// - audit_events 結構化欄位（event_type / principal / resource_ref / details）對 LIKE / FTS5
///   命中率本來就高、語義搜尋對「KILL_SWITCH」這種精確 token 收益很小
/// - RAG 的核心是 retrieve + LLM、retrieve 用什麼方法可替換、不影響本功能架構
///
/// 安全：只給 admin、避免 LLM prompt 洩漏 audit chain 內容給 user。
/// </summary>
public static class AuditRagEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var ar = group.MapGroup("/audit");

        ar.MapPost("/search", (HttpContext ctx, BrokerDb db) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var body = RequestBodyHelper.GetBody(ctx);
            var query = body.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            var limit = body.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li) ? Math.Clamp(li, 1, 200) : 30;
            var sinceHours = body.TryGetProperty("since_hours", out var s) && s.TryGetInt32(out var si)
                ? Math.Clamp(si, 1, 24 * 30) : 168;
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(ApiResponseHelper.Error("query required"));

            var since = DateTime.UtcNow.AddHours(-sinceHours);
            var sinceStr = since.ToString("o");

            // 用 LIKE 在 event_type / principal_id / resource_ref / details 四欄查
            // tokenize：用空白切、每 token 作為 LIKE pattern（AND 邏輯）
            var tokens = query.Split(new[] { ' ', ',', '、', '，', '?', '？' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2).Distinct().Take(8).ToList();
            if (tokens.Count == 0) tokens = new List<string> { query };

            var conditions = new StringBuilder();
            var args = new Dictionary<string, object> { ["sinceStr"] = sinceStr, ["lim"] = limit };
            for (int i = 0; i < tokens.Count; i++)
            {
                var k = $"@tok{i}";
                if (i > 0) conditions.Append(" AND ");
                conditions.Append($"(event_type LIKE {k} OR principal_id LIKE {k} OR resource_ref LIKE {k} OR details LIKE {k})");
                args[$"tok{i}"] = $"%{tokens[i]}%";
            }
            var sql = $@"SELECT * FROM audit_events
                         WHERE occurred_at >= @sinceStr
                           AND ({conditions})
                         ORDER BY occurred_at DESC LIMIT @lim";

            var events = db.Query<AuditEvent>(sql, args);
            return Results.Ok(ApiResponseHelper.Success(new {
                query,
                tokens,
                since_hours = sinceHours,
                match_strategy = "like",
                count = events.Count,
                events = events.Select(e => new {
                    event_id = e.EventId,
                    trace_id = e.TraceId,
                    trace_seq = e.TraceSeq,
                    event_type = e.EventType,
                    principal_id = e.PrincipalId,
                    resource_ref = e.ResourceRef,
                    occurred_at = e.OccurredAt,
                    event_hash = e.EventHash,
                    details_preview = (e.Details ?? "").Length > 240 ? e.Details!.Substring(0, 240) + "…" : e.Details,
                }),
                hint = events.Count == 0
                    ? "沒命中、試試別的關鍵字（如 KILL_SWITCH / APPROVAL / DISPATCH）"
                    : "把 events 帶進 /audit/rag-summary 拿 LLM 摘要",
            }));
        });

        ar.MapPost("/rag-summary", async (HttpContext ctx, ILlmProxyService llm) =>
        {
            if (!RequireAdmin(ctx, out var d)) return d;
            var body = RequestBodyHelper.GetBody(ctx);
            var query = body.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest(ApiResponseHelper.Error("query required"));

            if (!llm.IsEnabled)
                return Results.Ok(ApiResponseHelper.Error("LLM proxy disabled, summary unavailable"));

            // events 從 body 帶進來（caller 先呼 /search、再把 events 餵進來）
            string contextJson = "[]";
            if (body.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Array)
                contextJson = ev.GetRawText();

            var prompt = $@"你是 broker 平台稽核助手。下面 JSON 陣列是從 audit_events 表撈出的事件、
按時間倒序。請用繁體中文回答使用者問題、並引用具體的 trace_id / event_type / 時間。
若事件不足以回答、直接說「資料不足、請延長時間窗或換關鍵字」、不要編造。

使用者問題：{query}

事件 JSON：
{contextJson}

請以 markdown 結構回答：
1. **直接答案**（1-2 句）
2. **支持證據**（列點：trace_id 縮短前 18 字 + event_type + 時間）
3. **觀察**（如果有趨勢 / 異常）";

            var jsonBody = $@"{{""model"":""haiku"",""messages"":[{{""role"":""user"",""content"":{JsonEncodedText.Encode(prompt)}}}]}}";

            try
            {
                using var doc = JsonDocument.Parse(jsonBody);
                var result = await llm.ChatAsync(doc.RootElement, task: null, ctx.RequestAborted);
                return Results.Ok(ApiResponseHelper.Success(new {
                    query,
                    summary = result.Content,
                    model = result.Model,
                    duration_ms = result.TotalDuration / 1_000_000,
                    eval_tokens = result.EvalCount,
                }));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResponseHelper.Error("LLM summary failed: " + ex.Message));
            }
        });
    }

    private static bool RequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (RequestBodyHelper.IsAdmin(ctx)) { denied = null!; return true; }
        denied = Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);
        return false;
    }
}
