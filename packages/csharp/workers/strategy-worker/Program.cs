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
    ["fibonacci_retracement"] = new FibonacciStrategy(),
    ["bollinger_bands"] = new BollingerStrategy(),
    ["harmonic_pattern"] = new HarmonicStrategy(),
    ["vegas_tunnel"] = new VegasTunnelStrategy(),
    // Batch A 從朋友 ai-quant-starter2 移植的 5 個 indicator-as-strategy
    ["super_trend"]     = new SuperTrendStrategy(),
    ["adx_di"]          = new AdxDiStrategy(),
    ["ichimoku"]        = new IchimokuStrategy(),
    ["rsi_stoch"]       = new StochasticStrategy(),
    ["vwap"]            = new VwapStrategy(),
    // Batch B：Price Action 形態學（6 K 線型態加權成 buy/sell/hold）
    ["price_action"]    = new PriceActionStrategy(),
    // Tier 2 batch：再 7 個 indicator-as-strategy
    ["donchian"]        = new DonchianStrategy(),
    ["keltner"]         = new KeltnerStrategy(),
    ["parabolic_sar"]   = new ParabolicSarStrategy(),
    ["cci"]             = new CciStrategy(),
    ["obv"]             = new ObvStrategy(),
    ["mfi"]             = new MfiStrategy(),
    ["chaikin_mf"]      = new ChaikinMfStrategy(),
    // SMC：機構派價格結構（BOS/CHoCH + Order Block / FVG 回測）
    ["smc"]             = new SmcStrategy(),
};

// LLM proxy 配置——ensemble arbitrator 跟 llm/news 策略共用同一份 broker URL + model
IEnsembleArbitrator? arbitrator = null;
var llmEnabled = config.GetValue("Worker:Strategy:Llm:Enabled", false);
var llmBrokerUrl = config.GetValue<string>("Worker:Strategy:Llm:BrokerUrl")
                   ?? config.GetValue<string>("Worker:Strategy:Llm:BaseUrl")
                   ?? "http://broker:5000";
var llmModel = config.GetValue("Worker:Strategy:Llm:Model", "gemini-2.0-flash")!;

if (llmEnabled)
{
    // 仲裁者跟 ensemble 互相依賴的順序：先建 arbitrator、再建 ensemble 時注入
    var arbThreshold = (decimal)config.GetValue("Worker:Strategy:Ensemble:ArbitratorThreshold", 0.6);
    var arbHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    var arbLogger = loggerFactory.CreateLogger<LlmEnsembleArbitrator>();
    arbitrator = new LlmEnsembleArbitrator(arbHttp, arbLogger, llmBrokerUrl, llmModel, arbThreshold);
    logger.LogInformation("Ensemble LLM arbitrator enabled: threshold={T:P0} model={M}",
        arbThreshold, llmModel);
}

// Ensemble 必須在 constituents 都註冊好之後才能建（動態權重 by Sharpe + 選用 LLM 仲裁）
strategies["ensemble"] = new WeightedEnsembleStrategy(new List<IStrategy>
{
    strategies["sma_cross"],
    strategies["rsi_oversold"],
    strategies["macd_divergence"],
    strategies["multi_timeframe"],
}, arbitrator: arbitrator);

// AutoSelect 也是要 constituents 都在後才能建（regime → 1 個成員執行）
strategies["auto_select"] = AutoSelectStrategy.DefaultFrom(strategies);

// LLM 策略（選用）— 走 broker 的 /api/v1/llm-proxy/chat 集中代理，
// 不再直接連 Gemini / OpenAI，這樣每次呼叫才會被 broker 的 MeteredLlmProxyService
// 記到儀表板的 LLM Proxy 分頁。
if (llmEnabled)
{
    var llmLogger = loggerFactory.CreateLogger<LlmStrategy>();
    var llmHttp   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    strategies["llm"] = new LlmStrategy(llmHttp, llmLogger, llmBrokerUrl, llmModel);
    logger.LogInformation("LLM strategy enabled (via broker proxy): broker={Url} model={Model}",
        llmBrokerUrl, llmModel);

    var newsLogger = loggerFactory.CreateLogger<NewsSentimentStrategy>();
    var newsHttp   = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    strategies["news_sentiment"] = new NewsSentimentStrategy(newsHttp, newsLogger, llmBrokerUrl, llmModel);
    logger.LogInformation("News sentiment strategy enabled (via broker proxy)");
}

logger.LogInformation("Available strategies: [{Strategies}]", string.Join(", ", strategies.Keys));

// 把 dict 包進 IStrategyRegistry——handler / list endpoint / 之後的 lab 都從這拿單一來源
var registry = new DefaultStrategyRegistry();
foreach (var s in strategies.Values) registry.Register(s);

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
host.RegisterHandler(new StrategySignalHandler(registry));

logger.LogInformation(
    "StrategyWorker starting: broker={Host}:{Port}",
    options.BrokerHost, options.BrokerPort);

await host.RunAsync(cts.Token);
