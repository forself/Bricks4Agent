using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentWorker;

/// <summary>
/// Agent 的執行邏輯。
///
/// MVP-0：單次自介 LLM call（保留為 RunGreetingAsync）
/// MVP-1：inbox poll → LLM 對話 → complete 回填
/// MVP-2：在 LLM call 上接 tool calling — 把 broker 的 /agents/exec/tools 拿來的
///        OpenAI tool spec 餵給 LLM，模型回 tool_calls 就走 /agents/exec 執行，
///        結果再餵回 LLM 直到模型回 final text answer（最多 N 輪迴避無限迴圈）。
///
/// 為什麼還是走 broker LlmProxy + broker exec：
///   1. token / 工具用量都計入儀表板（觀測性）
///   2. capability_grants 是工具呼叫的唯一授權路徑（治理）
///   3. 這條路徑是「平台政策被實際執行」的證據，不是設計圖（給專題報告 §14 用）
/// </summary>
public class AgentRuntime
{
    private readonly string _brokerUrl;
    private readonly string _agentId;
    private readonly string _principalId;
    private readonly string _taskId;
    private readonly string _roleId;
    private readonly ILogger _log;
    private readonly HttpClient _http;

    /// <summary>tool spec 陣列 — 啟動時從 broker 撈，餵給 LLM 用</summary>
    private JsonElement? _tools;

