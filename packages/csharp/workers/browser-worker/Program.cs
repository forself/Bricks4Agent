using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using BrowserWorker;
using BrowserWorker.Handlers;

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

// ── Browser 配置 ──
var browserOptions = new BrowserWorkerOptions
{
    Headless = config.GetValue("Browser:Headless", true),
    DefaultTimeoutMs = config.GetValue("Browser:DefaultTimeoutMs", 30000f),
    NavigationTimeoutMs = config.GetValue("Browser:NavigationTimeoutMs", 30000f),
    MaxConcurrentPages = config.GetValue("Browser:MaxConcurrentPages", 3),
    UserAgent = config.GetValue<string>("Browser:UserAgent") ?? "Bricks4Agent-BrowserWorker/1.0",
    MaxContentLength = config.GetValue("Browser:MaxContentLength", 8000),
    ScreenshotOnEvidence = config.GetValue("Browser:ScreenshotOnEvidence", false)
};

// ── Playwright browser 服務 ──
var browserLogger = loggerFactory.CreateLogger<PlaywrightBrowserService>();
await using var browserService = new PlaywrightBrowserService(browserOptions, browserLogger);

// ── Worker 配置 ──
var workerOptions = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"browser-wkr-{Guid.NewGuid():N}"[..24],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 2),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5)
};

// ── 建立 WorkerHost ──
var host = new WorkerHost(workerOptions, logger);

// ── 註冊 Handlers ──
var readHandlerLogger = loggerFactory.CreateLogger<BrowserReadHandler>();
host.RegisterHandler(new BrowserReadHandler(browserService, readHandlerLogger));

// ── 啟動 ──
logger.LogInformation(
    "BrowserWorker starting: broker={Host}:{Port} headless={Headless} maxPages={MaxPages}",
    workerOptions.BrokerHost, workerOptions.BrokerPort, browserOptions.Headless, browserOptions.MaxConcurrentPages);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

// 預先初始化 Playwright（不要等到第一個請求才初始化）
try
{
    await browserService.EnsureInitializedAsync();
    logger.LogInformation("Playwright browser pre-initialized successfully.");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Playwright pre-initialization failed. Will retry on first request.");
}

await host.RunAsync(cts.Token);
