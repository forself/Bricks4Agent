using System.Text;
using LineWorker;
using LineWorker.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

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
var webhookLogger = loggerFactory.CreateLogger<WebhookReceiver>();
var dispatcherLogger = loggerFactory.CreateLogger<InboundDispatcher>();

var channelAccessToken = config.GetValue<string>("Line:ChannelAccessToken") ?? "";
var channelSecret = config.GetValue<string>("Line:ChannelSecret") ?? "";
var defaultRecipientId = config.GetValue<string>("Line:DefaultRecipientId") ?? "";
var webhookPort = config.GetValue("Line:WebhookPort", 5357);
var webhookHost = config.GetValue<string>("Line:WebhookHost") ?? "localhost";
var audioTempPath = config.GetValue<string>("Line:AudioTempPath") ?? "./audio_temp";
var allowedUserIds = config.GetValue<string>("Line:AllowedUserIds") ?? defaultRecipientId;

if (string.IsNullOrEmpty(channelAccessToken) || string.IsNullOrEmpty(channelSecret))
{
    logger.LogError("LINE ChannelAccessToken and ChannelSecret are required.");
    logger.LogError("Set via appsettings.json or env: WORKER_Line__ChannelAccessToken, WORKER_Line__ChannelSecret");
    return;
}

using var lineApi = new LineApiClient(channelAccessToken, channelSecret);

var brokerApiUrl = config.GetValue<string>("Broker:ApiUrl") ?? "http://localhost:5000";

using var webhookReceiver = new WebhookReceiver(webhookPort, webhookHost, lineApi, audioTempPath, webhookLogger);
var notificationPollMs = config.GetValue("Line:NotificationPollIntervalMs", 5000);
var notificationPollInterval = TimeSpan.FromMilliseconds(Math.Max(1000, notificationPollMs));
var inboundDispatcher = new InboundDispatcher(webhookReceiver, lineApi, allowedUserIds, brokerApiUrl, dispatcherLogger, notificationPollInterval);

var options = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"line-wkr-{Guid.NewGuid():N}"[..20],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5)
};

var host = new WorkerHost(options, logger);

host.RegisterHandler(new SendMessageHandler(lineApi, defaultRecipientId));
host.RegisterHandler(new SendNotificationHandler(lineApi, defaultRecipientId));
host.RegisterHandler(new SendAudioHandler(lineApi, defaultRecipientId));
host.RegisterHandler(new RequestApprovalHandler(lineApi, inboundDispatcher, defaultRecipientId));
host.RegisterHandler(new ReadMessagesHandler());

logger.LogInformation(
    "LineWorker starting: broker={Host}:{Port} brokerApi={ApiUrl} webhook={WebhookHost}:{WebhookPort} recipient={Recipient}",
    options.BrokerHost,
    options.BrokerPort,
    brokerApiUrl,
    webhookHost,
    webhookPort,
    string.IsNullOrEmpty(defaultRecipientId) ? "(not set)" : defaultRecipientId[..Math.Min(8, defaultRecipientId.Length)] + "...");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

var brokerTask = Task.Run(async () =>
{
    try
    {
        await host.RunAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        logger.LogWarning("Broker connection stopped: {Msg}", ex.Message);
    }
}, cts.Token);

logger.LogInformation("Starting webhook receiver and inbound dispatcher (broker connection runs in background)...");

await Task.WhenAll(
    webhookReceiver.RunAsync(cts.Token),
    inboundDispatcher.RunAsync(cts.Token),
    brokerTask
);