    public AgentRuntime(string brokerUrl, string agentId, string principalId, string taskId, string roleId, ILogger log)
    {
        _brokerUrl = brokerUrl.TrimEnd('/');
        _agentId = agentId;
        _principalId = principalId;
        _taskId = taskId;
        _roleId = roleId;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>啟動時撈一次 tool spec — 後續每次 LLM 呼叫都餵一樣的 list 給模型。</summary>
    public async Task LoadToolsAsync()
    {
        try
        {
            var url = $"{_brokerUrl}/api/v1/agents/exec/tools?agent_id={Uri.EscapeDataString(_agentId)}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("/agents/exec/tools HTTP {S} — agent 將以無工具模式運行", (int)resp.StatusCode);
                return;
            }
            var raw = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;

            var tools = data.GetProperty("tools").Clone();
            _tools = tools;

            var unsupported = data.TryGetProperty("unsupported_count", out var u) ? u.GetInt32() : 0;
            _log.LogInformation("loaded {N} tool spec(s)（dispatcher 沒實作的略過 {U} 個）",
                tools.GetArrayLength(), unsupported);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "載入 tool spec 失敗 — agent 將以無工具模式運行");
        }
    }

    /// <summary>啟動自介。</summary>
    public async Task RunGreetingAsync(string userPrompt)
    {
        var (reply, _, _, _) = await CallLlmWithToolsAsync(new List<object>
        {
            new { role = "system", content = BuildSystemPrompt() },
            new { role = "user",   content = userPrompt }
        }, allowTools: false);

        if (!string.IsNullOrWhiteSpace(reply))
            PrintReply("Greeting", reply);
    }

    /// <summary>從 broker inbox pull → LLM (含 tool calling) → complete。</summary>
    public async Task<bool> PollAndProcessOnceAsync()
    {
        // 1. pull
        var pullUrl = $"{_brokerUrl}/api/v1/agents/inbox/pull?agent_id={Uri.EscapeDataString(_agentId)}";
        var pullResp = await _http.GetAsync(pullUrl);
        if (!pullResp.IsSuccessStatusCode)
        {
            _log.LogWarning("pull HTTP {Status}", (int)pullResp.StatusCode);
            return false;
        }
        var pullJson = await pullResp.Content.ReadAsStringAsync();
        using var pullDoc = JsonDocument.Parse(pullJson);
        var data = pullDoc.RootElement.TryGetProperty("data", out var d) ? d : default;
        if (data.ValueKind != JsonValueKind.Object) return false;

        var taskId = data.GetProperty("task_id").GetString() ?? "";
        var seq    = data.TryGetProperty("seq", out var sq) ? sq.GetInt32() : 0;
        var prompt = data.GetProperty("prompt").GetString() ?? "";

        _log.LogInformation("📨 task #{Seq} ({Tid}) pulled — prompt: {P}", seq, taskId, Truncate(prompt, 80));

        // 2. LLM with tool calling
        var sw = Stopwatch.StartNew();
        bool success;
        string? reply = null, error = null, model = null;
        int evalTokens = 0, toolCallsCount = 0;
        try
        {
            var messages = new List<object>
            {
                new { role = "system", content = BuildSystemPrompt() },
                new { role = "user",   content = prompt }
            };
            (reply, model, evalTokens, toolCallsCount) = await ToolCallingLoopAsync(messages, maxRounds: 4);
            success = !string.IsNullOrEmpty(reply);
            if (!success) error = "Empty LLM reply after tool-calling loop";
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
            _log.LogError(ex, "task #{Seq} failed", seq);
        }
        sw.Stop();

        if (success && !string.IsNullOrWhiteSpace(reply))
            PrintReply($"Task #{seq}" + (toolCallsCount > 0 ? $" (used {toolCallsCount} tool call(s))" : ""), reply);

        // 3. complete
        var completeBody = JsonSerializer.Serialize(new
        {
            task_id = taskId, success, reply, error, model,
            eval_tokens = evalTokens, latency_ms = (int)sw.ElapsedMilliseconds
        });
        var completeResp = await _http.PostAsync($"{_brokerUrl}/api/v1/agents/inbox/complete",
            new StringContent(completeBody, Encoding.UTF8, "application/json"));
        if (!completeResp.IsSuccessStatusCode)
            _log.LogWarning("complete HTTP {S}", (int)completeResp.StatusCode);
        else
            _log.LogInformation("✓ task #{Seq} {St} ({Ms}ms, {T} tokens, {Tc} tool calls)",
                seq, success ? "done" : "failed", sw.ElapsedMilliseconds, evalTokens, toolCallsCount);

        return true;
    }

    /// <summary>
    /// 真正的 tool calling 迴圈：LLM ⇄ tool execution，最多 maxRounds 輪 fail-safe。
    /// 回傳：(最後的文字回覆, 模型名, 累計 eval tokens, 工具呼叫次數)
    /// </summary>
    private async Task<(string content, string model, int evalTokens, int toolCalls)> ToolCallingLoopAsync(
        List<object> messages, int maxRounds)
    {
        string lastModel = "";
        int totalTokens = 0;
        int toolCalls = 0;
        var trace = new List<string>(); // 每筆工具呼叫的單行摘要，最後 append 到 reply 末尾

        for (int round = 0; round < maxRounds; round++)
        {
            var (content, toolCallList, model, eval) = await CallLlmWithToolsAsync(messages, allowTools: true);
            lastModel = model;
            totalTokens += eval;

            if (toolCallList == null || toolCallList.Count == 0)
                return (AppendTrace(content, trace), lastModel, totalTokens, toolCalls);

            // 模型要呼叫工具 — 把 assistant turn (含 tool_calls) 加進 history，再一個一個跑
            messages.Add(new
            {
                role = "assistant",
                content = (string?)null,
                tool_calls = toolCallList.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments }
                }).ToArray()
            });

            foreach (var tc in toolCallList)
            {
                toolCalls++;
                _log.LogInformation("🔧 tool_call: {Name}({Args})", tc.Name, Truncate(tc.Arguments, 120));

                var sw = Stopwatch.StartNew();
                string toolResult;
                bool ok = true;
                string? errBrief = null;
                try
                {
                    toolResult = await ExecToolAsync(tc.Name, tc.Arguments);
                    // exec endpoint 永遠回 200 + JSON；判斷 success 看 inner data.success
                    try
                    {
                        using var d = JsonDocument.Parse(toolResult);
                        if (d.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                        {
                            ok = false;
                            if (d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                                errBrief = e.GetString();
                        }
                    }
                    catch { /* ok stays true if can't parse */ }
                }
                catch (Exception ex)
                {
                    ok = false;
                    errBrief = ex.Message;
                    toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                    _log.LogWarning("tool {Name} failed: {Msg}", tc.Name, ex.Message);
                }
                sw.Stop();

                _log.LogInformation("   ← {Result}", Truncate(toolResult, 160));
                trace.Add(ok
                    ? $"  - `{tc.Name}({Truncate(tc.Arguments, 80)})` → ok ({sw.ElapsedMilliseconds}ms)"
                    : $"  - `{tc.Name}({Truncate(tc.Arguments, 80)})` → **fail** {Truncate(errBrief ?? "?", 80)} ({sw.ElapsedMilliseconds}ms)");

                messages.Add(new
                {
                    role = "tool",
                    tool_call_id = tc.Id,
                    name = tc.Name,
                    content = toolResult
                });
            }
        }

        // 跑滿 maxRounds 還沒收斂 → 強制結束，要 LLM 用目前 messages 給最終答覆（不允許再用工具）
        var (finalContent, _, finalModel, finalEval) = await CallLlmWithToolsAsync(messages, allowTools: false);
        totalTokens += finalEval;
        if (string.IsNullOrEmpty(lastModel)) lastModel = finalModel;
        return (AppendTrace(finalContent, trace), lastModel, totalTokens, toolCalls);
    }

    /// <summary>
    /// 把工具呼叫摘要 append 到 reply 末尾。dashboard 派任務歷史會看到。
    /// content 為空時不 append — 讓 PollAndProcessOnceAsync 的「Empty LLM reply」
    /// 路徑能正確觸發 failed 狀態（避免「LLM 503 但有 trace 就誤判 done」）。
    /// </summary>
    private static string AppendTrace(string content, List<string> trace)
    {
        if (trace.Count == 0 || string.IsNullOrWhiteSpace(content)) return content;
        var sb = new StringBuilder(content.TrimEnd());
        sb.AppendLine().AppendLine().AppendLine("---");
        sb.AppendLine($"🔧 工具呼叫（{trace.Count} 次）:");
        foreach (var line in trace) sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    /// <summary>呼叫 broker /agents/exec 執行被授予的工具。</summary>
    private async Task<string> ExecToolAsync(string route, string argumentsJson)
    {
        // arguments 是 LLM 給的 JSON 字串；轉成物件後傳給 broker
        JsonElement argsElem;
        try { argsElem = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson).RootElement; }
        catch (Exception ex) { return $"{{\"error\":\"invalid arguments JSON: {ex.Message}\"}}"; }

        // route → capability_id 對照（用最少程式：靠 broker 端的 capability 表查）
        // 為了不再多打一條 HTTP，我們在 broker /tools 回應裡新增 route → capability 對照表會更乾淨；
        // 這裡先用 hard-coded 對照（與 BrokerDbInitializer.SeedCapabilities 對齊）。
        var capabilityId = RouteToCapabilityId(route);
        if (capabilityId == null)
            return $"{{\"error\":\"unknown route: {route}\"}}";

        var body = JsonSerializer.Serialize(new
        {
            agent_id = _agentId,
            capability_id = capabilityId,
            @params = argsElem
        });

        var resp = await _http.PostAsync($"{_brokerUrl}/api/v1/agents/exec",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return $"{{\"error\":\"exec HTTP {(int)resp.StatusCode}\",\"detail\":{JsonSerializer.Serialize(Truncate(raw, 300))}}}";

        // broker 回 {success, message, data:{success,result,...}}；給 LLM 看的是 inner data
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out var inner))
                return inner.GetRawText();
        }
        catch { }
        return raw;
    }

    /// <summary>
    /// route → capability_id 的對照（與 BrokerDbInitializer.SeedCapabilities 同步）。
    /// 之後若擴更多 capability，只要在 broker 種子加新行、然後在這裡補一行即可。
    /// </summary>
    private static string? RouteToCapabilityId(string route) => route switch
    {
        "read_file"               => "file.read",
        "list_directory"          => "file.list",
        "search_files"            => "file.search_name",
        "search_content"          => "file.search_content",
        "list_agents"             => "agent.list",
        "memory_store"            => "memory.write",
        "memory_retrieve"         => "memory.read",
        "memory_delete"           => "memory.delete",
        "memory_fulltext_search"  => "memory.fulltext_search",
        "memory_semantic_search"  => "memory.semantic_search",
        "rag_retrieve"            => "rag.retrieve",
        "web_search"              => "web.search",
        "web_search_duckduckgo"   => "web.search.duckduckgo",
        "web_search_google"       => "web.search.google",
        "web_fetch"               => "web.fetch",
        "conv_log_append"         => "conv.log.write",
        "conv_log_read"           => "conv.log.read",
        _                         => null
    };

    /// <summary>表示 LLM 回的一個 tool_call。</summary>
    private record ToolCall(string Id, string Name, string Arguments);

    /// <summary>單次打 broker /llm-proxy/chat（可選擇是否帶 tools）。</summary>
    private async Task<(string content, List<ToolCall>? toolCalls, string model, int evalTokens)> CallLlmWithToolsAsync(
        List<object> messages, bool allowTools)
    {
        var bodyObj = new Dictionary<string, object?>
        {
            ["messages"]    = messages,
            ["temperature"] = 0.3,
            ["max_tokens"]  = 800,
            ["task_id"]     = string.IsNullOrEmpty(_taskId) ? null : _taskId
        };
        if (allowTools && _tools.HasValue)
            bodyObj["tools"] = _tools.Value;

        var bodyJson = JsonSerializer.Serialize(bodyObj);
        var resp = await _http.PostAsync($"{_brokerUrl}/api/v1/llm-proxy/chat",
            new StringContent(bodyJson, Encoding.UTF8, "application/json"));

        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogError("LLM proxy {S}: {B}", (int)resp.StatusCode, Truncate(raw, 400));
            return ("", null, "", 0);
        }

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return ("", null, "", 0);

        var content    = data.TryGetProperty("content",    out var c)  ? (c.GetString() ?? "") : "";
        var model      = data.TryGetProperty("model",      out var m)  ? (m.GetString() ?? "") : "";
        var evalTokens = data.TryGetProperty("eval_count", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : 0;

        List<ToolCall>? calls = null;
        if (data.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array && tc.GetArrayLength() > 0)
        {
            calls = new List<ToolCall>();
            foreach (var item in tc.EnumerateArray())
            {
                var id   = item.TryGetProperty("id",   out var i) ? (i.GetString() ?? "") : Guid.NewGuid().ToString("N")[..12];
                var name = "";
                var argsStr = "{}";
                if (item.TryGetProperty("function", out var f))
                {
                    if (f.TryGetProperty("name",      out var n)) name    = n.GetString() ?? "";
                    if (f.TryGetProperty("arguments", out var a)) argsStr = a.ValueKind == JsonValueKind.String ? (a.GetString() ?? "{}") : a.GetRawText();
                }
                if (!string.IsNullOrEmpty(name))
                    calls.Add(new ToolCall(id, name, argsStr));
            }
        }

        return (content, calls, model, evalTokens);
    }

    private void PrintReply(string label, string content)
    {
        _log.LogInformation("┌─ {Label} ──────────────────────────────", label);
        foreach (var line in content.Split('\n'))
            _log.LogInformation("│ {Line}", line);
        _log.LogInformation("└────────────────────────────────────────────");
    }

    private string BuildSystemPrompt() => string.Format(
        """
        你是一個跑在 Bricks4Agent 平台容器裡的 AI Agent。
        身份：principal_id={0}, task_id={1}, role_id={2}, agent_id={3}。

        【工具呼叫規則 - MVP-2】
        - 你被授予的能力會以 OpenAI function-calling 形式提供（每個 function.name 是 broker dispatcher 的 route 名）
        - 需要查資料、記事、搜尋網路、讀檔時，**必須**呼叫對應的 function，不要憑空編造
        - 使用 memory_store 把使用者要你記住的事情存起來；之後 memory_retrieve 取回（用相同的 key）
        - 工具呼叫的結果會回到你這邊作為 tool 訊息，整合後再給使用者最終文字答覆
        - 若工具呼叫失敗（result.error 有值），用人話跟使用者說失敗了，不要再重試同一個工具同一組參數

        所有 LLM 呼叫和工具呼叫都會被 broker 記下來（給管理者觀察用）。
        回答時保持簡潔、直接、繁體中文。
        """,
        Safe(_principalId), Safe(_taskId), Safe(_roleId), Safe(_agentId));

    private static string Safe(string v) => string.IsNullOrEmpty(v) ? "(未提供)" : v;
    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
