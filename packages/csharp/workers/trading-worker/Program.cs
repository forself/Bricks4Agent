using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using TradingWorker.Exchange;
using TradingWorker.Handlers;
using TradingWorker.Storage;

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

var logger = loggerFactory.CreateLogger<WorkerHost>();

// ── Worker SDK 選項 ───────────────────────────────────────────────────
var options = new WorkerHostOptions
{
    BrokerHost               = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort               = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId                 = config.GetValue<string>("Worker:WorkerId")
                               ?? $"trading-wkr-{Guid.NewGuid():N}"[..20],
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "trading-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

// ── 資料庫 ────────────────────────────────────────────────────────────
var dbPath    = config.GetValue("Worker:Trading:DbPath", "trading.db")!;
var dbLogger  = loggerFactory.CreateLogger<TradingDbStorage>();
var tradingDb = new TradingDbStorage(dbPath, dbLogger);

// ── 交易所客戶端 ──────────────────────────────────────────────────────
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var clients = new Dictionary<string, IExchangeClient>();

// Alpaca（美股）
if (config.GetValue("Worker:Trading:Alpaca:Enabled", false))
{
    var alpacaKey    = config.GetValue<string>("Worker:Trading:Alpaca:ApiKey") ?? "";
    var alpacaSecret = config.GetValue<string>("Worker:Trading:Alpaca:ApiSecret") ?? "";
    var alpacaPaper  = config.GetValue("Worker:Trading:Alpaca:IsPaper", true);

    if (!string.IsNullOrEmpty(alpacaKey) && !string.IsNullOrEmpty(alpacaSecret))
    {
        var alpacaLogger = loggerFactory.CreateLogger<AlpacaClient>();
        clients["alpaca"] = new AlpacaClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            alpacaLogger, alpacaKey, alpacaSecret, alpacaPaper);
        logger.LogInformation("Alpaca exchange enabled (paper={IsPaper})", alpacaPaper);
    }
    else
    {
        logger.LogWarning("Alpaca enabled but API key/secret not configured");
    }
}

// Binance（加密貨幣）
if (config.GetValue("Worker:Trading:Binance:Enabled", false))
{
    var binanceKey    = config.GetValue<string>("Worker:Trading:Binance:ApiKey") ?? "";
    var binanceSecret = config.GetValue<string>("Worker:Trading:Binance:ApiSecret") ?? "";
    var binanceTest   = config.GetValue("Worker:Trading:Binance:IsTestnet", true);

    if (!string.IsNullOrEmpty(binanceKey) && !string.IsNullOrEmpty(binanceSecret))
    {
        var binanceLogger = loggerFactory.CreateLogger<BinanceClient>();
        clients["binance"] = new BinanceClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            binanceLogger, binanceKey, binanceSecret, binanceTest);
        logger.LogInformation("Binance exchange enabled (testnet={IsTestnet})", binanceTest);
    }
    else
    {
        logger.LogWarning("Binance enabled but API key/secret not configured");
    }
}

if (clients.Count == 0)
    logger.LogWarning("No exchange clients configured — trading capabilities will fail. Set Alpaca or Binance config.");

// ── 取消 Token ────────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

// ── 建立 WorkerHost 並註冊能力 ────────────────────────────────────────
var host = new WorkerHost(options, logger);
host.RegisterHandler(new TradingOrderHandler(clients, tradingDb));
host.RegisterHandler(new TradingAccountHandler(clients, tradingDb));

logger.LogInformation(
    "TradingWorker starting: broker={Host}:{Port}, exchanges=[{Exchanges}]",
    options.BrokerHost, options.BrokerPort, string.Join(", ", clients.Keys));

await host.RunAsync(cts.Token);
