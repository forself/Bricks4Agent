using System.Text.Json;
using Broker.Helpers;
using Broker.Scripts;
using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Container;

namespace Broker.Endpoints;

/// <summary>
/// Agent 生命週期端點
///
/// 全 POST、全加密、零 GET —— 所有端點都經過 ECDH+AES-256-GCM 信封加密
///
/// 流程：
/// 1. POST /agents/capabilities → 取得可用能力清單（含風險等級、說明）
/// 2. POST /agents/capabilities/defaults → 取得預設（低權限）能力集合
/// 3. POST /agents/create → 選擇能力 → 建立 Agent（Principal + Task + Grants）
/// 4. POST /agents/spawn  → 生成 Agent 容器（需先 create）
/// 5. POST /agents/list → 列出所有 Agent
/// 6. POST /agents/stop → 停止 Agent 容器 + 停用 Principal
/// </summary>
public static class AgentEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static void Map(RouteGroupBuilder group)
    {
        var agents = group.MapGroup("/agents");

        // ── 1. 列出可用能力（含風險等級、分類、說明） ──
        agents.MapPost("/capabilities", (AgentSpawnService spawnService) =>
        {
            var capabilities = spawnService.ListAvailableCapabilities();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                capabilities,
                total = capabilities.Count
            }));
        });

        // ── 2. 取得預設（低權限）能力集合 ──
        agents.MapPost("/capabilities/defaults", (AgentSpawnService spawnService) =>
        {
            var defaults = spawnService.GetDefaultCapabilities();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                capability_ids = defaults,
                description = "Low-risk capabilities only (default policy)"
            }));
        });

        // ── 3. 建立 Agent（選擇能力 → 建立 Principal/Task/Grants） ──
        agents.MapPost("/create", (HttpContext ctx, AgentSpawnService spawnService, IEnvelopeCrypto crypto) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);

            // 解析能力列表
            var capabilityIds = new List<string>();
            if (body.TryGetProperty("capability_ids", out var capsEl) && capsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in capsEl.EnumerateArray())
                {
                    var id = item.GetString();
                    if (!string.IsNullOrEmpty(id)) capabilityIds.Add(id);
                }
            }

            // 如果未指定，使用預設（低權限）
            if (capabilityIds.Count == 0)
            {
                capabilityIds = spawnService.GetDefaultCapabilities();
            }

            var request = new AgentSpawnRequest
            {
                AgentId = body.TryGetProperty("agent_id", out var aid) ? aid.GetString() : null,
                DisplayName = body.TryGetProperty("display_name", out var dn) ? dn.GetString() : null,
                CapabilityIds = capabilityIds,
                TaskType = body.TryGetProperty("task_type", out var tt) ? tt.GetString() : "analysis",
                ScopeDescriptor = body.TryGetProperty("scope_descriptor", out var sd) ? sd.GetRawText() : null,
                RequestedBy = body.TryGetProperty("requested_by", out var rb) ? rb.GetString() : "api",
                QuotaPerCapability = body.TryGetProperty("quota_per_capability", out var q) ? q.GetInt32() : -1
            };

            var result = spawnService.CreateAgent(request);

            if (!result.Success)
                return Results.BadRequest(ApiResponseHelper.Error(result.Error ?? "Failed to create agent", 400));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                agent_id = result.AgentId,
                principal_id = result.PrincipalId,
                task_id = result.TaskId,
                role_id = result.RoleId,
                granted_capabilities = result.GrantedCapabilities,
                max_risk_level = result.MaxRiskLevel,
                warnings = result.Warnings,
                broker_public_key = crypto.GetBrokerPublicKey(),
                next_step = "Call POST /api/v1/agents/spawn with { agent_id } to start the container"
            }));
        });

        // ── 4. 生成 Agent 容器 ──
        agents.MapPost("/spawn", async (HttpContext ctx, AgentSpawnService spawnService, IContainerManager containerManager, IEnvelopeCrypto crypto) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "agent_id", out var agentId, out var err))
                return err!;

            // 查找已建立的 Agent
            var allAgents = spawnService.ListAgents();
            var agent = allAgents.FirstOrDefault(a => a.AgentId == agentId);
            if (agent == null)
                return Results.NotFound(ApiResponseHelper.Error($"Agent '{agentId}' not found. Create it first via POST /agents/create.", 404));

            if (agent.State != "Active")
                return Results.BadRequest(ApiResponseHelper.Error($"Agent is not active (state: {agent.State})", 400));

            // 檢查容器運行時
            var runtimeOk = await containerManager.IsRuntimeAvailableAsync();
            if (!runtimeOk)
                return Results.Json(ApiResponseHelper.Error(
                    "Container runtime not available. Configure ContainerManager or start Docker/Podman.", 503), statusCode: 503);

            // 準備環境變數
            var envOverrides = new Dictionary<string, string>
            {
                ["BROKER_URL"] = "http://broker:5000",
                ["BROKER_PUB_KEY"] = crypto.GetBrokerPublicKey(),
                ["BROKER_PRINCIPAL_ID"] = agent.PrincipalId,
                ["BROKER_TASK_ID"] = agent.TaskId,
                ["BROKER_ROLE_ID"] = agent.RoleId,
                ["BROKER_WAIT_FOR_HEALTH"] = "1",
                ["AGENT_NO_CONFIRM"] = "1",
                ["AGENT_LINE_LISTEN"] = "1",
                ["AGENT_MAX_ITERATIONS"] = "10",
                ["AGENT_VERBOSE"] = "1"
            };

            // 可選的模型覆蓋
            if (body.TryGetProperty("model", out var modelEl))
                envOverrides["AGENT_MODEL"] = modelEl.GetString() ?? "";
            if (body.TryGetProperty("provider", out var provEl))
                envOverrides["AGENT_PROVIDER"] = provEl.GetString() ?? "";
            if (body.TryGetProperty("api_key", out var keyEl))
                envOverrides["OPENAI_API_KEY"] = keyEl.GetString() ?? "";

            try
            {
                var containerId = await containerManager.SpawnWorkerAsync(
                    "agent", agentId, envOverrides);

                return Results.Ok(ApiResponseHelper.Success(new
                {
                    agent_id = agentId,
                    container_id = containerId,
                    status = "spawned",
                    capabilities = agent.Capabilities
                }));
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponseHelper.Error(
                    $"Failed to spawn agent container: {ex.Message}", 500), statusCode: 500);
            }
        });

        // ── 5. 列出所有 Agent ──
        agents.MapPost("/list", (AgentSpawnService spawnService) =>
        {
            var agentList = spawnService.ListAgents();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                agents = agentList,
                total = agentList.Count
            }));
        });

        // ── 6. 停止 Agent ──
        agents.MapPost("/stop", async (HttpContext ctx, AgentSpawnService spawnService, IContainerManager containerManager) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "agent_id", out var agentId, out var err))
                return err!;

            // 停用資料庫記錄
            spawnService.DeactivateAgent(agentId);

            // 嘗試停止容器
            try
            {
                var containers = await containerManager.ListManagedAsync();
                var agentContainer = containers.FirstOrDefault(c =>
                    c.WorkerId == agentId || c.WorkerType == "agent");

                if (agentContainer != null)
                {
                    await containerManager.StopWorkerAsync(agentContainer.ContainerId);
                }
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    agent_id = agentId,
                    status = "deactivated",
                    container_stopped = false,
                    note = $"Container stop failed: {ex.Message}"
                }));
            }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                agent_id = agentId,
                status = "stopped"
            }));
        });

        // ── 7. RAG 種子：消費者保護法寫入 ──
        agents.MapPost("/rag/seed-consumer-law", async (
            BrokerDb db,
            EmbeddingService embeddingService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SeedConsumerProtectionLaw");
            var result = await SeedConsumerProtectionLaw.SeedAsync(db, embeddingService, logger);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                laws_fetched = result.LawsFetched,
                articles_inserted = result.Inserted,
                articles_skipped = result.Skipped,
                articles_embedded = result.Embedded,
                errors = result.Errors
            }));
        });

        // ── 8. RAG 匯入：JSON ──
        agents.MapPost("/rag/import-json", async (
            HttpContext ctx,
            BrokerDb db,
            EmbeddingService embeddingService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("RagImportJson");
            var body = RequestBodyHelper.GetBody(ctx);

            var tag = body.TryGetProperty("tag", out var t) ? t.GetString() ?? "" : "";
            var taskId = body.TryGetProperty("task_id", out var tid) ? tid.GetString() ?? "global" : "global";

            if (string.IsNullOrWhiteSpace(tag))
                return Results.BadRequest(ApiResponseHelper.Error("tag is required", 400));

            // data 可以是 JSON 陣列字串或直接內嵌
            string jsonContent;
            if (body.TryGetProperty("data", out var dataEl))
            {
                if (dataEl.ValueKind == JsonValueKind.Array)
                    jsonContent = dataEl.GetRawText();
                else if (dataEl.ValueKind == JsonValueKind.String)
                    jsonContent = dataEl.GetString() ?? "[]";
                else
                    return Results.BadRequest(ApiResponseHelper.Error("data must be a JSON array or string", 400));
            }
            else
                return Results.BadRequest(ApiResponseHelper.Error("data is required (JSON array of {key, content, tag?})", 400));

            var result = await RagIngestService.ImportJsonAsync(jsonContent, tag, taskId, db, embeddingService, logger);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                inserted = result.Inserted,
                skipped = result.Skipped,
                embedded = result.Embedded,
                errors = result.Errors
            }));
        });

        // ── 9. RAG 匯入：CSV ──
        agents.MapPost("/rag/import-csv", async (
            HttpContext ctx,
            BrokerDb db,
            EmbeddingService embeddingService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("RagImportCsv");
            var body = RequestBodyHelper.GetBody(ctx);

            var tag = body.TryGetProperty("tag", out var t) ? t.GetString() ?? "" : "";
            var taskId = body.TryGetProperty("task_id", out var tid) ? tid.GetString() ?? "global" : "global";

            if (string.IsNullOrWhiteSpace(tag))
                return Results.BadRequest(ApiResponseHelper.Error("tag is required", 400));

            var csvContent = body.TryGetProperty("data", out var dataEl) ? dataEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(csvContent))
                return Results.BadRequest(ApiResponseHelper.Error("data is required (CSV string)", 400));

            var result = await RagIngestService.ImportCsvAsync(csvContent, tag, taskId, db, embeddingService, logger);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                inserted = result.Inserted,
                skipped = result.Skipped,
                embedded = result.Embedded,
                errors = result.Errors
            }));
        });

        // ── 10. RAG 匯入：網路搜尋 ──
        agents.MapPost("/rag/import-web", async (
            HttpContext ctx,
            BrokerDb db,
            EmbeddingService embeddingService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("RagImportWeb");
            var body = RequestBodyHelper.GetBody(ctx);

            var query = body.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            var tag = body.TryGetProperty("tag", out var t) ? t.GetString() : null;
            var taskId = body.TryGetProperty("task_id", out var tid) ? tid.GetString() ?? "global" : "global";
            var maxPages = body.TryGetProperty("max_pages", out var mp) ? mp.GetInt32() : 5;
            var chunkSize = body.TryGetProperty("chunk_size", out var cs) ? cs.GetInt32() : 1000;
            var chunkOverlap = body.TryGetProperty("chunk_overlap", out var co) ? co.GetInt32() : 100;

            List<string>? urls = null;
            if (body.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
            {
                urls = new List<string>();
                foreach (var u in urlsEl.EnumerateArray())
                {
                    var urlStr = u.GetString();
                    if (!string.IsNullOrWhiteSpace(urlStr)) urls.Add(urlStr);
                }
            }

            if (string.IsNullOrWhiteSpace(query) && (urls == null || urls.Count == 0))
                return Results.BadRequest(ApiResponseHelper.Error("query or urls is required", 400));

            var request = new RagIngestService.WebSearchRequest
            {
                Query = query,
                Tag = tag ?? query,
                MaxPages = maxPages,
                Urls = urls,
                ChunkSize = chunkSize,
                ChunkOverlap = chunkOverlap
            };

            var result = await RagIngestService.ImportFromWebAsync(request, taskId, db, embeddingService, logger);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                pages_fetched = result.PagesFetched,
                inserted = result.Inserted,
                skipped = result.Skipped,
                embedded = result.Embedded,
                errors = result.Errors
            }));
        });

        // ── 11. RAG 測試查詢 ──
        agents.MapPost("/rag/test", async (
            HttpContext ctx,
            BrokerDb db,
            EmbeddingService embeddingService) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var query = body.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
            var mode = body.TryGetProperty("mode", out var m) ? m.GetString() ?? "hybrid" : "hybrid";
            var limitVal = body.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 5;

            if (string.IsNullOrEmpty(query))
                return Results.BadRequest(ApiResponseHelper.Error("query is required", 400));

            var taskId = "global";
            var k = 60; // RRF constant

            // BM25 分支
            var bm25Results = new List<(string key, string content, double rank)>();
            try
            {
                var fts = db.Query<FtsQueryResult>(
                    "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                    new { q = query, taskId, fetchLimit = limitVal * 3 });

                bm25Results = fts.Select(r => (r.SourceKey ?? "", r.Content ?? "", r.Rank)).ToList();
            }
            catch { }

            // 向量分支
            var vecResults = new List<(string key, float similarity)>();
            if (mode is "semantic" or "hybrid" && embeddingService.IsEnabled)
            {
                try
                {
                    var queryVector = await embeddingService.EmbedAsync(query);
                    if (queryVector != null)
                    {
                        var vectors = db.GetAll<VectorEntry>()
                            .Where(v => v.TaskId == taskId && v.Embedding.Length > 0)
                            .ToList();

                        vecResults = vectors.Select(v =>
                        {
                            var vec = EmbeddingService.BytesToVector(v.Embedding);
                            var sim = EmbeddingService.CosineSimilarity(queryVector, vec);
                            return (v.SourceKey, sim);
                        })
                        .Where(x => x.sim >= 0.2f)
                        .OrderByDescending(x => x.sim)
                        .Take(limitVal * 3)
                        .ToList();
                    }
                }
                catch { }
            }

            // RRF 融合
            var bm25Scores = new Dictionary<string, float>();
            var vecScores = new Dictionary<string, float>();
            var contentCache = new Dictionary<string, string>();

            int rank = 1;
            foreach (var (key, content, _) in bm25Results)
            {
                bm25Scores[key] = 1.0f / (k + rank);
                contentCache[key] = content;
                rank++;
            }

            rank = 1;
            foreach (var (key, _) in vecResults)
            {
                vecScores[key] = 1.0f / (k + rank);
                rank++;
            }

            var allKeys = bm25Scores.Keys.Union(vecScores.Keys).Distinct();
            var fused = allKeys.Select(key =>
            {
                var bm25 = bm25Scores.GetValueOrDefault(key, 0f);
                var vec = vecScores.GetValueOrDefault(key, 0f);
                return new { key, score = bm25 + vec, bm25, vec };
            })
            .OrderByDescending(x => x.score)
            .Take(limitVal)
            .ToList();

            var results = fused.Select(f =>
            {
                string content;
                if (contentCache.TryGetValue(f.key, out var cached))
                    content = cached;
                else
                {
                    var entry = db.GetAll<SharedContextEntry>()
                        .Where(e => e.TaskId == taskId && e.Key == f.key)
                        .OrderByDescending(e => e.Version)
                        .FirstOrDefault();
                    content = entry?.ContentRef ?? "";
                }

                return new
                {
                    key = f.key,
                    content = content.Length > 500 ? content[..500] + "..." : content,
                    rrf_score = MathF.Round(f.score, 4),
                    bm25_score = MathF.Round(f.bm25, 4),
                    vector_score = MathF.Round(f.vec, 4)
                };
            }).ToList();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                query,
                mode,
                results,
                total = results.Count,
                bm25_candidates = bm25Results.Count,
                vector_candidates = vecResults.Count,
                vector_db_total = db.GetAll<VectorEntry>().Count(v => v.TaskId == taskId)
            }));
        });
    }

    // FTS query DTO
    private class FtsQueryResult
    {
        [BaseOrm.Column("source_key")]
        public string? SourceKey { get; set; }
        [BaseOrm.Column("content")]
        public string? Content { get; set; }
        [BaseOrm.Column("rank")]
        public double Rank { get; set; }
    }
}
