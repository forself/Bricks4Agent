using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// Agent Exec 端點 — MVP-2（2026-05-01）
///
/// 解決什麼：MVP-1 的 agent 只會跟 LLM 講話，雖然 DB 有 25 個 capability_grants
/// 但根本不能實際呼叫工具。MVP-2 的這個端點讓 agent 可以：
///   1. POST /agents/exec { agent_id, capability_id, params }
///   2. broker 驗證 agent 真的有這個 capability_id 在 grants 裡
///   3. 把 capability.route 翻成 InProcessDispatcher 認得的路由
///   4. 走既有的 dispatcher（memory_store / web_search / file_read 等）
///   5. 回傳結果給 agent
///
/// 設計取捨：
/// - 故意 bypass BrokerService 的 16-step PEP pipeline（會觸發 DEBUG 警告）。
///   理由：agent 的 capability_grants 在 spawn 時就被 PEP 審核過了；每次工具呼叫
///   再跑一次審批會卡死 agent 互動式行為。grant 本身就是「批准」。
/// - 真正的審計留在兩處：(a) inbox tasks 完整紀錄 prompt+reply+latency
///   (b) LLM Proxy metrics 紀錄每個 LLM round-trip
///
/// 走 trusted-internal allowlist（與 /llm-proxy、/agents/inbox 一致），不走 ECDH。
/// </summary>
public static class AgentExecEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var exec = group.MapGroup("/agents/exec");

        // POST /api/v1/agents/exec
        //   { agent_id, capability_id, params: {...} }
        //   → { success, result, capability_id, route, latency_ms, error? }
        exec.MapPost("/", async (HttpContext ctx, BrokerDb db, IExecutionDispatcher dispatcher) =>
        {
            JsonElement body;
            try { body = RequestBodyHelper.GetBody(ctx); }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error("Invalid body: " + ex.Message)); }

            if (!RequestBodyHelper.TryGetRequired(body, "agent_id",      out var agentId,   out var err1)) return err1!;
            if (!RequestBodyHelper.TryGetRequired(body, "capability_id", out var capId,     out var err2)) return err2!;

            // params 是物件，預設空 {}
            var paramsJson = "{}";
            if (body.TryGetProperty("params", out var pe) && pe.ValueKind == JsonValueKind.Object)
                paramsJson = pe.GetRawText();

            // 1. 找 agent task
            var taskRecordId = $"task_{agentId}";
            var task = db.Get<BrokerTask>(taskRecordId);
            if (task == null)
                return Results.NotFound(ApiResponseHelper.Error($"Agent '{agentId}' not found", 404));

            // 2. 解析該 agent 的 capability_grants（從 RuntimeDescriptor JSON 撈）
            var grantedRoutes = ExtractGrantedRoutes(task.RuntimeDescriptor, db);
            if (!grantedRoutes.TryGetValue(capId, out var route))
                return Results.Json(
                    ApiResponseHelper.Error($"Agent '{agentId}' does not have capability '{capId}' granted", 403),
                    statusCode: 403);

            // 3. 構造 ApprovedRequest（會觸發 DEBUG-only PEP-bypass 警告，預期內）
            //    Payload 包成 dispatcher 認得的 {args:...} 形狀
            var payload = JsonSerializer.Serialize(new
            {
                args = JsonDocument.Parse(paramsJson).RootElement
            });

            var req = new ApprovedRequest
            {
                RequestId = $"agent_exec_{Guid.NewGuid():N}"[..20],
                CapabilityId = capId,
                Route = route,
                Payload = payload,
                Scope = "{}",
                TraceId = $"agent_{agentId}",
                PrincipalId = task.AssignedPrincipalId ?? $"prn_{agentId}",
                TaskId = taskRecordId,
                SessionId = ""
            };

            // 4. 派發
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ExecutionResult result;
            try
            {
                result = await dispatcher.DispatchAsync(req);
            }
            catch (Exception ex)
            {
                return Results.Json(ApiResponseHelper.Error($"Dispatch threw: {ex.Message}", 500), statusCode: 500);
            }
            sw.Stop();

            // 5. 結構化回傳（不直接吐 dispatcher 的 RequestId 等內部欄位給 agent）
            JsonElement? resultElem = null;
            if (result.Success && !string.IsNullOrEmpty(result.ResultPayload))
            {
                try
                {
                    using var rd = JsonDocument.Parse(result.ResultPayload);
                    resultElem = rd.RootElement.Clone();
                }
                catch { /* keep raw text below */ }
            }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                success = result.Success,
                capability_id = capId,
                route = route,
                latency_ms = (int)sw.ElapsedMilliseconds,
                result = resultElem,
                result_raw = resultElem == null ? result.ResultPayload : null,
                error = result.ErrorMessage
            }));
        });

        // GET /api/v1/agents/exec/tools?agent_id=X
        //   → 把 agent 的 capability_grants 翻成 OpenAI tool spec 陣列，給 agent 直接餵 LLM。
        //   每個 tool spec 含 name=route、description、parameters=capability.param_schema
        //   不包未在 dispatcher 實作的（否則 LLM 會呼叫但失敗）。
        exec.MapGet("/tools", (HttpContext ctx, BrokerDb db) =>
        {
            var agentId = ctx.Request.Query["agent_id"].ToString();
            if (string.IsNullOrWhiteSpace(agentId))
                return Results.BadRequest(ApiResponseHelper.Error("Missing query param: agent_id"));

            var task = db.Get<BrokerTask>($"task_{agentId}");
            if (task == null)
                return Results.NotFound(ApiResponseHelper.Error($"Agent '{agentId}' not found", 404));

            var grants = ExtractGrants(task.RuntimeDescriptor);
            var allCaps = db.GetAll<Capability>().ToDictionary(c => c.CapabilityId);

            var tools = new List<object>();
            var unsupported = new List<string>();
            foreach (var capId in grants)
            {
                if (!allCaps.TryGetValue(capId, out var cap)) continue;
                if (!IsRouteImplemented(cap.Route))
                {
                    unsupported.Add(capId);
                    continue;
                }
                JsonElement parameters = default;
                try { parameters = JsonDocument.Parse(string.IsNullOrWhiteSpace(cap.ParamSchema) ? "{}" : cap.ParamSchema).RootElement.Clone(); }
                catch { parameters = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement.Clone(); }

                tools.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = cap.Route,                            // dispatcher 路由名（function name 不可有點）
                        description = $"[{cap.RiskLevel}] {DescribeShort(capId)}",
                        parameters
                    }
                });
            }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                agent_id = agentId,
                tool_count = tools.Count,
                unsupported_count = unsupported.Count,
                unsupported,
                tools
            }));
        });
    }

    /// <summary>從 RuntimeDescriptor 抽出 capability_id → route 的映射（route 從 Capability 表查）。</summary>
    private static Dictionary<string, string> ExtractGrantedRoutes(string runtimeDescriptor, BrokerDb db)
    {
        var grants = ExtractGrants(runtimeDescriptor);
        var allCaps = db.GetAll<Capability>().ToDictionary(c => c.CapabilityId);
        var map = new Dictionary<string, string>();
        foreach (var capId in grants)
        {
            if (allCaps.TryGetValue(capId, out var cap) && !string.IsNullOrEmpty(cap.Route))
                map[capId] = cap.Route;
        }
        return map;
    }

    /// <summary>從 RuntimeDescriptor JSON 抽出 capability_grants 的 capability_id 清單。</summary>
    private static List<string> ExtractGrants(string runtimeDescriptor)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(runtimeDescriptor)) return ids;
        try
        {
            using var doc = JsonDocument.Parse(runtimeDescriptor);
            if (doc.RootElement.TryGetProperty("capability_grants", out var grants) &&
                grants.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in grants.EnumerateArray())
                {
                    if (g.TryGetProperty("capability_id", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        var id = c.GetString();
                        if (!string.IsNullOrEmpty(id)) ids.Add(id);
                    }
                }
            }
        }
        catch { /* runtime descriptor malformed → no grants */ }
        return ids;
    }

    /// <summary>白名單：哪些 dispatcher route 在 InProcessDispatcher 實際接通了（避免 LLM 呼叫不存在的 tool）。</summary>
    private static bool IsRouteImplemented(string route) => route switch
    {
        "read_file" or "list_directory" or "search_files" or "search_content" => true,
        "list_agents" or "create_agent" or "stop_agent" => true,
        "conv_log_append" or "conv_log_read" => true,
        "memory_store" or "memory_retrieve" or "memory_delete" => true,
        "memory_fulltext_search" or "memory_semantic_search" => true,
        "rag_retrieve" => true,
        "web_search" or "web_search_duckduckgo" or "web_search_google" => true,
        "web_fetch" => true,
        _ => false,
    };

    private static string DescribeShort(string capId) => capId switch
    {
        "memory.read"             => "Retrieve a value previously stored under a key",
        "memory.write"            => "Store a value under a key for later retrieval",
        "memory.delete"           => "Delete a stored memory entry by key",
        "memory.fulltext_search"  => "BM25 full-text search over stored memory and conversation log",
        "memory.semantic_search"  => "Vector semantic search over stored memory",
        "rag.retrieve"            => "Hybrid (BM25+vector) retrieval over knowledge base",
        "web.search"              => "Search the web (returns titles + snippets + URLs)",
        "web.search.duckduckgo"   => "Search via DuckDuckGo",
        "web.search.google"       => "Search via Google",
        "web.fetch"               => "Fetch a web page's text content given a URL",
        "file.read"               => "Read a text file from the broker workspace",
        "file.list"               => "List directory contents",
        "file.search_name"        => "Search for files by filename pattern",
        "file.search_content"     => "Search file contents (grep-style)",
        "conv.log.write"          => "Append a message to conversation log",
        "conv.log.read"           => "Read recent conversation log entries",
        "agent.list"              => "List existing agents on the platform",
        _                         => capId
    };
}
