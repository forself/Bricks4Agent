using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using TelemetryWorker.Handlers;
using TelemetryWorker.Sampler;
using TelemetryWorker.Storage;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(args)
    .Build();

using var loggerFactory = LoggerFactory.Create(b => { b.AddConsole(); b.SetMinimumLevel(LogLevel.Information); });
var logger = loggerFactory.CreateLogger<WorkerHost>();

var options = new WorkerHostOptions
{
    BrokerHost               = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort               = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId                 = string.IsNullOrEmpty(config.GetValue<string>("Worker:WorkerId"))
                               ? $"telemetry-wkr-{Guid.NewGuid():N}"[..20]
                               : config.GetValue<string>("Worker:WorkerId")!,
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 2),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "telemetry-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

// ── Telemetry 設定 ──
var brokerApiUrl   = config.GetValue("Worker:Telemetry:BrokerApiUrl", "http://localhost:5000")!;
var sampleInterval = config.GetValue("Worker:Telemetry:SampleIntervalSeconds", 15);
var dbPath         = config.GetValue("Worker:Telemetry:DbPath", "telemetry.db")!;
var retentionHours = config.GetValue("Worker:Telemetry:RetentionHours", 168);

var db = new TelemetryDb(dbPath, loggerFactory.CreateLogger<TelemetryDb>());

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
http.DefaultRequestHeaders.Add("User-Agent", "B4A-TelemetryWorker/1.0");

var sampler = new TelemetrySampler(db, http, brokerApiUrl, sampleInterval, retentionHours,
    loggerFactory.CreateLogger("TelemetrySampler"));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); logger.LogInformation("Shutdown signal received."); };

// 背景取樣迴圈
_ = sampler.RunAsync(cts.Token);

var host = new WorkerHost(options, logger);
host.RegisterHandler(new TelemetryHistoryHandler(db));

logger.LogInformation(
    "TelemetryWorker starting: broker={Host}:{Port}, sampling {Url} every {Sec}s → {Db}",
    options.BrokerHost, options.BrokerPort, brokerApiUrl, sampleInterval, dbPath);

await host.RunAsync(cts.Token);
