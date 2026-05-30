using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using CodeExecWorker.Handlers;

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
                               ? $"codeexec-wkr-{Guid.NewGuid():N}"[..20]
                               : config.GetValue<string>("Worker:WorkerId")!,
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 2),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "code-exec-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

var handler = new CodeExecHandler(
    maxTimeout: config.GetValue("Worker:CodeExec:MaxTimeoutSeconds", 30),
    memKB:      config.GetValue("Worker:CodeExec:MemLimitKB", 262144),
    nproc:      config.GetValue("Worker:CodeExec:MaxProcs", 64),
    fsizeKB:    config.GetValue("Worker:CodeExec:MaxFileSizeKB", 10240),
    outCap:     config.GetValue("Worker:CodeExec:OutputCapBytes", 16384),
    log:        logger);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); logger.LogInformation("Shutdown signal received."); };

var host = new WorkerHost(options, logger);
host.RegisterHandler(handler);

logger.LogInformation("CodeExecWorker starting: broker={Host}:{Port} (capability code.exec, sandbox timeout/ulimit)",
    options.BrokerHost, options.BrokerPort);

await host.RunAsync(cts.Token);
