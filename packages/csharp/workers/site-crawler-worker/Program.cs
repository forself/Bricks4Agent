using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Services;
using WorkerSdk;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(args)
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<WorkerHost>();
var workerAuthType = config.GetValue<string>("Worker:Auth:WorkerType") ?? "site-crawler-worker";
var workerAuthKeyId = config.GetValue<string>("Worker:Auth:KeyId") ?? string.Empty;
var workerAuthSharedSecret = config.GetValue<string>("Worker:Auth:SharedSecret") ?? string.Empty;

var options = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"site-crawler-wkr-{Guid.NewGuid():N}"[..24],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 2),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType = workerAuthType,
    WorkerAuthKeyId = workerAuthKeyId,
    WorkerAuthSharedSecret = workerAuthSharedSecret
};

var host = new WorkerHost(options, logger);

var pageFetcher = new HttpPageFetcher();
var extractor = new DeterministicSiteExtractor();
var crawlerService = new SiteCrawlerService(
    pageFetcher,
    extractor,
    loggerFactory.CreateLogger<SiteCrawlerService>());

host.RegisterHandler(new SiteCrawlSourceHandler(
    crawlerService,
    loggerFactory.CreateLogger<SiteCrawlSourceHandler>()));

logger.LogInformation(
    "SiteCrawlerWorker starting: broker={Host}:{Port} maxConcurrent={MaxConcurrent}",
    options.BrokerHost,
    options.BrokerPort,
    options.MaxConcurrent);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

await host.RunAsync(cts.Token);
