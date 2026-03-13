using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using FileWorker.Handlers;

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

// ── Worker 配置 ──
var sandboxRoot = config.GetValue<string>("Worker:SandboxRoot") ?? ".";
var options = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"file-wkr-{Guid.NewGuid():N}"[..20],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5)
};

// ── 建立 WorkerHost ──
var host = new WorkerHost(options, logger);

// ── 註冊 Handlers（4 讀取 + 2 寫入） ──
host.RegisterHandler(new ReadFileHandler(sandboxRoot));
host.RegisterHandler(new ListDirHandler(sandboxRoot));
host.RegisterHandler(new SearchFilesHandler(sandboxRoot));
host.RegisterHandler(new SearchContentHandler(sandboxRoot));
host.RegisterHandler(new WriteFileHandler(sandboxRoot));
host.RegisterHandler(new DeleteFileHandler(sandboxRoot));

// ── 啟動 ──
logger.LogInformation(
    "FileWorker starting: broker={Host}:{Port} sandbox={Sandbox}",
    options.BrokerHost, options.BrokerPort, Path.GetFullPath(sandboxRoot));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

await host.RunAsync(cts.Token);
