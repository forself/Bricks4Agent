using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Services;
using WorkerSdk;

const string DefaultWorkerType = "site-crawler-worker";
const string DefaultWorkerAuthKeyId = "REPLACE_WITH_SITE_CRAWLER_WORKER_KEY_ID";
const string DefaultWorkerAuthSharedSecret = "REPLACE_WITH_SITE_CRAWLER_WORKER_SHARED_SECRET";

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
var workerAuthType = GetConfiguredValue("Worker:Auth:WorkerType", DefaultWorkerType);
var workerAuthKeyId = GetConfiguredValue("Worker:Auth:KeyId", DefaultWorkerAuthKeyId);
var workerAuthSharedSecret = GetConfiguredValue("Worker:Auth:SharedSecret", DefaultWorkerAuthSharedSecret);

var options = new WorkerHostOptions
{
    BrokerHost = GetConfiguredValue("Worker:BrokerHost", "localhost"),
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = GetConfiguredValue("Worker:WorkerId", $"site-crawler-wkr-{Guid.NewGuid():N}"[..24]),
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
var componentLibrary = DefaultComponentLibrary.Create();
var generatorConverter = new SiteGeneratorConverter(componentLibrary);
var packageGenerator = new StaticSitePackageGenerator();

host.RegisterHandler(new SiteCrawlSourceHandler(
    crawlerService,
    loggerFactory.CreateLogger<SiteCrawlSourceHandler>()));
host.RegisterHandler(new SiteGeneratePackageHandler(
    generatorConverter,
    packageGenerator,
    loggerFactory.CreateLogger<SiteGeneratePackageHandler>()));

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

string GetConfiguredValue(string key, string fallback)
{
    var value = config.GetValue<string>(key);
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}
