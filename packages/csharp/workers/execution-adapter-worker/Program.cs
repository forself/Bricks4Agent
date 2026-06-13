using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using ExecutionAdapterWorker.Handlers;

// ── 讀取配置 ──
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(args)
    .Build();

// ── 日誌 ──
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<WorkerHost>();

var workerAuthType = config.GetValue<string>("Worker:Auth:WorkerType") ?? "execution-adapter-worker";
var workerAuthKeyId = config.GetValue<string>("Worker:Auth:KeyId") ?? "";
var workerAuthSharedSecret = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "";

// ── Worker 配置 ──
var workspaceRoot = config.GetValue<string>("Worker:SandboxRoot") ?? ".";
var evidenceRoot = config.GetValue<string>("Worker:EvidenceRoot");

// build/test 白名單（config 覆寫；未設則用內建預設）
var whitelist = config.GetSection("Worker:BuildTest:Whitelist").Get<string[]>();

var options = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"exec-wkr-{Guid.NewGuid():N}"[..20],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 2),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType = workerAuthType,
    WorkerAuthKeyId = workerAuthKeyId,
    WorkerAuthSharedSecret = workerAuthSharedSecret
};

// ── 建立 WorkerHost ──
var host = new WorkerHost(options, logger);

// ── 註冊執行配接器 Handlers ──
host.RegisterHandler(new RepoApplyPatchHandler(workspaceRoot, evidenceRoot));
host.RegisterHandler(new BuildTestRunHandler(workspaceRoot, whitelist, evidenceRoot));

// ── 啟動 ──
logger.LogInformation(
    "ExecutionAdapterWorker starting: broker={Host}:{Port} workspace={Workspace}",
    options.BrokerHost, options.BrokerPort, Path.GetFullPath(workspaceRoot));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

await host.RunAsync(cts.Token);
