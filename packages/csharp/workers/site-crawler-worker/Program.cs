using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;
using WorkerSdk;

const string DefaultWorkerType = "site-crawler-worker";
const string DefaultWorkerAuthKeyId = "REPLACE_WITH_SITE_CRAWLER_WORKER_KEY_ID";
const string DefaultWorkerAuthSharedSecret = "REPLACE_WITH_SITE_CRAWLER_WORKER_SHARED_SECRET";

// `reconstruct` subcommand: run one crawl -> reconstruct-package locally, without a broker.
// Exercises the same production SiteReconstructPackageHandler the broker path uses.
//   dotnet run --project packages/csharp/workers/site-crawler-worker -- reconstruct --url <start-url> [--out <dir>] [...]
var isReconstruct = args.Length > 0 && string.Equals(args[0], "reconstruct", StringComparison.OrdinalIgnoreCase);
var configArgs = isReconstruct ? args[1..] : args;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(configArgs)
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<WorkerHost>();

var pageFetcher = new HttpPageFetcher();
var extractor = new DeterministicSiteExtractor();
await using var visualRenderer = VisualPageRendererFactory.Create(config, loggerFactory);
var crawlerService = new SiteCrawlerService(
    pageFetcher,
    extractor,
    visualRenderer,
    loggerFactory.CreateLogger<SiteCrawlerService>());
var componentLibraryLoader = new ComponentLibraryLoader();
var componentLibrary = componentLibraryLoader.Load(config.GetValue<string>("Generator:ComponentLibraryPath"));
var generatorConverter = new SiteGeneratorConverter(componentLibrary);
var packageGenerator = new StaticSitePackageGenerator();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

if (isReconstruct)
{
    return await RunReconstructCliAsync();
}

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

host.RegisterHandler(new SiteCrawlSourceHandler(
    crawlerService,
    loggerFactory.CreateLogger<SiteCrawlSourceHandler>()));
host.RegisterHandler(new SiteGeneratePackageHandler(
    generatorConverter,
    packageGenerator,
    loggerFactory.CreateLogger<SiteGeneratePackageHandler>()));
host.RegisterHandler(new SiteReconstructPackageHandler(
    crawlerService,
    generatorConverter,
    packageGenerator,
    loggerFactory.CreateLogger<SiteReconstructPackageHandler>()));

logger.LogInformation(
    "SiteCrawlerWorker starting: broker={Host}:{Port} maxConcurrent={MaxConcurrent}",
    options.BrokerHost,
    options.BrokerPort,
    options.MaxConcurrent);

await host.RunAsync(cts.Token);
return 0;

string GetConfiguredValue(string key, string fallback)
{
    var value = config.GetValue<string>(key);
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}

async Task<int> RunReconstructCliAsync()
{
    var url = config.GetValue<string>("url");
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.Error.WriteLine(
            "usage: site-crawler-worker reconstruct --url <start-url> [--out <dir>] [--package-name <name>]\n" +
            "         [--max-pages <n=8>] [--max-depth <n=1>] [--visual <true|false>]\n" +
            "         [--archive <true|false>] [--quality-gate <true|false>] [--json]");
        return 2;
    }

    var maxPages = config.GetValue("max-pages", 8);
    var request = new SiteReconstructPackageRequest
    {
        RequestId = $"cli-{Guid.NewGuid():N}"[..16],
        StartUrl = url,
        Scope = new SiteCrawlScope
        {
            Kind = "link_depth",
            MaxDepth = config.GetValue("max-depth", 1),
            SameOriginOnly = true,
            PathPrefixLock = false,
        },
        Capture = new SiteCrawlCaptureOptions
        {
            Html = true,
            RenderedDom = config.GetValue("visual", true),
            RenderedDomMaxPages = Math.Min(maxPages, 5),
        },
        Budgets = new SiteCrawlBudgets
        {
            MaxPages = maxPages,
            MaxTotalBytes = 40L * 1024 * 1024,
            WallClockTimeoutSeconds = config.GetValue("timeout-seconds", 240),
        },
        OutputDirectory = config.GetValue<string>("out")
            ?? Path.Combine(Path.GetTempPath(), "bricks4agent-generated-sites"),
        PackageName = config.GetValue<string>("package-name") ?? "generated-site",
        EnforceQualityGate = config.GetValue("quality-gate", false),
        CreateArchive = config.GetValue("archive", true),
    };

    var handler = new SiteReconstructPackageHandler(
        crawlerService,
        generatorConverter,
        packageGenerator,
        loggerFactory.CreateLogger<SiteReconstructPackageHandler>());

    var payload = JsonSerializer.Serialize(
        new { args = request },
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    logger.LogInformation("reconstruct: crawling {Url} -> {Out}", url, request.OutputDirectory);
    var (success, resultPayload, error) = await handler.ExecuteAsync(
        request.RequestId, "site.reconstruct_package", payload, "{}", cts.Token);

    if (config.GetValue("json", false) && resultPayload is not null)
    {
        Console.WriteLine(resultPayload);
    }

    if (!success)
    {
        logger.LogError("reconstruct failed: {Error}", error);
        return 1;
    }

    var result = JsonSerializer.Deserialize<SiteReconstructPackageResult>(
        resultPayload!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    if (result is not null)
    {
        Console.WriteLine($"OK  pages={result.PageCount} excluded={result.ExcludedCount}");
        Console.WriteLine($"    output  : {result.Package.OutputDirectory}");
        Console.WriteLine($"    archive : {result.Package.ArchivePath}");
        Console.WriteLine($"    quality : passed={result.Package.QualityReport.IsPassed} "
            + $"nodes={result.Package.QualityReport.ComponentNodeCount} "
            + $"gaps={result.Package.QualityReport.ComponentRequestCount}");
    }

    return 0;
}
