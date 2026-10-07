using GenerationWorker.Handlers;
using GenerationWorker.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;

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
var handlerLogger = loggerFactory.CreateLogger("GenerationWorker");

var workerAuthType = config.GetValue<string>("Worker:Auth:WorkerType") ?? "generation-worker";
var workerAuthKeyId = config.GetValue<string>("Worker:Auth:KeyId") ?? "";
var workerAuthSharedSecret = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "";

// ── 生成設定 ──
// 輸出位置只由 Generation:OutputRoot 與 grant scope 決定；node 與生成器位置只由設定決定。
var configuredToolsRoot = config.GetValue<string>("Generation:ToolsRoot");
var generationOptions = new GenerationWorkerOptions
{
    NodePath = GenerationWorkerOptions.ResolveNodePath(config.GetValue<string>("Generation:NodePath")),
    ToolsRoot = string.IsNullOrWhiteSpace(configuredToolsRoot) ? string.Empty : Path.GetFullPath(configuredToolsRoot),
    OutputRoot = config.GetValue<string>("Generation:OutputRoot") ?? string.Empty,
    QueryTimeout = TimeSpan.FromSeconds(config.GetValue("Generation:QueryTimeoutSeconds", 30)),
    BuildTimeout = TimeSpan.FromSeconds(config.GetValue("Generation:BuildTimeoutSeconds", 120)),
    MaxStdoutBytes = config.GetValue("Generation:MaxStdoutBytes", 4 * 1024 * 1024),
};

var configurationError = generationOptions.Validate();
if (configurationError != null)
{
    logger.LogError("GenerationWorker configuration error: {Error}", configurationError);
    return 2;
}

Directory.CreateDirectory(generationOptions.OutputRoot);

var options = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"gen-wkr-{Guid.NewGuid():N}"[..20],
    // 一次只處理一個請求：生成是 CPU 與磁碟密集的確定性工作，並以 requestId 目錄做冪等。
    MaxConcurrent = 1,
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType = workerAuthType,
    WorkerAuthKeyId = workerAuthKeyId,
    WorkerAuthSharedSecret = workerAuthSharedSecret
};

// ── 建立 WorkerHost ──
var host = new WorkerHost(options, logger);
var cli = new NodeGeneratorCli(generationOptions, handlerLogger);

// ── 註冊生成 Handlers ──
host.RegisterHandler(new CatalogQueryHandler(generationOptions, cli, handlerLogger));
host.RegisterHandler(new DefinitionValidateHandler(generationOptions, cli, handlerLogger));
host.RegisterHandler(new ScaffoldGenerateHandler(generationOptions, cli, handlerLogger));

// ── 啟動 ──
logger.LogInformation(
    "GenerationWorker starting: broker={Host}:{Port} maxConcurrent=1",
    options.BrokerHost, options.BrokerPort);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

await host.RunAsync(cts.Token);
return 0;
