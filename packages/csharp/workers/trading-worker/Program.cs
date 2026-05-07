using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BrokerCore.Services;
using WorkerSdk;
using TradingWorker.Exchange;
using TradingWorker.Handlers;
using TradingWorker.Services;
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
    WorkerId                 = string.IsNullOrEmpty(config.GetValue<string>("Worker:WorkerId"))
                               ? $"trading-wkr-{Guid.NewGuid():N}"[..20]
                               : config.GetValue<string>("Worker:WorkerId")!,
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

// ── 機密來源總覽（一次 log 標示哪些 secret 走 file mount、哪些走 env、哪些缺）──
config.LogSecretSummary(logger,
    "Worker:Trading:Alpaca:ApiKey",
    "Worker:Trading:Alpaca:ApiSecret",
    "Worker:Trading:Binance:ApiKey",
    "Worker:Trading:Binance:ApiSecret",
    "Worker:Auth:SharedSecret");

// ── 交易所客戶端 ──────────────────────────────────────────────────────
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var clients = new Dictionary<string, IExchangeClient>();

// Alpaca（美股）
if (config.GetValue("Worker:Trading:Alpaca:Enabled", false))
{
    // GetSecret 支援 Docker secrets：若 env 設了 `Worker__Trading__Alpaca__ApiKeyFile=/run/secrets/alpaca_key`
    // 會去讀檔；沒設就 fallback 到 ApiKey 直接讀，向後相容
    var alpacaKey    = config.GetSecret("Worker:Trading:Alpaca:ApiKey")    ?? "";
    var alpacaSecret = config.GetSecret("Worker:Trading:Alpaca:ApiSecret") ?? "";
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
    var binanceKey    = config.GetSecret("Worker:Trading:Binance:ApiKey")    ?? "";
    var binanceSecret = config.GetSecret("Worker:Trading:Binance:ApiSecret") ?? "";
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

// ── Fill Poller（背景輪詢未成交訂單，把 fill 寫進 trades 表）──────────
var fillPollerLogger = loggerFactory.CreateLogger<FillPollerService>();
var fillPollerInterval = config.GetValue("Worker:Trading:FillPollIntervalSec", 30);
var fillPoller = new FillPollerService(clients, tradingDb, fillPollerLogger, fillPollerInterval);
var fillPollerTask = clients.Count > 0
    ? Task.Run(() => fillPoller.RunAsync(cts.Token), cts.Token)
    : Task.CompletedTask;  // 沒任何 exchange 設定就不啟動

logger.LogInformation(
    "TradingWorker starting: broker={Host}:{Port}, exchanges=[{Exchanges}], fillPoller={Enabled}",
    options.BrokerHost, options.BrokerPort, string.Join(", ", clients.Keys), clients.Count > 0);

await host.RunAsync(cts.Token);
// host.RunAsync 退出後 cts 已被 cancel；等 fill poller 收尾
try { await fillPollerTask; } catch (OperationCanceledException) { }
