using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TransportTdxWorker.Handlers;
using TransportTdxWorker.Services;
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
var workerAuthType = config.GetValue<string>("Worker:Auth:WorkerType") ?? "transport-tdx";
var workerAuthKeyId = config.GetValue<string>("Worker:Auth:KeyId") ?? string.Empty;
var workerAuthSharedSecret = config.GetValue<string>("Worker:Auth:SharedSecret") ?? string.Empty;

var options = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"transport-wkr-{Guid.NewGuid():N}"[..24],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType = workerAuthType,
    WorkerAuthKeyId = workerAuthKeyId,
    WorkerAuthSharedSecret = workerAuthSharedSecret
};

var host = new WorkerHost(options, logger);
var tdxOptions = new TdxOptions
{
    ClientId = config.GetValue<string>("Tdx:ClientId") ?? string.Empty,
    ClientSecret = config.GetValue<string>("Tdx:ClientSecret") ?? string.Empty,
    AuthUrl = config.GetValue<string>("Tdx:AuthUrl") ?? "https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token",
    BaseUrl = config.GetValue<string>("Tdx:BaseUrl") ?? "https://tdx.transportdata.tw/api/basic"
};
var httpClient = new HttpClient();
var tdxApiService = new TdxApiService(tdxOptions, httpClient, loggerFactory.CreateLogger<TdxApiService>());
var transportProvider = new TdxTransportProvider(
    tdxApiService,
    new TransportQueryContextResolver(),
    loggerFactory.CreateLogger<TdxTransportProvider>());

host.RegisterHandler(new TransportQueryHandler(
    new TransportQuerySufficiencyAnalyzer(),
    new TransportFollowUpBuilder(),
    new TransportRangeAnswerBuilder(),
    new TransportQueryContextResolver(),
    transportProvider));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

await host.RunAsync(cts.Token);
