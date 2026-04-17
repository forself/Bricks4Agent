using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using QuoteWorker.Fetcher;
using QuoteWorker.Handlers;
using QuoteWorker.Queue;

// ── 設定 ──────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(args)
    .Build();

// ── 日誌 ──────────────────────────────────────────────────────────────
using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Information);
});

var logger       = loggerFactory.CreateLogger<WorkerHost>();
var fetchLogger  = loggerFactory.CreateLogger<QuoteJobQueue>();
var httpLogger   = loggerFactory.CreateLogger<QuoteFetcher>();

// ── Worker SDK 選項 ───────────────────────────────────────────────────
var options = new WorkerHostOptions
{
    BrokerHost               = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort               = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId                 = config.GetValue<string>("Worker:WorkerId")
                               ?? $"quote-wkr-{Guid.NewGuid():N}"[..20],
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "quote-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

// ── Quote 設定 ────────────────────────────────────────────────────────
var fetchIntervalMinutes = config.GetValue("Worker:Quote:FetchIntervalMinutes", 5);
var cryptoIds            = config.GetValue("Worker:Quote:CryptoIds",   "bitcoin,ethereum,solana,dogecoin")!;
var stockSymbols         = config.GetValue("Worker:Quote:StockSymbols", "AAPL,MSFT,TSLA,NVDA")!;

// ── 建立元件 ──────────────────────────────────────────────────────────
var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
};
httpClient.DefaultRequestHeaders.Add(
    "User-Agent",
    "Mozilla/5.0 (compatible; B4A-QuoteWorker/1.0; +https://github.com/bricks4agent)");

var fetcher  = new QuoteFetcher(httpClient, httpLogger, cryptoIds, stockSymbols);
var jobQueue = new QuoteJobQueue(fetcher, fetchLogger, fetchIntervalMinutes);

// ── 取消 Token ────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

// ── 啟動背景抓取迴圈 ──────────────────────────────────────────────────
_ = jobQueue.RunAsync(cts.Token);

// ── 建立 WorkerHost 並註冊能力 ────────────────────────────────────────
var host = new WorkerHost(options, logger);
host.RegisterHandler(new QuoteHistoryHandler(jobQueue));
host.RegisterHandler(new QuotePricesHandler(jobQueue));
host.RegisterHandler(new QuoteFetchNowHandler(jobQueue));

logger.LogInformation(
    "QuoteWorker starting: broker={Host}:{Port}, fetchInterval={Interval}min, crypto=[{Crypto}], stocks=[{Stocks}]",
    options.BrokerHost, options.BrokerPort, fetchIntervalMinutes, cryptoIds, stockSymbols);

await host.RunAsync(cts.Token);
