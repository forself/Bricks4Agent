using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using RiskWorker.Engine;
using RiskWorker.Handlers;
using RiskWorker.Models;

// ── 設定 ──────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(args)
    .Build();

// ── 日誌 ──────────────────────────────────────────────────────────────
using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<WorkerHost>();

// ── Worker SDK 選項 ───────────────────────────────────────────────────
var options = new WorkerHostOptions
{
    BrokerHost               = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort               = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId                 = string.IsNullOrEmpty(config.GetValue<string>("Worker:WorkerId"))
                               ? $"risk-wkr-{Guid.NewGuid():N}"[..20]
                               : config.GetValue<string>("Worker:WorkerId")!,
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "risk-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

// ── 風控規則 ────────────────────────────────────────────────────────
// 規則表的單一事實來源是 RiskEngine.DefaultRules()——這裡只負責載入後
// 對舊的 r1~r6 用環境變數覆蓋 threshold（向後相容；之前 r7+ 完全沒被讀到）。
// 完整動態管理走 admin UI 的 set_rules（會整批覆蓋 _rules）。
var rules = RiskEngine.DefaultRules();
var thresholdOverrides = new Dictionary<string, decimal?>
{
    ["r1"] = config.GetValue<decimal?>("Worker:Risk:MaxPositionSize"),
    ["r2"] = config.GetValue<decimal?>("Worker:Risk:MaxPortfolioPct"),
    ["r3"] = config.GetValue<decimal?>("Worker:Risk:MaxOrderSize"),
    ["r4"] = config.GetValue<decimal?>("Worker:Risk:MaxDailyLoss"),
    ["r5"] = config.GetValue<decimal?>("Worker:Risk:MaxDrawdownPct"),
    ["r6"] = config.GetValue<decimal?>("Worker:Risk:MaxDailyTrades"),
};
foreach (var rule in rules)
    if (thresholdOverrides.TryGetValue(rule.RuleId, out var ov) && ov.HasValue)
        rule.Threshold = ov.Value;

logger.LogInformation("Risk rules loaded: {Count} rules", rules.Count);
foreach (var r in rules)
    logger.LogInformation("  [{Id}] {Name}: {Type} <= {Threshold}", r.RuleId, r.Name, r.Type, r.Threshold);

// ── 取消 Token ────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

// ── 建立 WorkerHost 並註冊能力 ────────────────────────────────────────
var host = new WorkerHost(options, logger);
host.RegisterHandler(new RiskCheckHandler(rules));

logger.LogInformation(
    "RiskWorker starting: broker={Host}:{Port}",
    options.BrokerHost, options.BrokerPort);

await host.RunAsync(cts.Token);
