using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkerSdk;
using StrategyWorker.Engine;
using StrategyWorker.Handlers;

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
                               ? $"strategy-wkr-{Guid.NewGuid():N}"[..20]
                               : config.GetValue<string>("Worker:WorkerId")!,
    MaxConcurrent            = config.GetValue("Worker:MaxConcurrent", 4),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType               = "strategy-worker",
    WorkerAuthKeyId          = config.GetValue<string>("Worker:Auth:KeyId") ?? "",
    WorkerAuthSharedSecret   = config.GetValue<string>("Worker:Auth:SharedSecret") ?? "",
};

// ── 策略引擎 ──────────────────────────────────────────────────────────
var strategies = new Dictionary<string, IStrategy>
{
    ["sma_cross"]       = new SmaCrossStrategy(),
    ["rsi_oversold"]    = new RsiStrategy(),
    ["macd_divergence"] = new MacdStrategy(),
    ["composite"]       = CompositeStrategy.Default(),
    ["multi_timeframe"] = new MultiTimeframeStrategy(),
};

// Ensemble 必須在 constituents 都註冊好之後才能建（動態權重 by Sharpe）
strategies["ensemble"] = new WeightedEnsembleStrategy(new List<IStrategy>
{
    strategies["sma_cross"],
    strategies["rsi_oversold"],
    strategies["macd_divergence"],
    strategies["multi_timeframe"],
});

// LLM 策略（選用）
if (config.GetValue("Worker:Strategy:Llm:Enabled", false))
{
    var llmBaseUrl = config.GetValue<string>("Worker:Strategy:Llm:BaseUrl") ?? "";
    var llmApiKey  = config.GetValue<string>("Worker:Strategy:Llm:ApiKey") ?? "";
    var llmModel   = config.GetValue("Worker:Strategy:Llm:Model", "gemini-2.0-flash")!;

    if (!string.IsNullOrEmpty(llmBaseUrl))
    {
        var llmLogger = loggerFactory.CreateLogger<LlmStrategy>();
        var llmHttp   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        strategies["llm"] = new LlmStrategy(llmHttp, llmLogger, llmBaseUrl, llmApiKey, llmModel);
        logger.LogInformation("LLM strategy enabled: model={Model}", llmModel);

        var newsLogger = loggerFactory.CreateLogger<NewsSentimentStrategy>();
        var newsHttp   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        strategies["news_sentiment"] = new NewsSentimentStrategy(newsHttp, newsLogger, llmApiKey, llmModel);
        logger.LogInformation("News sentiment strategy enabled");
    }
}

logger.LogInformation("Available strategies: [{Strategies}]", string.Join(", ", strategies.Keys));

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
host.RegisterHandler(new StrategySignalHandler(strategies));

logger.LogInformation(
    "StrategyWorker starting: broker={Host}:{Port}",
    options.BrokerHost, options.BrokerPort);

await host.RunAsync(cts.Token);
