using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using QuoteWorker.Fetcher;
using QuoteWorker.Handlers;
using QuoteWorker.History;
using QuoteWorker.Queue;
using QuoteWorker.Storage;

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
    WorkerId                 = string.IsNullOrEmpty(config.GetValue<string>("Worker:WorkerId"))
                               ? $"quote-wkr-{Guid.NewGuid():N}"[..20]
                               : config.GetValue<string>("Worker:WorkerId")!,
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
// 深度回補的 crypto universe（Binance 符號、逗號分隔）。空 = 從 cryptoIds 推導。
var backfillSymbols      = config.GetValue("Worker:Quote:BackfillSymbols", "")!;

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

// ── 持久化 & 歷史 K 線 ───────────────────────────────────────────────
var dbPath       = config.GetValue("Worker:Quote:DbPath", "quote.db")!;
var dbLogger     = loggerFactory.CreateLogger<QuoteDbStorage>();
var histLogger   = loggerFactory.CreateLogger<HistoricalDataFetcher>();
var quoteDb      = new QuoteDbStorage(dbPath, dbLogger);
var histFetcher  = new HistoricalDataFetcher(httpClient, quoteDb, histLogger);

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

// ── 啟動報價持久化迴圈（每次 fetch 後自動存入 SQLite）─────────────────
var persistLogger = loggerFactory.CreateLogger("SnapshotPersistence");
_ = SnapshotPersistenceLoop.RunAsync(jobQueue, quoteDb, persistLogger, ct: cts.Token);

// ── 啟動時自動抓取歷史 K 線（增量，不重複）──────────────────────────
var startupLogger = loggerFactory.CreateLogger<StartupHistoryFetcher>();
var startupFetcher = new StartupHistoryFetcher(histFetcher, quoteDb, startupLogger, stockSymbols, cryptoIds, backfillSymbols);
_ = startupFetcher.RunOnceAsync(cts.Token);

// ── 建立 WorkerHost 並註冊能力 ────────────────────────────────────────
var host = new WorkerHost(options, logger);
host.RegisterHandler(new QuoteHistoryHandler(jobQueue));
host.RegisterHandler(new QuotePricesHandler(jobQueue));
host.RegisterHandler(new QuoteFetchNowHandler(jobQueue));
host.RegisterHandler(new QuoteOhlcvHandler(quoteDb, histFetcher));
host.RegisterHandler(new QuoteIndicatorHandler(quoteDb));
host.RegisterHandler(new QuoteBatchFetchHandler(startupFetcher));

logger.LogInformation(
    "QuoteWorker starting: broker={Host}:{Port}, fetchInterval={Interval}min, crypto=[{Crypto}], stocks=[{Stocks}]",
    options.BrokerHost, options.BrokerPort, fetchIntervalMinutes, cryptoIds, stockSymbols);

await host.RunAsync(cts.Token);
