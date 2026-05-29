using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Endpoints;

/// <summary>
/// Agent Inbox 端點 — MVP-1（2026-05-01）
///
/// 解決 MVP-0 的限制：spawn 後就只能跑啟動時的 prompt。MVP-1 的 inbox 模型讓：
///   - dashboard / 任何呼叫者 push 一筆 prompt
///   - agent 容器 poll 一筆 pending → 跑 LLM → complete 回填結果
///   - dashboard list 看任務歷史
///
/// 全部走 trusted-internal allowlist（不需 ECDH session），因為呼叫端都在
/// docker 內部網路（agent 容器、dashboard via plain JSON path），跟 LLM Proxy 一樣。
/// </summary>
public static class AgentInboxEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var inbox = group.MapGroup("/agents/inbox");

        // POST /api/v1/agents/inbox/push
        //   { agent_id, prompt, requested_by? }
        //   → { task_id, seq, status: "pending" }
        inbox.MapPost("/push", (HttpContext ctx, BrokerDb db) =>
        {
            JsonElement body;
            try { body = RequestBodyHelper.GetBody(ctx); }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error("Invalid body: " + ex.Message)); }

            if (!RequestBodyHelper.TryGetRequired(body, "agent_id", out var agentId, out var err1)) return err1!;
            if (!RequestBodyHelper.TryGetRequired(body, "prompt",   out var prompt,  out var err2)) return err2!;

            // 驗證 agent 存在（身份必須對應到實際的 agent 紀錄）
            var principalId = $"prn_{agentId}";
            if (db.Get<Principal>(principalId) == null)
                return Results.NotFound(ApiResponseHelper.Error($"Agent '{agentId}' not found", 404));

            var requestedBy = body.TryGetProperty("requested_by", out var rb) && rb.ValueKind == JsonValueKind.String
                ? rb.GetString() : null;
            // 冪等鍵(選填):同 (agent_id, idempotency_key) 已有任務 → 回既有、不重建。
            // 防 client 重試 / broker 重啟接手時重複建任務;沒帶 key → 維持原行為(不去重)。
            var idempotencyKey = body.TryGetProperty("idempotency_key", out var ik) && ik.ValueKind == JsonValueKind.String
                ? ik.GetString() : null;

            IResult Existing(AgentInboxTask t) => Results.Ok(ApiResponseHelper.Success(new
            {
                task_id = t.TaskId, agent_id = t.AgentId, seq = t.Seq,
                status = t.Status, created_at = t.CreatedAt, deduped = true
            }));

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var dup = db.QueryFirst<AgentInboxTask>(
                    "SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND idempotency_key = @k LIMIT 1",
                    new { aid = agentId, k = idempotencyKey });
                if (dup != null) return Existing(dup);
            }

            var taskId = $"inbox_{Guid.NewGuid():N}"[..20];
            AgentInboxTask entity;
            try
            {
                // seq 計算 + insert 包進交易:InTransaction 走 _writeGate 把整段序列化,
                // 修「兩個併發 push 讀到同一個 MAX(seq) → 撞號」。
                entity = db.InTransaction(() =>
                {
                    var maxSeq = db.QueryFirst<MaxSeqRow>(
                        "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
                        new { aid = agentId });
                    var e = new AgentInboxTask
                    {
                        TaskId = taskId,
                        AgentId = agentId,
                        Seq = (maxSeq?.Seq ?? 0) + 1,
                        Prompt = prompt,
                        Status = "pending",
                        RequestedBy = requestedBy,
                        IdempotencyKey = idempotencyKey,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Insert(e);
                    return e;
                });
            }
            catch (Exception) when (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                // 併發:另一請求用同 key 搶先 insert → 撞 UNIQUE index。
                // 冪等的權威是「捕捉 UNIQUE 違反例外」,不是上面的 SELECT 早退(SELECT 不阻塞並發)。
                var raced = db.QueryFirst<AgentInboxTask>(
                    "SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND idempotency_key = @k LIMIT 1",
                    new { aid = agentId, k = idempotencyKey });
                if (raced != null) return Existing(raced);
                throw;   // 不是 key 衝突造成的 → 照常往上拋
            }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                task_id = entity.TaskId,
                agent_id = agentId,
                seq = entity.Seq,
                status = "pending",
                created_at = entity.CreatedAt
            }));
        });

        // GET /api/v1/agents/inbox/pull?agent_id=X
        //   → { task_id, prompt, seq } 或 null
        //   side effect：把該筆任務從 pending → processing
        //   重點：只 atomic 抓一筆、避免兩個 agent 容器搶同一筆
        inbox.MapGet("/pull", (HttpContext ctx, BrokerDb db) =>
        {
            var agentId = ctx.Request.Query["agent_id"].ToString();
            if (string.IsNullOrWhiteSpace(agentId))
                return Results.BadRequest(ApiResponseHelper.Error("Missing query param: agent_id"));

            // 撈最舊的一筆 pending（FIFO）
            var pending = db.QueryFirst<AgentInboxTask>(
                @"SELECT * FROM agent_inbox_tasks
                  WHERE agent_id = @aid AND status = 'pending'
                  ORDER BY seq ASC LIMIT 1",
                new { aid = agentId });

            if (pending == null)
                return Results.Ok(ApiResponseHelper.Success<object?>(null));

            // 標記為 processing — 一句 UPDATE 也是 atomic（SQLite 預設行為）
            // 競爭：若兩個 agent 同時 pull、都拿到同一筆，第一個 UPDATE 會成功；
            // 第二個會看到 status 已不是 pending（在 WHERE 加守衛），rowsAffected=0 → 跳掉重試
            var rows = db.Execute(
                @"UPDATE agent_inbox_tasks
                  SET status = 'processing', started_at = @ts
                  WHERE task_id = @tid AND status = 'pending'",
                new { tid = pending.TaskId, ts = DateTime.UtcNow });

            if (rows == 0)
                return Results.Ok(ApiResponseHelper.Success<object?>(null)); // 別人搶到了，這次回空

            return Results.Ok(ApiResponseHelper.Success(new
            {
                task_id = pending.TaskId,
                agent_id = pending.AgentId,
                seq = pending.Seq,
                prompt = pending.Prompt,
                created_at = pending.CreatedAt
            }));
        });

        // POST /api/v1/agents/inbox/complete
        //   { task_id, success: bool, reply?: string, error?: string,
        //     model?: string, eval_tokens?: int, latency_ms?: int }
        inbox.MapPost("/complete", (HttpContext ctx, BrokerDb db) =>
        {
            JsonElement body;
            try { body = RequestBodyHelper.GetBody(ctx); }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error("Invalid body: " + ex.Message)); }

            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out var err)) return err!;

            var task = db.Get<AgentInboxTask>(taskId);
            if (task == null)
                return Results.NotFound(ApiResponseHelper.Error($"Task '{taskId}' not found", 404));

            var success = body.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            task.Status = success ? "done" : "failed";
            task.CompletedAt = DateTime.UtcNow;

            if (body.TryGetProperty("reply", out var rep) && rep.ValueKind == JsonValueKind.String)
                task.Reply = rep.GetString();
            if (body.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String)
                task.Error = er.GetString();
            if (body.TryGetProperty("model", out var mod) && mod.ValueKind == JsonValueKind.String)
                task.Model = mod.GetString();
            if (body.TryGetProperty("eval_tokens", out var et) && et.ValueKind == JsonValueKind.Number)
                task.EvalTokens = et.GetInt32();
            if (body.TryGetProperty("latency_ms", out var lm) && lm.ValueKind == JsonValueKind.Number)
                task.LatencyMs = lm.GetInt32();

            db.Update(task);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                task_id = task.TaskId,
                status = task.Status,
                completed_at = task.CompletedAt
            }));
        });

        // GET /api/v1/agents/inbox/list?agent_id=X&limit=50
        //   → { tasks: [...], total }
        //   給 dashboard 的「任務歷史」面板用。
        inbox.MapGet("/list", (HttpContext ctx, BrokerDb db) =>
        {
            var agentId = ctx.Request.Query["agent_id"].ToString();
            var limitStr = ctx.Request.Query["limit"].ToString();
            var limit = int.TryParse(limitStr, out var l) ? Math.Clamp(l, 1, 200) : 50;

            List<AgentInboxTask> tasks;
            if (string.IsNullOrWhiteSpace(agentId))
            {
                tasks = db.Query<AgentInboxTask>(
                    "SELECT * FROM agent_inbox_tasks ORDER BY created_at DESC LIMIT @lim",
                    new { lim = limit });
            }
            else
            {
                tasks = db.Query<AgentInboxTask>(
                    @"SELECT * FROM agent_inbox_tasks
                      WHERE agent_id = @aid
                      ORDER BY seq DESC LIMIT @lim",
                    new { aid = agentId, lim = limit });
            }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = tasks.Count,
                tasks = tasks.Select(t => new
                {
                    task_id = t.TaskId,
                    agent_id = t.AgentId,
                    seq = t.Seq,
                    prompt = t.Prompt,
                    status = t.Status,
                    reply = t.Reply,
                    error = t.Error,
                    requested_by = t.RequestedBy,
                    model = t.Model,
                    eval_tokens = t.EvalTokens,
                    latency_ms = t.LatencyMs,
                    created_at = t.CreatedAt,
                    started_at = t.StartedAt,
                    completed_at = t.CompletedAt,
                })
            }));
        });
    }

    /// <summary>輔助 row 用於 MAX(seq) 查詢回傳。</summary>
    private class MaxSeqRow
    {
        public int Seq { get; set; }
    }
}
