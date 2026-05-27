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
    // 正交基礎指標：Hurst 性格判斷 + 波動率擠壓突破（增加 ensemble 區別性、不跟方向型疊加）
    ["hurst_adaptive"]      = new HurstStrategy(),
    ["volatility_breakout"] = new VolatilityBreakoutStrategy(),
    // 非價格因子（方向性）：資金費率極端反轉 — 驗證 funding 單獨有沒有 OOS edge
    ["funding_extreme"]     = new FundingExtremeStrategy(),
    // [whitelist add: 2026-05-24 AnthonyLee] 可用量化批次：經 broad-universe(12 檔)walk-forward OOS
    // 驗證為「可用」的 5 支(跨檔穩健正報酬、回撤遠低於買入持有)。以趨勢/突破/動量為主。
    ["ts_momentum"]     = new TsMomentumStrategy(),     // 波動率管理絕對動量
    ["chandelier_trend"]= new ChandelierTrendStrategy(),// Donchian 突破 + ATR 吊燈移動停損
    ["ma_regime_trend"] = new MaRegimeTrendStrategy(),  // 均線斜率 regime
    ["dual_thrust"]     = new DualThrustStrategy(),     // 區間突破 + 趨勢過濾
    ["accel_momentum"]  = new AccelMomentumStrategy(),  // 動量加速度 + 趨勢過濾
    // [whitelist add: 2026-05-24 AnthonyLee] 第二批「原生多空」批次:經 LongShortBacktestEngine
    // 廣宇宙驗證為正期望的 5 支(3 動量趨勢 + 2 去相關 sleeve)。組合等權 maxDD 49% < B&H 72%。
    ["dual_mom_ls"]      = new DualMomentumLsStrategy(),    // 雙時框動量共振
    ["di_trend_ls"]      = new DiTrendLsStrategy(),         // ADX/DI 方向趨勢
    ["supertrend_ls"]    = new SuperTrendLsStrategy(),      // SuperTrend ATR 趨勢
    ["bb_revert_ls"]     = new BollingerRevertLsStrategy(), // 趨勢對齊 z-score 回歸(去相關)
    ["donchian_fade_ls"] = new DonchianFadeLsStrategy(),    // 震盪市通道 fade(去相關)
    // [whitelist add: 2026-05-24 AnthonyLee] 第三批(形態工具,多空):
    // fib_retrace_ls 驗證可用且對趨勢家族「負相關」(最佳對沖腿);harmonic_ls 經正規諧波法+多空
    // 驗證仍「無 OOS edge」(全期 -34%/Sharpe -0.09),保留供研究、★勿實盤部署★。
    ["fib_retrace_ls"]   = new FibRetraceLsStrategy(),
    ["harmonic_ls"]      = new HarmonicLsStrategy(),        // ⚠ 無 edge、勿實盤
    // [2026-05-24 Claude] 8 年深日線(2018-2026)OOS + 去相關篩出的 3 條,無狀態出場、可 bot 直接跑。
    // 一致性 ≥55%、1× 報酬正、趨勢↔均值回歸彼此 |r|≤0.42。★上真錢前必走 shadow 對帳、務必 ~1× 有效槓桿★。
    ["don_trend"]        = new DonTrendStrategy(),   // 海龜突破(趨勢腿)
    ["rsi2_rev"]         = new Rsi2RevStrategy(),    // RSI-2 超賣回歸(均值回歸腿)
    ["boll_rev"]         = new BollRevStrategy(),    // 布林下軌回歸(去相關均值回歸)

    // [2026-05-25 Claude] 3 條新策略,先在 paper 場(美股/Binance)驗證 edge 再考慮真錢:
    ["squeeze_breakout"] = new SqueezeBreakoutStrategy(),  // 布林擠壓 → 突破
    ["rsi_divergence"]   = new RsiDivergenceStrategy(),    // RSI 背離反轉
    ["volume_breakout"]  = new VolumeBreakoutStrategy(),   // 通道突破 + 量能確認
};

// LLM proxy 配置——llm/news 策略共用同一份 broker URL + model
var llmEnabled = config.GetValue("Worker:Strategy:Llm:Enabled", false);
var llmBrokerUrl = config.GetValue<string>("Worker:Strategy:Llm:BrokerUrl")
                   ?? config.GetValue<string>("Worker:Strategy:Llm:BaseUrl")
                   ?? "http://broker:5000";
var llmModel = config.GetValue("Worker:Strategy:Llm:Model", "gemini-2.0-flash")!;

// [whitelist add: 2026-05-24 AnthonyLee] 去相關精選 4 支的一鍵組合 = 淨加權曝險 ensemble。
// 用「淨加權曝險」(非投票):單一 symbol 只有一個淨部位,持有 4 支反波動率加權後的淨曝險,
// 在數學上恆等於該組合在此 symbol 的損益。配 AUTOTRADER_CONFIDENCE_SIZING_ENABLED=true →
// confidence∝|淨曝險| 會在成員分歧時自動縮量 → 復刻風險加權組合(maxDD 46% 版),可單一 watch 一鍵部署。
// 反波動率權重(2026-05-24 驗證):dual_mom 38% / dual_thrust 32% / bb_revert 19% / fib 10%。
strategies["decorr4_ls"] = new NetWeightedEnsembleStrategy(new List<(IStrategy, decimal)>
{
    (strategies["dual_mom_ls"],    0.38m),
    (strategies["dual_thrust"],    0.32m),
    (strategies["bb_revert_ls"],   0.19m),
    (strategies["fib_retrace_ls"], 0.10m),
}, name: "decorr4_ls");

// 專注震盪集成：只合驗證過有 edge 的 rsi_stoch/rsi_oversold/mfi/cci（盤整引擎）。要 inner 都在。
strategies["osc_ensemble"] = OscillatorEnsembleStrategy.DefaultFrom(strategies);

// [2026-05-27 C 路線] tsmom_btc_not_up:ts_momentum + BTC regime filter(只在 sideways/down 開倉)
// 實證:tsmom 在 BTC up 期 Sharpe 只 0.31(進場晚被 SL 殺)、過濾掉後 baseline 0.66 → 0.82 (+0.16)
// 需 broker scanner 注入 BTC bars 到 StrategySignalHandler;handler 會設 BtcBarsRef、wrapper 才能 evaluate regime
strategies["tsmom_btc_not_up"] = new BtcRegimeFilterStrategy(
    strategies["ts_momentum"],
    new[] { "sideways", "down" },
    emaFast: 20, emaSlow: 50,
    name: "tsmom_btc_not_up");

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
