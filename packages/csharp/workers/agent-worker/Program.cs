using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AgentWorker;

// MVP-0：證明「平台 → 政策 → 執行」整條鏈打通了。
// MVP-1（2026-05-01）：把 hard-coded prompt 換成 inbox poll —
//   1. 從 env 讀身份（broker 在 spawn 時注入 BROKER_PRINCIPAL_ID 等）
//   2. 開機自介一次（保留 MVP-0 的「啟動時就會說一句話」體驗）
//   3. 進 poll 迴圈：每 N 秒拉 /agents/inbox/pull → 有任務就處理 → /complete 回填
//   4. 沒任務就繼續等，dashboard 看得到容器活著
//
// 之後 MVP-2 才接 LLM tool calling、agent 真的去 web.search/memory.write。

var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    b.SetMinimumLevel(LogLevel.Information);
});
var log = loggerFactory.CreateLogger("agent");

var brokerUrl   = config["BROKER_URL"]          ?? "http://broker:5000";
var principalId = config["BROKER_PRINCIPAL_ID"] ?? "";
var taskId      = config["BROKER_TASK_ID"]      ?? "";
var roleId      = config["BROKER_ROLE_ID"]      ?? "";
var greetPrompt = config["AGENT_PROMPT"]
    ?? "你是一個剛被 spawn 起來的 Agent。請用一段話自介、說明你的身份（principal/task/role）。中文回覆。";
var pollIntervalSec = int.TryParse(config["AGENT_POLL_INTERVAL_SECONDS"], out var pi) ? pi : 5;

// agentId 從 principalId 推回來：principal 是 "prn_<agentId>"
var agentId = principalId.StartsWith("prn_") ? principalId[4..] : principalId;

log.LogInformation("=== B4A Agent Runtime starting ===");
log.LogInformation("broker_url       = {Url}", brokerUrl);
log.LogInformation("agent_id         = {Aid}", string.IsNullOrEmpty(agentId)     ? "(unset)" : agentId);
log.LogInformation("principal_id     = {Pid}", string.IsNullOrEmpty(principalId) ? "(unset)" : principalId);
log.LogInformation("task_id          = {Tid}", string.IsNullOrEmpty(taskId)      ? "(unset)" : taskId);
log.LogInformation("role_id          = {Rid}", string.IsNullOrEmpty(roleId)      ? "(unset)" : roleId);
log.LogInformation("poll_interval_s  = {Sec}", pollIntervalSec);

if (string.IsNullOrEmpty(agentId))
{
    log.LogError("BROKER_PRINCIPAL_ID 沒設或格式不對 — 無法 poll inbox。容器繼續活著但不做事。");
    while (true) await Task.Delay(TimeSpan.FromMinutes(5));
}

await Task.Delay(TimeSpan.FromSeconds(2)); // 給 broker 起來緩衝

var runtime = new AgentRuntime(brokerUrl, agentId, principalId, taskId, roleId, log);

// MVP-2：載入 capability_grants 對應的 OpenAI tool spec — 後續 LLM call 才能 tool calling
try { await runtime.LoadToolsAsync(); }
catch (Exception ex) { log.LogWarning(ex, "tool spec 載入失敗（MVP-2 工具呼叫會停用，仍可純對話）"); }

// 啟動自介（純文字，不啟用 tool）
try
{
    await runtime.RunGreetingAsync(greetPrompt);
}
catch (Exception ex)
{
    log.LogError(ex, "啟動自介失敗，但 inbox poll 迴圈仍會繼續");
}

log.LogInformation("=== entering inbox poll loop（每 {Sec}s）===", pollIntervalSec);
var idleTicks = 0;
while (true)
{
    try
    {
        var processed = await runtime.PollAndProcessOnceAsync();
        if (processed)
        {
            idleTicks = 0;
        }
        else
        {
            idleTicks++;
            // 60 個 idle tick（預設 5 分鐘）印一次心跳，避免 log 太空
            if (idleTicks % 60 == 0)
                log.LogInformation("idle — waiting for tasks（{Min} min）", idleTicks * pollIntervalSec / 60);
        }
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "poll loop 錯誤（會繼續嘗試）");
    }
    await Task.Delay(TimeSpan.FromSeconds(pollIntervalSec));
}
