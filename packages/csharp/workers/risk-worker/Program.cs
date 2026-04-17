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
    WorkerId                 = config.GetValue<string>("Worker:WorkerId")
                               ?? $"risk-wkr-{Guid.NewGuid():N}"[..20],
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "risk-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

// ── 風控規則（從設定檔載入，可透過 API 動態更新）──────────────────────
var rules = new List<RiskRule>
{
    new() { RuleId = "r1", Name = "Max Position Size",        Type = "max_position",      Threshold = config.GetValue("Worker:Risk:MaxPositionSize", 10_000m) },
    new() { RuleId = "r2", Name = "Max Portfolio Allocation",  Type = "max_portfolio_pct", Threshold = config.GetValue("Worker:Risk:MaxPortfolioPct", 25m) },
    new() { RuleId = "r3", Name = "Max Single Order",         Type = "max_order_size",    Threshold = config.GetValue("Worker:Risk:MaxOrderSize", 5_000m) },
    new() { RuleId = "r4", Name = "Max Daily Loss",           Type = "max_daily_loss",    Threshold = config.GetValue("Worker:Risk:MaxDailyLoss", 1_000m) },
    new() { RuleId = "r5", Name = "Max Drawdown",             Type = "max_drawdown_pct",  Threshold = config.GetValue("Worker:Risk:MaxDrawdownPct", 10m) },
    new() { RuleId = "r6", Name = "Max Daily Trades",         Type = "max_daily_trades",  Threshold = config.GetValue("Worker:Risk:MaxDailyTrades", 20m) },
};

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
