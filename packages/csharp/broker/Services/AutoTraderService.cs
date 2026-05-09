using System.Collections.Concurrent;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 自動交易背景服務。
///
/// 流程：拉 K 線 → 策略分析 → 風控檢查 → 下單
/// 可透過 API 動態新增/移除/暫停監控的 symbol。
///
/// Watchlist 持久化到 SQLite（auto_trade_watchlist 表，2026-05-02 補完）。
/// 啟動時 load 所有 entry 重建記憶體 dict；任何變更（add/remove/pause/resume/qty 調整）
/// 同步寫回 DB——這樣 broker 重啟後監控清單不會消失。
/// </summary>
public class AutoTraderService : BackgroundService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly BrokerDb _db;
    private readonly ILogger<AutoTraderService> _logger;

    private readonly ConcurrentDictionary<string, WatchItem> _watchList = new();
    private readonly ConcurrentQueue<TradeLog> _tradeLog = new();
    private const int MaxLogEntries = 200;

    private int _intervalSeconds = 300; // 預設 5 分鐘
    private bool _enabled = false;

    /// <summary>上次 cycle 開始時間（UTC）。null = 從未跑過 / 沒 enabled。
    /// 給 /metrics 跟 heartbeat watchdog 看「auto-trader 是不是真的有在跑」用。</summary>
    private DateTime? _lastCycleAt;
    public DateTime? LastCycleAt => _lastCycleAt;

    /// <summary>
    /// 開發/測試用：env AUTOTRADER_DEV_FORCE_ACTION=buy|sell 會強制覆蓋 strategy 訊號
    /// （繞過 action=="hold" early-return 跟 confidence threshold），讓 e2e 真的打到
    /// 交易所。**只該在 paper 帳號用、用完一定要 unset**——每一輪會印 WARNING log 提醒。
    /// 預設 null = 不啟用、走原本訊號驅動邏輯。
    /// </summary>
    private readonly string? _devForceAction;

    /// <summary>
    /// 信心度門檻——env AUTOTRADER_MIN_CONFIDENCE 可覆蓋（合法範圍 [0, 1]）。
    /// 預設 0.5。Composite 策略 + 三道過濾鏈（hold 稀釋 + auto-trader 門檻 + risk
    /// dampening）很容易把 buy/sell 訊號擋下來，這個 knob 讓 paper 階段可以放寬到 0.45-0.5
    /// 觀察出單頻率，正式上線再拉回 0.6+。
    /// </summary>
    private readonly decimal _minConfidence;

    /// <summary>
    /// Portfolio 當日最大 drawdown 上限（%）。env AUTOTRADER_MAX_PORTFOLIO_DD_PCT 可覆蓋。
    /// 預設 8。每個 exchange 各自追蹤當日 peak，當 (peak - current) / peak ≥ 此 % 時，
    /// 該 exchange 的所有新單會被該 cycle 擋下（既有持倉不動）；peak 在 UTC 午夜重置。
    /// 跟 risk-worker 的 max_drawdown_pct 規則互補：那條看「歷史總高」、這條看「當日最高」。
    /// </summary>
    private readonly decimal _maxPortfolioDdPct;
    private readonly ConcurrentDictionary<string, PortfolioPeakState> _peakByExchange = new();

    public string? DevForceAction => _devForceAction;
    public decimal MinConfidence => _minConfidence;
    public decimal MaxPortfolioDdPct => _maxPortfolioDdPct;
    public IReadOnlyDictionary<string, object> CircuitBreakerSnapshot =>
        _peakByExchange.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                peak_value     = kv.Value.PeakValue,
                last_value     = kv.Value.LastValue,
                dd_pct         = kv.Value.LastDdPct,
                triggered      = kv.Value.LastTriggered,
                threshold_pct  = _maxPortfolioDdPct,
                peak_reset_at  = kv.Value.PeakResetAt,
                last_update    = kv.Value.LastUpdate,
            });

    /// <summary>每 exchange 一份的當日 peak / 最近一次評估快照。</summary>
    internal class PortfolioPeakState
    {
        public decimal PeakValue;
        public decimal LastValue;
        public decimal LastDdPct;
        public bool LastTriggered;
        public DateTime PeakResetAt;  // UTC 當日 00:00
        public DateTime LastUpdate;
    }

    /// <summary>單次 circuit breaker 評估結果。</summary>
    public class CircuitBreakerEval
    {
        public bool Triggered    { get; init; }
        public decimal PeakValue { get; init; }
        public decimal CurrentValue { get; init; }
        public decimal DdPct     { get; init; }
        public decimal Threshold { get; init; }
        public DateTime PeakResetAt { get; init; }
    }

    // ── B4a Position protection ─────────────────────────────────────
    //
    // 每個 open position 一份 state，每 cycle 跑 sweep：
    //   1. SL hit  → 賣全部 → 清 state（下次 sweep 由 qty=0 觸發 cleanup）
    //   2. 漲 ≥ partial_exit_pct → 賣 partial_exit_ratio 的量、partial_exited=true（只觸發 1 次）
    //   3. 漲 ≥ breakeven_trigger_pct → SL 移到 entry × (1 + buffer) (be_moved=true)
    //
    // SL 是 broker 端「軟 SL」——不打交易所的 stop-loss order，每 cycle 拉 current_price
    // 比對 sl_price 自己決定要不要平倉。好處：可動態調整、不用管 exchange 的 SL 規格差異。
    // 風險：cycle 間隔 5 分鐘期間若有 flash crash，會錯過。配 B1 circuit breaker 為兜底。
    public class ProtectionConfig
    {
        public decimal InitialSlPct        { get; init; }  // 進場時 SL 距離 entry 的 %
        public decimal PartialExitPct      { get; init; }  // 漲 ≥ 此 % 觸發部分平倉
        public decimal PartialExitRatio    { get; init; }  // 部分平倉的數量比例 [0, 1]
        public decimal BreakevenTriggerPct { get; init; }  // 漲 ≥ 此 % SL 移到 BE
        public decimal BreakevenBufferPct  { get; init; }  // BE 上方留多少 buffer（避免價差掃損）
    }

    public class PositionProtectionState
    {
        public string Exchange      { get; set; } = "";
        public string Symbol        { get; set; } = "";
        public decimal EntryPrice   { get; set; }
        public decimal PeakPrice    { get; set; }
        public decimal SlPrice      { get; set; }
        public bool PartialExited   { get; set; }
        public bool BeMoved         { get; set; }
        public DateTime CreatedAt   { get; set; }
        public DateTime UpdatedAt   { get; set; }
    }

    private readonly ProtectionConfig _protectionConfig;
    private readonly ConcurrentDictionary<string, PositionProtectionState> _positionState = new();

    public ProtectionConfig PositionProtectionConfig => _protectionConfig;
    public IReadOnlyDictionary<string, PositionProtectionState> PositionStates => _positionState;

    // ── Phase 4: Perpetual position protection（雙向 + 強平距離）──
    //
    // 跟 spot 的 PositionProtectionState 故意分開：
    //   - Side: long / short — SL math 完全反向（long: mark ≤ sl 觸發 / short: mark ≥ sl 觸發）
    //   - PnL%: long = (mark-entry)/entry, short = (entry-mark)/entry
    //   - PeakMark: long 看最高 mark、short 看最低 mark（保護「漲過再跌」/「跌過再漲」）
    //   - LiquidationPrice: 強平價（從交易所即時拉），距離過近觸發 emergency close
    public class PerpetualPositionState
    {
        public string Exchange      { get; set; } = "";
        public string Symbol        { get; set; } = "";
        public string Side          { get; set; } = "long";  // "long" | "short"
        public decimal EntryPrice   { get; set; }
        public decimal PeakMark     { get; set; }            // long: max mark seen; short: min mark seen
        public decimal SlPrice      { get; set; }            // long: 在 entry 下方; short: 在 entry 上方
        public decimal LiquidationPrice { get; set; }
        public int Leverage         { get; set; } = 1;
        public bool PartialExited   { get; set; }
        public bool BeMoved         { get; set; }
        public DateTime CreatedAt   { get; set; }
        public DateTime UpdatedAt   { get; set; }
    }

    private readonly ConcurrentDictionary<string, PerpetualPositionState> _perpPositionState = new();
    private readonly decimal _perpLiqEmergencyPct;
    public IReadOnlyDictionary<string, PerpetualPositionState> PerpetualPositionStates => _perpPositionState;
    public decimal PerpLiquidationEmergencyPct => _perpLiqEmergencyPct;

    public enum PerpProtectionAction { None, SlHit, PartialExit, BeMove, LiquidationEmergency }

    public class PerpProtectionDecision
    {
        public PerpProtectionAction Action { get; init; }
        public decimal PartialQty       { get; init; }
        public decimal NewSlPrice       { get; init; }
        public decimal PnlPct           { get; init; }
        public decimal LiqDistancePct   { get; init; }
        public string Reason            { get; init; } = "";
    }

    /// <summary>
    /// Pure decision——給 perpetual position state + mark + qty + config 回傳該做什麼。
    /// 同樣不下單、不改 state、不依賴 dispatcher，方便單元測試。
    ///
    /// 優先順序：LiquidationEmergency > SlHit > PartialExit > BeMove > None
    /// 強平距離保護排第一：即使 SL 還沒到、若離強平太近也要先平
    ///
    /// 雙向 SL math：
    ///   long  SlHit:  mark ≤ sl_price
    ///   short SlHit:  mark ≥ sl_price
    /// 雙向 PnL%:
    ///   long  pnlPct = (mark - entry) / entry × 100
    ///   short pnlPct = (entry - mark) / entry × 100
    /// 雙向 BE:
    ///   long  newSl = entry × (1 + buffer/100)
    ///   short newSl = entry × (1 - buffer/100)
    /// </summary>
    public static PerpProtectionDecision EvaluatePerpetualProtection(
        PerpetualPositionState state, decimal markPrice, decimal qty,
        decimal liqDistancePct, ProtectionConfig config, decimal liqEmergencyPct)
    {
        if (state.EntryPrice <= 0m || qty <= 0m || markPrice <= 0m)
            return new PerpProtectionDecision { Action = PerpProtectionAction.None, Reason = "invalid inputs" };

        var isLong = state.Side == "long";
        var pnlPct = isLong
            ? (markPrice - state.EntryPrice) / state.EntryPrice * 100m
            : (state.EntryPrice - markPrice) / state.EntryPrice * 100m;

        // 1) Liquidation emergency — 不論方向、距離過近就先平
        if (state.LiquidationPrice > 0m && liqDistancePct > 0m && liqDistancePct <= liqEmergencyPct)
        {
            return new PerpProtectionDecision
            {
                Action = PerpProtectionAction.LiquidationEmergency,
                PartialQty = qty, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                Reason = $"⚠ liquidation emergency: distance {liqDistancePct:F2}% ≤ {liqEmergencyPct}% (mark {markPrice:F4}, liq {state.LiquidationPrice:F4})",
            };
        }

        // 2) SL hit (含 BE 後挪過的 SL)
        var slHit = isLong ? markPrice <= state.SlPrice : markPrice >= state.SlPrice;
        if (slHit)
        {
            return new PerpProtectionDecision
            {
                Action = PerpProtectionAction.SlHit,
                PartialQty = qty, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                Reason = $"SL hit ({state.Side}) @ {markPrice:F4} {(isLong ? "≤" : "≥")} {state.SlPrice:F4} (entry {state.EntryPrice:F4}, P&L {pnlPct:+0.00;-0.00}%)",
            };
        }

        // 3) Partial exit
        if (!state.PartialExited && pnlPct >= config.PartialExitPct)
        {
            var partialQty = Math.Round(qty * config.PartialExitRatio, 4, MidpointRounding.ToZero);
            if (partialQty > 0m && partialQty < qty)
            {
                return new PerpProtectionDecision
                {
                    Action = PerpProtectionAction.PartialExit,
                    PartialQty = partialQty, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                    Reason = $"Partial exit ({state.Side}) @ +{pnlPct:F2}% — selling {config.PartialExitRatio:P0} ({partialQty})",
                };
            }
        }

        // 4) BE SL move
        if (!state.BeMoved && pnlPct >= config.BreakevenTriggerPct)
        {
            var newSl = isLong
                ? state.EntryPrice * (1m + config.BreakevenBufferPct / 100m)
                : state.EntryPrice * (1m - config.BreakevenBufferPct / 100m);
            // 只往「縮小風險」方向挪：long 往上挪、short 往下挪
            var moves = isLong ? newSl > state.SlPrice : newSl < state.SlPrice;
            if (moves)
            {
                return new PerpProtectionDecision
                {
                    Action = PerpProtectionAction.BeMove,
                    NewSlPrice = newSl, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                    Reason = $"SL → BE ({state.Side}) {(isLong ? "+" : "−")}{config.BreakevenBufferPct}% (was {state.SlPrice:F4}, now {newSl:F4}) at +{pnlPct:F2}%",
                };
            }
        }

        return new PerpProtectionDecision { Action = PerpProtectionAction.None, PnlPct = pnlPct, LiqDistancePct = liqDistancePct };
    }

    // ── B3 SL flush freeze ─────────────────────────────────────────
    //
    // 連環 SL 觸發 = 訊號斷崖式失敗（策略當下抓不住行情、或極端 regime）。
    // 滑動視窗看最近 N 分鐘的 SL hit 次數，超過閾值就把 _enabled 翻 false、
    // 強制讓使用者手動 reset。避免「演算法亂跑、user 還沒注意到、損失越滾越大」。
    public class SlHitRecord
    {
        public string Exchange { get; init; } = "";
        public string Symbol   { get; init; } = "";
        public DateTime At     { get; init; }
    }

    private readonly int _slFlushThreshold;
    private readonly int _slFlushWindowMinutes;
    private readonly ConcurrentQueue<SlHitRecord> _recentSlHits = new();
    private DateTime? _slFlushTriggeredAt;

    public int SlFlushThreshold      => _slFlushThreshold;
    public int SlFlushWindowMinutes  => _slFlushWindowMinutes;
    public bool SlFlushTriggered     => _slFlushTriggeredAt.HasValue;
    public DateTime? SlFlushTriggeredAt => _slFlushTriggeredAt;
    public IReadOnlyList<SlHitRecord> RecentSlHits => _recentSlHits.ToArray();

    public enum ProtectionAction { None, SlHit, PartialExit, BeMove }

    public class ProtectionDecision
    {
        public ProtectionAction Action { get; init; }
        public decimal PartialQty      { get; init; }
        public decimal NewSlPrice      { get; init; }
        public decimal PnlPct          { get; init; }
        public string Reason           { get; init; } = "";
    }

    /// <summary>
    /// Pure decision——給 state + 當前 price/qty + config 回傳該做什麼動作。
    /// 不下單、不改 state、不依賴 dispatcher，方便單元測試。
    /// 呼叫端拿到 decision 後再決定怎麼執行（下單 / 改 state）。
    ///
    /// 優先順序：SL hit > Partial exit > BE move > None
    /// 一次只回一個 action（同 cycle 不會既 partial 又 BE）；下次 sweep 自然會接著走。
    /// </summary>
    public static ProtectionDecision EvaluateProtection(
        PositionProtectionState state, decimal currentPrice, decimal qty, ProtectionConfig config)
    {
        if (state.EntryPrice <= 0m || qty <= 0m || currentPrice <= 0m)
            return new ProtectionDecision { Action = ProtectionAction.None, Reason = "invalid inputs" };

        var pnlPct = (currentPrice - state.EntryPrice) / state.EntryPrice * 100m;

        // 1) SL hit (含 BE 後挪過的 SL)
        if (currentPrice <= state.SlPrice)
        {
            return new ProtectionDecision
            {
                Action = ProtectionAction.SlHit,
                PartialQty = qty,
                PnlPct = pnlPct,
                Reason = $"SL hit @ {currentPrice:F4} ≤ {state.SlPrice:F4} (entry {state.EntryPrice:F4}, peak {state.PeakPrice:F4}, P&L {pnlPct:+0.00;-0.00}%)",
            };
        }

        // 2) Partial exit
        if (!state.PartialExited && pnlPct >= config.PartialExitPct)
        {
            var partialQty = Math.Round(qty * config.PartialExitRatio, 4, MidpointRounding.ToZero);
            if (partialQty > 0m && partialQty < qty)
            {
                return new ProtectionDecision
                {
                    Action = ProtectionAction.PartialExit,
                    PartialQty = partialQty,
                    PnlPct = pnlPct,
                    Reason = $"Partial exit @ +{pnlPct:F2}% — selling {config.PartialExitRatio:P0} ({partialQty})",
                };
            }
        }

        // 3) BE SL move
        if (!state.BeMoved && pnlPct >= config.BreakevenTriggerPct)
        {
            var newSl = state.EntryPrice * (1m + config.BreakevenBufferPct / 100m);
            if (newSl > state.SlPrice)
            {
                return new ProtectionDecision
                {
                    Action = ProtectionAction.BeMove,
                    NewSlPrice = newSl,
                    PnlPct = pnlPct,
                    Reason = $"SL → BE +{config.BreakevenBufferPct}% (was {state.SlPrice:F4}, now {newSl:F4}) at +{pnlPct:F2}%",
                };
            }
        }

        return new ProtectionDecision { Action = ProtectionAction.None, PnlPct = pnlPct };
    }

    public AutoTraderService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        BrokerDb db,
        ILogger<AutoTraderService> logger)
    {
        _dispatcher = dispatcher;
        _registry   = registry;
        _db         = db;
        _logger     = logger;

        var forceRaw = Environment.GetEnvironmentVariable("AUTOTRADER_DEV_FORCE_ACTION")?.Trim().ToLowerInvariant();
        if (forceRaw == "buy" || forceRaw == "sell")
        {
            _devForceAction = forceRaw;
            _logger.LogWarning(
                "⚠ AUTOTRADER_DEV_FORCE_ACTION={Action} active — signals will be overridden, threshold bypassed. UNSET FOR PRODUCTION.",
                forceRaw);
        }

        _minConfidence = ParseMinConfidence(Environment.GetEnvironmentVariable("AUTOTRADER_MIN_CONFIDENCE"));
        _maxPortfolioDdPct = ParseMaxPortfolioDdPct(Environment.GetEnvironmentVariable("AUTOTRADER_MAX_PORTFOLIO_DD_PCT"));
        _protectionConfig = ParseProtectionConfig();
        _slFlushThreshold = ParseIntEnv("AUTOTRADER_SL_FLUSH_THRESHOLD", defaultValue: 3, min: 1, max: 100);
        _slFlushWindowMinutes = ParseIntEnv("AUTOTRADER_SL_FLUSH_WINDOW_MINUTES", defaultValue: 60, min: 1, max: 1440);
        // Perp 強平距離保護：低於此 % 觸發 emergency close（不論 SL 是否到）。預設 5%
        _perpLiqEmergencyPct = ParsePctEnv("AUTOTRADER_PERP_LIQ_EMERGENCY_PCT", defaultValue: 5m, min: 0.5m, max: 50m);
        _logger.LogInformation(
            "AutoTrader thresholds: confidence={Conf:P0} portfolio_dd={Dd}% sl_flush={Flush}/{Window}min · " +
            "protection: initial_sl={IniSl}% partial_exit={Pe}% (sell {Per:P0}) BE_trigger={Bet}% (buffer {Beb}%) · " +
            "perp_liq_emergency={LiqEm}%",
            _minConfidence, _maxPortfolioDdPct, _slFlushThreshold, _slFlushWindowMinutes,
            _protectionConfig.InitialSlPct, _protectionConfig.PartialExitPct, _protectionConfig.PartialExitRatio,
            _protectionConfig.BreakevenTriggerPct, _protectionConfig.BreakevenBufferPct,
            _perpLiqEmergencyPct);

        LoadWatchListFromDb();
        LoadSettingsFromDb();
        LoadPerpStatesFromDb();
    }

    internal static int ParseIntEnv(string envName, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!int.TryParse(raw.Trim(), out var v)) return defaultValue;
        if (v < min) return defaultValue;
        if (v > max) return max;
        return v;
    }

    /// <summary>
    /// 記錄一次 SL hit，並判斷是否觸發 flush（連環 SL → 凍結 auto-trader）。
    /// 滑動視窗外的舊 hit 會在這裡 prune；達到閾值就把 _enabled 翻 false、
    /// 寫 _slFlushTriggeredAt 給 dashboard 看（要 user 手動按 ResetSlFlush 復原）。
    /// </summary>
    internal void RecordSlHit(string exchange, string symbol, DateTime now)
    {
        _recentSlHits.Enqueue(new SlHitRecord { Exchange = exchange, Symbol = symbol, At = now });

        // 在 window 外的舊 hit 移除
        var cutoff = now.AddMinutes(-_slFlushWindowMinutes);
        while (_recentSlHits.TryPeek(out var oldest) && oldest.At < cutoff)
            _recentSlHits.TryDequeue(out _);

        // 達到閾值 → 凍結
        if (_slFlushTriggeredAt == null && _recentSlHits.Count >= _slFlushThreshold)
        {
            _slFlushTriggeredAt = now;
            _enabled = false;
            PersistSettings();   // SL flush 觸發的 disabled 也要持久化、避免重啟後又自己 enable 出去
            _logger.LogError(
                "⚠ SL flush triggered: {Count} SLs hit within {Window}min — auto-trader DISABLED. Manual reset required via /api/v1/auto-trader/sl-flush/reset.",
                _recentSlHits.Count, _slFlushWindowMinutes);
        }
    }

    /// <summary>手動清除 SL flush 狀態（呼叫 /api/v1/auto-trader/sl-flush/reset 後）。</summary>
    public void ResetSlFlush()
    {
        _slFlushTriggeredAt = null;
        while (_recentSlHits.TryDequeue(out _)) { }
        _logger.LogInformation("SL flush state reset (queue cleared, trigger flag cleared)");
    }

    private static ProtectionConfig ParseProtectionConfig() => new()
    {
        InitialSlPct        = ParsePctEnv("AUTOTRADER_INITIAL_SL_PCT",        defaultValue: 5m,    min: 0.5m, max: 50m),
        PartialExitPct      = ParsePctEnv("AUTOTRADER_PARTIAL_EXIT_PCT",      defaultValue: 5m,    min: 0.5m, max: 100m),
        PartialExitRatio    = ParseRatioEnv("AUTOTRADER_PARTIAL_EXIT_RATIO",  defaultValue: 0.5m),
        BreakevenTriggerPct = ParsePctEnv("AUTOTRADER_BREAKEVEN_TRIGGER_PCT", defaultValue: 3m,    min: 0.5m, max: 100m),
        BreakevenBufferPct  = ParsePctEnv("AUTOTRADER_BREAKEVEN_BUFFER_PCT",  defaultValue: 0.5m,  min: 0m,   max: 10m),
    };

    internal static decimal ParsePctEnv(string envName, decimal defaultValue, decimal min, decimal max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return defaultValue;
        if (v < min) return defaultValue;
        if (v > max) return max;
        return v;
    }

    internal static decimal ParseRatioEnv(string envName, decimal defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return defaultValue;
        if (v <= 0m || v >= 1m) return defaultValue;  // (0, 1) 之外無意義
        return v;
    }

    internal static decimal ParseMinConfidence(string? raw)
    {
        const decimal defaultValue = 0.5m;
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return defaultValue;
        if (v < 0m) return 0m;
        if (v > 1m) return 1m;
        return v;
    }

    internal static decimal ParseMaxPortfolioDdPct(string? raw)
    {
        const decimal defaultValue = 8m;
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return defaultValue;
        if (v <= 0m) return defaultValue;  // 0 / 負數視同沒設、走預設
        if (v > 100m) return 100m;
        return v;
    }

    /// <summary>
    /// 每 cycle 對 (exchange, currentPortfolioValue) 評估 circuit breaker。
    /// 內部會更新該 exchange 的 peak（漲就抬高、跨 UTC 午夜重置）並計算 DD%。
    /// 觸發條件：DD% ≥ _maxPortfolioDdPct → Triggered=true，呼叫端要 skip 該 cycle 的下單。
    /// </summary>
    public CircuitBreakerEval EvaluateCircuitBreaker(string exchange, decimal currentValue, DateTime nowUtc)
    {
        if (currentValue <= 0m)
        {
            // 沒有有效 portfolio value（worker 連不上、或帳戶為空）→ 視為 0 DD、不擋
            return new CircuitBreakerEval { Triggered = false, PeakValue = 0m, CurrentValue = 0m, DdPct = 0m, Threshold = _maxPortfolioDdPct };
        }

        var todayUtc = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, DateTimeKind.Utc);
        var state = _peakByExchange.AddOrUpdate(
            exchange,
            _ => new PortfolioPeakState
            {
                PeakValue = currentValue, PeakResetAt = todayUtc,
                LastValue = currentValue, LastDdPct = 0m, LastTriggered = false, LastUpdate = nowUtc,
            },
            (_, s) =>
            {
                // UTC 午夜重置 peak
                if (s.PeakResetAt < todayUtc)
                {
                    s.PeakValue = currentValue;
                    s.PeakResetAt = todayUtc;
                }
                else if (currentValue > s.PeakValue)
                {
                    s.PeakValue = currentValue;
                }
                s.LastValue = currentValue;
                s.LastUpdate = nowUtc;
                return s;
            });

        var ddPct = state.PeakValue > 0m ? (state.PeakValue - currentValue) / state.PeakValue * 100m : 0m;
        var triggered = ddPct >= _maxPortfolioDdPct;
        state.LastDdPct = Math.Round(ddPct, 2);
        state.LastTriggered = triggered;

        return new CircuitBreakerEval
        {
            Triggered    = triggered,
            PeakValue    = state.PeakValue,
            CurrentValue = currentValue,
            DdPct        = Math.Round(ddPct, 2),
            Threshold    = _maxPortfolioDdPct,
            PeakResetAt  = state.PeakResetAt,
        };
    }

    // ── 持久化 ──────────────────────────────────────────────────────

    private void LoadWatchListFromDb()
    {
        try
        {
            var entries = _db.GetAll<AutoTradeWatchEntry>();
            foreach (var e in entries)
            {
                _watchList[e.EntryKey] = new WatchItem
                {
                    Symbol = e.Symbol, Exchange = e.Exchange, Strategy = e.Strategy,
                    Quantity = e.Quantity, Active = e.Active,
                    LastSignal = e.LastSignal, LastConfidence = e.LastConfidence, LastCheck = e.LastCheck,
                    Mode = string.IsNullOrEmpty(e.Mode) ? "spot" : e.Mode,
                    Leverage = e.Leverage > 0 ? e.Leverage : 5,
                };
            }
            if (entries.Count > 0)
                _logger.LogInformation("AutoTrader: restored {Count} watch entries from DB", entries.Count);
        }
        catch (Exception ex)
        {
            // 不要因為 DB load 失敗就讓服務起不來——既有記憶體 dict 為空，至少 broker 起得來
            _logger.LogError(ex, "AutoTrader: failed to load watchlist from DB; starting with empty list");
        }
    }

    private void PersistWatch(string key, WatchItem item)
    {
        try
        {
            var existing = _db.Get<AutoTradeWatchEntry>(key);
            var now = DateTime.UtcNow;
            if (existing == null)
            {
                _db.Insert(new AutoTradeWatchEntry
                {
                    EntryKey = key, Symbol = item.Symbol, Exchange = item.Exchange,
                    Strategy = item.Strategy, Quantity = item.Quantity, Active = item.Active,
                    LastSignal = item.LastSignal, LastConfidence = item.LastConfidence, LastCheck = item.LastCheck,
                    Mode = item.Mode, Leverage = item.Leverage,
                    CreatedAt = now, UpdatedAt = now,
                });
            }
            else
            {
                existing.Symbol = item.Symbol; existing.Exchange = item.Exchange;
                existing.Strategy = item.Strategy; existing.Quantity = item.Quantity; existing.Active = item.Active;
                existing.LastSignal = item.LastSignal; existing.LastConfidence = item.LastConfidence; existing.LastCheck = item.LastCheck;
                existing.Mode = item.Mode; existing.Leverage = item.Leverage;
                existing.UpdatedAt = now;
                _db.Update(existing);
            }
        }
        catch (Exception ex)
        {
            // 持久化失敗不要中斷主流程——記憶體 dict 已更新、log 出來等下次 add/remove 重試
            _logger.LogWarning(ex, "AutoTrader: failed to persist watch entry {Key}", key);
        }
    }

    private void DeletePersistedWatch(string key)
    {
        try { _db.Delete<AutoTradeWatchEntry>(key); }
        catch (Exception ex) { _logger.LogWarning(ex, "AutoTrader: failed to delete persisted watch {Key}", key); }
    }

    // ── 全域設定持久化（enabled / interval_seconds）──
    private const string SettingsKey = "main";

    private void LoadSettingsFromDb()
    {
        try
        {
            var entry = _db.Get<AutoTraderSettingsEntry>(SettingsKey);
            if (entry != null)
            {
                _enabled = entry.Enabled;
                _intervalSeconds = Math.Max(60, entry.IntervalSeconds);
                _logger.LogInformation(
                    "AutoTrader: restored settings from DB (enabled={Enabled}, interval={Interval}s)",
                    _enabled, _intervalSeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AutoTrader: failed to load settings; using defaults");
        }
    }

    private void PersistSettings()
    {
        try
        {
            var existing = _db.Get<AutoTraderSettingsEntry>(SettingsKey);
            var now = DateTime.UtcNow;
            if (existing == null)
            {
                _db.Insert(new AutoTraderSettingsEntry
                {
                    SingletonKey = SettingsKey, Enabled = _enabled,
                    IntervalSeconds = _intervalSeconds, UpdatedAt = now,
                });
            }
            else
            {
                existing.Enabled = _enabled;
                existing.IntervalSeconds = _intervalSeconds;
                existing.UpdatedAt = now;
                _db.Update(existing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AutoTrader: failed to persist settings");
        }
    }

    // ── Perp 部位保護狀態持久化 ──
    private void LoadPerpStatesFromDb()
    {
        try
        {
            var entries = _db.GetAll<PerpetualPositionStateEntry>();
            foreach (var e in entries)
            {
                _perpPositionState[e.EntryKey] = new PerpetualPositionState
                {
                    Exchange = e.Exchange, Symbol = e.Symbol, Side = e.Side,
                    EntryPrice = e.EntryPrice, PeakMark = e.PeakMark, SlPrice = e.SlPrice,
                    LiquidationPrice = e.LiquidationPrice, Leverage = e.Leverage,
                    PartialExited = e.PartialExited, BeMoved = e.BeMoved,
                    CreatedAt = e.CreatedAt, UpdatedAt = e.UpdatedAt,
                };
            }
            if (entries.Count > 0)
                _logger.LogInformation("AutoTrader: restored {Count} perp position states from DB", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AutoTrader: failed to load perp position states");
        }
    }

    private void PersistPerpState(string key, PerpetualPositionState state)
    {
        try
        {
            var existing = _db.Get<PerpetualPositionStateEntry>(key);
            if (existing == null)
            {
                _db.Insert(new PerpetualPositionStateEntry
                {
                    EntryKey = key, Exchange = state.Exchange, Symbol = state.Symbol, Side = state.Side,
                    EntryPrice = state.EntryPrice, PeakMark = state.PeakMark, SlPrice = state.SlPrice,
                    LiquidationPrice = state.LiquidationPrice, Leverage = state.Leverage,
                    PartialExited = state.PartialExited, BeMoved = state.BeMoved,
                    CreatedAt = state.CreatedAt, UpdatedAt = state.UpdatedAt,
                });
            }
            else
            {
                existing.EntryPrice = state.EntryPrice; existing.PeakMark = state.PeakMark;
                existing.SlPrice = state.SlPrice; existing.LiquidationPrice = state.LiquidationPrice;
                existing.Leverage = state.Leverage;
                existing.PartialExited = state.PartialExited; existing.BeMoved = state.BeMoved;
                existing.UpdatedAt = state.UpdatedAt;
                _db.Update(existing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AutoTrader: failed to persist perp state {Key}", key);
        }
    }

    private void DeletePersistedPerpState(string key)
    {
        try { _db.Delete<PerpetualPositionStateEntry>(key); }
        catch (Exception ex) { _logger.LogWarning(ex, "AutoTrader: failed to delete perp state {Key}", key); }
    }

    // ── 外部控制 API ────────────────────────────────────────────────

    public bool IsEnabled => _enabled;
    public int IntervalSeconds => _intervalSeconds;
    public IReadOnlyDictionary<string, WatchItem> WatchList => _watchList;
    public IEnumerable<TradeLog> RecentLogs => _tradeLog.ToArray().Take(MaxLogEntries);

    public void Enable() { _enabled = true; PersistSettings(); _logger.LogInformation("AutoTrader ENABLED"); }
    public void Disable() { _enabled = false; PersistSettings(); _logger.LogInformation("AutoTrader DISABLED"); }
    public void SetInterval(int seconds) { _intervalSeconds = Math.Max(60, seconds); PersistSettings(); }

    public void AddWatch(string symbol, string exchange, string strategy = "composite", decimal quantity = 1,
        string mode = "spot", int leverage = 5)
    {
        var key = $"{exchange}:{symbol}";
        // 驗 mode：unknown 一律退回 spot 避免奇怪行為
        var validModes = new[] { "spot", "perp_long_only", "perp_both" };
        if (!validModes.Contains(mode)) mode = "spot";
        // leverage clamp：1-125（BingX 上限）；非 perp 模式忽略此值但仍存
        leverage = Math.Max(1, Math.Min(125, leverage));
        var item = new WatchItem
        {
            Symbol = symbol, Exchange = exchange, Strategy = strategy,
            Quantity = quantity, Active = true, Mode = mode, Leverage = leverage,
        };
        _watchList[key] = item;
        PersistWatch(key, item);
        _logger.LogInformation("AutoTrader: watching {Key} strategy={Strategy} qty={Qty} mode={Mode} lev={Lev}x",
            key, strategy, quantity, mode, leverage);
    }

    public bool RemoveWatch(string symbol, string exchange)
    {
        var key = $"{exchange}:{symbol}";
        var removed = _watchList.TryRemove(key, out _);
        if (removed) DeletePersistedWatch(key);
        return removed;
    }

    public void PauseWatch(string symbol, string exchange)
    {
        var key = $"{exchange}:{symbol}";
        if (_watchList.TryGetValue(key, out var item))
        {
            item.Active = false;
            PersistWatch(key, item);
        }
    }

    public void ResumeWatch(string symbol, string exchange)
    {
        var key = $"{exchange}:{symbol}";
        if (_watchList.TryGetValue(key, out var item))
        {
            item.Active = true;
            PersistWatch(key, item);
        }
    }

    // ── 主迴圈 ──────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AutoTrader service started (disabled by default, use API to enable)");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), ct);
            }
            catch (OperationCanceledException) { break; }

            if (!_enabled || _watchList.IsEmpty) continue;

            // 標記本次 cycle 開始時間——觀察性指標 / heartbeat watchdog 用，
            // 看 auto-trader 是否真的在跑（沒在跑時 dashboard / Discord 可警告）
            _lastCycleAt = DateTime.UtcNow;

            // Step 0a: Spot position protection sweep——既有的長倉保護
            try { await SweepPositionProtectionAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "AutoTrader spot protection sweep failed"); }

            // Step 0b: Perpetual position protection sweep（Phase 4）——
            // 雙向部位 SL math + 強平距離保護。BingX 等 perp exchange 才跑。
            try { await SweepPerpetualProtectionAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "AutoTrader perp protection sweep failed"); }

            foreach (var (key, item) in _watchList)
            {
                if (!item.Active || ct.IsCancellationRequested) continue;

                try
                {
                    await ProcessSymbolAsync(item, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AutoTrader error processing {Key}", key);
                    AddLog(item, "error", $"Exception: {ex.Message}");
                }
            }
        }
    }

    // ── B4a Position protection sweep ──────────────────────────────
    //
    // 對所有 watch 出現過的 exchange，拉 get_positions、對每個有 qty 的部位：
    //   1. 沒 state 的（新部位）→ 建 state、entry/peak/sl 從 avg_entry_price 算
    //   2. SL hit → 賣全部
    //   3. PnL ≥ partial_exit_pct 且未 partial_exited → 賣 ratio 比例
    //   4. PnL ≥ breakeven_trigger_pct 且未 be_moved → SL 上挪到 entry × (1 + buffer%)
    // 完成後清除 state 裡 qty=0 的舊部位。
    private async Task SweepPositionProtectionAsync(CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.account") || !_registry.HasAvailableWorker("trading.order"))
            return;

        var exchanges = _watchList.Values.Select(w => w.Exchange).Distinct().ToList();
        foreach (var exchange in exchanges)
        {
            if (ct.IsCancellationRequested) return;
            var posResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_positions",
                JsonSerializer.Serialize(new { exchange })));
            if (!posResult.Success) continue;

            var pos = JsonDocument.Parse(posResult.ResultPayload ?? "{}").RootElement;
            if (!pos.TryGetProperty("positions", out var posArr) || posArr.ValueKind != JsonValueKind.Array)
                continue;

            var liveKeys = new HashSet<string>();
            foreach (var p in posArr.EnumerateArray())
            {
                var symbol = p.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                var qty = p.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0m;
                var entryPrice = p.TryGetProperty("avg_entry_price", out var e) ? e.GetDecimal() : 0m;
                var currentPrice = p.TryGetProperty("current_price", out var cp) ? cp.GetDecimal() : 0m;
                if (string.IsNullOrEmpty(symbol) || qty <= 0m || entryPrice <= 0m || currentPrice <= 0m) continue;

                liveKeys.Add($"{exchange}:{symbol}");
                try
                {
                    await ProcessPositionProtectionAsync(exchange, symbol, qty, entryPrice, currentPrice, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Protection check failed for {Exchange}:{Symbol}", exchange, symbol);
                }
            }

            // 清除 state 裡 exchange 下、live 沒出現的（已被全平的）
            var staleKeys = _positionState
                .Where(kv => kv.Key.StartsWith(exchange + ":") && !liveKeys.Contains(kv.Key))
                .Select(kv => kv.Key).ToList();
            foreach (var k in staleKeys)
            {
                if (_positionState.TryRemove(k, out _))
                    _logger.LogInformation("Position {Key} closed — protection state cleaned", k);
            }
        }
    }

    private async Task ProcessPositionProtectionAsync(
        string exchange, string symbol, decimal qty, decimal entryPrice, decimal currentPrice, CancellationToken ct)
    {
        var key = $"{exchange}:{symbol}";
        var now = DateTime.UtcNow;
        var state = _positionState.GetOrAdd(key, _ =>
        {
            var initialSl = entryPrice * (1m - _protectionConfig.InitialSlPct / 100m);
            _logger.LogInformation(
                "Protection state init for {Key}: entry={Entry:F4}, SL={Sl:F4} ({Pct}% below entry)",
                key, entryPrice, initialSl, _protectionConfig.InitialSlPct);
            return new PositionProtectionState
            {
                Exchange = exchange, Symbol = symbol,
                EntryPrice = entryPrice, PeakPrice = currentPrice,
                SlPrice = initialSl, PartialExited = false, BeMoved = false,
                CreatedAt = now, UpdatedAt = now,
            };
        });

        // 若交易所那邊 avg_entry_price 變了（比如加倉），更新 entry / 重置 SL
        if (state.EntryPrice != entryPrice && entryPrice > 0m)
        {
            _logger.LogInformation("Entry price changed for {Key}: {Old:F4} → {New:F4} — recomputing SL",
                key, state.EntryPrice, entryPrice);
            state.EntryPrice = entryPrice;
            state.SlPrice = entryPrice * (1m - _protectionConfig.InitialSlPct / 100m);
            state.PartialExited = false;
            state.BeMoved = false;
            state.PeakPrice = currentPrice;
        }

        if (currentPrice > state.PeakPrice) state.PeakPrice = currentPrice;
        state.UpdatedAt = now;

        var decision = EvaluateProtection(state, currentPrice, qty, _protectionConfig);
        switch (decision.Action)
        {
            case ProtectionAction.SlHit:
                await ExecuteProtectionOrderAsync(exchange, symbol, "sell", qty, decision.Reason, ct);
                // 記入 SL flush 滑動視窗——夠多 SL 在窗內就把 _enabled 翻 false
                RecordSlHit(exchange, symbol, now);
                // state 在下次 sweep 因 qty=0 自動清掉
                break;

            case ProtectionAction.PartialExit:
            {
                var ok = await ExecuteProtectionOrderAsync(exchange, symbol, "sell", decision.PartialQty, decision.Reason, ct);
                if (ok) state.PartialExited = true;
                break;
            }

            case ProtectionAction.BeMove:
                _logger.LogInformation("BE SL move for {Key}: {Reason}", key, decision.Reason);
                state.SlPrice = decision.NewSlPrice;
                state.BeMoved = true;
                if (_watchList.TryGetValue(key, out var wi))
                    AddLog(wi, "protect", decision.Reason);
                break;

            case ProtectionAction.None:
            default:
                break;
        }
    }

    /// <summary>下保護單（partial exit / SL hit），回傳是否成功送出。</summary>
    private async Task<bool> ExecuteProtectionOrderAsync(
        string exchange, string symbol, string side, decimal qty, string reason, CancellationToken ct)
    {
        var key = $"{exchange}:{symbol}";
        var clientOrderId = $"prot-{exchange}-{symbol}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}".Replace(".", "_");
        if (clientOrderId.Length > 36) clientOrderId = clientOrderId.Substring(0, 36);

        var orderPayload = JsonSerializer.Serialize(new
        {
            exchange, symbol, side, quantity = qty,
            order_type = "market",
            client_order_id = clientOrderId,
        });
        var result = await _dispatcher.DispatchAsync(BuildRequest("trading.order", "place_order", orderPayload));
        if (result.Success)
        {
            _logger.LogInformation(
                "🛡 Protection order placed: {Symbol} {Side} {Qty} on {Exchange} — {Reason}",
                symbol, side, qty, exchange, reason);
            if (_watchList.TryGetValue(key, out var wi))
                AddLog(wi, "protect", $"{side.ToUpper()} {qty} — {reason}");
            return true;
        }
        _logger.LogWarning(
            "Protection order failed: {Symbol} {Side} {Qty} on {Exchange} — {Error}",
            symbol, side, qty, exchange, result.ErrorMessage);
        if (_watchList.TryGetValue(key, out var wi2))
            AddLog(wi2, "error", $"Protection {side} failed: {result.ErrorMessage}");
        return false;
    }

    // ── Phase 4: Perpetual protection sweep ──────────────────────────
    //
    // 對所有 watch 列出的 perp exchange、抓 trading.perpetual.get_positions、
    // 對每個有 qty 的 position 跑 EvaluatePerpetualProtection、執行決策。
    // state 用 (exchange, symbol, side) 三元組 key——同 symbol 雙向倉位（hedge mode）各自記錄。
    private async Task SweepPerpetualProtectionAsync(CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.perpetual")) return;

        // 從 watch list 找出所有 perp exchanges
        var exchanges = _watchList.Values
            .Where(w => w.Mode == "perp_long_only" || w.Mode == "perp_both")
            .Select(w => w.Exchange).Distinct().ToList();
        if (exchanges.Count == 0) return;

        foreach (var exchange in exchanges)
        {
            if (ct.IsCancellationRequested) return;
            var posResult = await _dispatcher.DispatchAsync(BuildRequest("trading.perpetual", "get_positions",
                JsonSerializer.Serialize(new { exchange })));
            if (!posResult.Success) continue;

            var pos = JsonDocument.Parse(posResult.ResultPayload ?? "{}").RootElement;
            if (!pos.TryGetProperty("positions", out var posArr) || posArr.ValueKind != JsonValueKind.Array)
                continue;

            var liveKeys = new HashSet<string>();
            foreach (var p in posArr.EnumerateArray())
            {
                var symbol = p.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                var side = p.TryGetProperty("side", out var sd) ? sd.GetString() ?? "long" : "long";
                var qty = p.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0m;
                var entryPrice = p.TryGetProperty("avg_entry_price", out var e) ? e.GetDecimal() : 0m;
                var markPrice = p.TryGetProperty("mark_price", out var mp) ? mp.GetDecimal() : 0m;
                var liqPrice = p.TryGetProperty("liquidation_price", out var lp) ? lp.GetDecimal() : 0m;
                var liqDist = p.TryGetProperty("liquidation_distance_pct", out var ld) ? ld.GetDecimal() : 0m;
                var leverage = p.TryGetProperty("leverage", out var lv) && lv.TryGetInt32(out var lvI) ? lvI : 1;
                if (string.IsNullOrEmpty(symbol) || qty <= 0m || entryPrice <= 0m || markPrice <= 0m) continue;

                var key = $"{exchange}:{symbol}:{side}";
                liveKeys.Add(key);
                try
                {
                    await ProcessPerpProtectionAsync(exchange, symbol, side, qty, entryPrice, markPrice,
                        liqPrice, liqDist, leverage, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Perp protection check failed for {Key}", key);
                }
            }

            // 清掉 state 裡 exchange 下、live 沒出現的（已被全平的）
            var staleKeys = _perpPositionState
                .Where(kv => kv.Key.StartsWith(exchange + ":") && !liveKeys.Contains(kv.Key))
                .Select(kv => kv.Key).ToList();
            foreach (var k in staleKeys)
            {
                if (_perpPositionState.TryRemove(k, out _))
                {
                    DeletePersistedPerpState(k);
                    _logger.LogInformation("Perp position {Key} closed — protection state cleaned", k);
                }
            }
        }
    }

    private async Task ProcessPerpProtectionAsync(
        string exchange, string symbol, string side, decimal qty,
        decimal entryPrice, decimal markPrice, decimal liqPrice, decimal liqDistPct, int leverage,
        CancellationToken ct)
    {
        var key = $"{exchange}:{symbol}:{side}";
        var now = DateTime.UtcNow;
        var isLong = side == "long";
        var state = _perpPositionState.GetOrAdd(key, _ =>
        {
            var initialSl = isLong
                ? entryPrice * (1m - _protectionConfig.InitialSlPct / 100m)
                : entryPrice * (1m + _protectionConfig.InitialSlPct / 100m);
            _logger.LogInformation(
                "Perp protection state init for {Key}: entry={Entry:F4}, SL={Sl:F4} ({Pct}% {Dir} entry, lev {Lev}x)",
                key, entryPrice, initialSl, _protectionConfig.InitialSlPct, isLong ? "below" : "above", leverage);
            return new PerpetualPositionState
            {
                Exchange = exchange, Symbol = symbol, Side = side,
                EntryPrice = entryPrice, PeakMark = markPrice, SlPrice = initialSl,
                LiquidationPrice = liqPrice, Leverage = leverage,
                PartialExited = false, BeMoved = false,
                CreatedAt = now, UpdatedAt = now,
            };
        });

        // entry 變了（加倉）→ reset state
        if (state.EntryPrice != entryPrice && entryPrice > 0m)
        {
            _logger.LogInformation("Perp entry price changed for {Key}: {Old:F4} → {New:F4} — recomputing SL", key, state.EntryPrice, entryPrice);
            state.EntryPrice = entryPrice;
            state.SlPrice = isLong
                ? entryPrice * (1m - _protectionConfig.InitialSlPct / 100m)
                : entryPrice * (1m + _protectionConfig.InitialSlPct / 100m);
            state.PartialExited = false; state.BeMoved = false; state.PeakMark = markPrice;
        }

        // PeakMark：long 取最高、short 取最低
        if (isLong && markPrice > state.PeakMark) state.PeakMark = markPrice;
        else if (!isLong && (state.PeakMark == 0m || markPrice < state.PeakMark)) state.PeakMark = markPrice;
        state.LiquidationPrice = liqPrice;
        state.UpdatedAt = now;

        var decision = EvaluatePerpetualProtection(state, markPrice, qty, liqDistPct, _protectionConfig, _perpLiqEmergencyPct);
        switch (decision.Action)
        {
            case PerpProtectionAction.LiquidationEmergency:
            case PerpProtectionAction.SlHit:
                await ExecutePerpProtectionOrderAsync(exchange, symbol, side, qty, decision.Reason, ct);
                if (decision.Action == PerpProtectionAction.SlHit)
                    RecordSlHit(exchange, symbol, now);  // 連環 SL flush 也適用
                break;

            case PerpProtectionAction.PartialExit:
            {
                var ok = await ExecutePerpProtectionOrderAsync(exchange, symbol, side, decision.PartialQty, decision.Reason, ct);
                if (ok) state.PartialExited = true;
                break;
            }

            case PerpProtectionAction.BeMove:
                _logger.LogInformation("Perp BE SL move for {Key}: {Reason}", key, decision.Reason);
                state.SlPrice = decision.NewSlPrice;
                state.BeMoved = true;
                if (_watchList.TryGetValue($"{exchange}:{symbol}", out var wi))
                    AddLog(wi, "protect", decision.Reason);
                break;

            case PerpProtectionAction.None:
            default:
                break;
        }

        // 不論 action 為何、把 PeakMark / SlPrice / PartialExited / BeMoved 的最新狀態落 DB——
        // broker 重啟後 cycle 才能用既有 state 恢復、避免「entry 用最新 mark 重算 SL 跑遠」。
        PersistPerpState(key, state);
    }

    /// <summary>
    /// 平倉用：long 倉用 sell + position_side=long + reduce_only；short 倉用 buy + position_side=short + reduce_only。
    /// </summary>
    private async Task<bool> ExecutePerpProtectionOrderAsync(
        string exchange, string symbol, string side, decimal qty, string reason, CancellationToken ct)
    {
        // 平多 = sell + LONG + reduceOnly；平空 = buy + SHORT + reduceOnly
        var orderSide = side == "long" ? "sell" : "buy";
        var watchKey = $"{exchange}:{symbol}";
        var orderPayload = JsonSerializer.Serialize(new
        {
            exchange, symbol, side = orderSide, position_side = side,
            order_type = "market", quantity = qty, reduce_only = true,
        });
        var result = await _dispatcher.DispatchAsync(BuildRequest("trading.perpetual", "place_order", orderPayload));
        if (result.Success)
        {
            _logger.LogInformation("🛡 Perp protection close: {Symbol} {Side}({OrdSide}) {Qty} on {Exchange} — {Reason}",
                symbol, side, orderSide, qty, exchange, reason);
            if (_watchList.TryGetValue(watchKey, out var wi))
                AddLog(wi, "protect", $"perp close {side.ToUpper()} {qty} — {reason}");
            return true;
        }
        _logger.LogWarning("Perp protection close failed: {Symbol} {Side} {Qty} on {Exchange} — {Error}",
            symbol, side, qty, exchange, result.ErrorMessage);
        if (_watchList.TryGetValue(watchKey, out var wi2))
            AddLog(wi2, "error", $"perp protection close failed: {result.ErrorMessage}");
        return false;
    }

    private async Task ProcessSymbolAsync(WatchItem item, CancellationToken ct)
    {
        var symbol   = item.Symbol;
        var exchange = item.Exchange;

        // Step 1: 拉 K 線
        if (!_registry.HasAvailableWorker("quote.ohlcv"))
        {
            AddLog(item, "skip", "quote-worker not connected");
            return;
        }

        var barsPayload = JsonSerializer.Serialize(new { symbol, interval = "1d", limit = 100 });
        var barsResult = await _dispatcher.DispatchAsync(BuildRequest("quote.ohlcv", "get_bars", barsPayload));
        if (!barsResult.Success)
        {
            AddLog(item, "skip", $"Failed to get bars: {barsResult.ErrorMessage}");
            return;
        }

        var barsDoc = JsonDocument.Parse(barsResult.ResultPayload ?? "{}");
        if (!barsDoc.RootElement.TryGetProperty("bars", out var barsArr) || barsArr.GetArrayLength() < 30)
        {
            AddLog(item, "skip", $"Not enough bars: {barsArr.GetArrayLength()}");
            return;
        }

        // Step 2: 策略分析
        if (!_registry.HasAvailableWorker("strategy.signal"))
        {
            AddLog(item, "skip", "strategy-worker not connected");
            return;
        }

        var signalPayload = JsonSerializer.Serialize(new
        {
            strategy = item.Strategy,
            symbol,
            exchange,
            interval = "1d",
            bars = barsArr,
        });
        var signalResult = await _dispatcher.DispatchAsync(BuildRequest("strategy.signal", "evaluate", signalPayload));
        if (!signalResult.Success)
        {
            AddLog(item, "skip", $"Strategy failed: {signalResult.ErrorMessage}");
            return;
        }

        var signal = JsonDocument.Parse(signalResult.ResultPayload ?? "{}").RootElement;
        var action = signal.TryGetProperty("action", out var a) ? a.GetString() ?? "hold" : "hold";
        var confidence = signal.TryGetProperty("confidence", out var c) ? c.GetDecimal() : 0;
        var reason = signal.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

        item.LastSignal = action;
        item.LastConfidence = confidence;
        item.LastCheck = DateTime.UtcNow;
        PersistWatch($"{exchange}:{symbol}", item);

        // Dev-only override：env 設了就覆蓋訊號 + 跳過 threshold（用於 e2e 測試）
        if (_devForceAction != null)
        {
            _logger.LogWarning("⚠ AUTOTRADER_DEV_FORCE_ACTION overriding {Original}@{Conf:P0} → {Forced} for {Symbol}",
                action, confidence, _devForceAction, symbol);
            action = _devForceAction;
            AddLog(item, "force", $"DEV: forcing {action} (real signal was {item.LastSignal}@{confidence:P0})");
        }
        else
        {
            if (action == "hold")
            {
                AddLog(item, "hold", $"Confidence={confidence:P0}. {TruncateReason(reason)}");
                return;
            }

            // 信心度門檻（env AUTOTRADER_MIN_CONFIDENCE 可調，預設 0.5）
            if (confidence < _minConfidence)
            {
                AddLog(item, "skip", $"Signal={action} but confidence {confidence:P0} < {_minConfidence:P0} threshold");
                return;
            }
        }

        // Step 3: 取得價格估算
        decimal price = 0;
        if (signal.TryGetProperty("indicators", out var indicators))
        {
            foreach (var prop in indicators.EnumerateObject())
            {
                if (prop.Name.EndsWith(".price") || prop.Name == "price")
                {
                    price = prop.Value.GetDecimal();
                    break;
                }
            }
        }

        // Step 4: 風控檢查
        if (_registry.HasAvailableWorker("risk.check") && price > 0)
        {
            var riskPayload = JsonSerializer.Serialize(new
            {
                symbol, exchange, side = action,
                quantity = item.Quantity, price,
                portfolio = new { cash = 0, portfolio_value = 0, day_pnl = 0, peak_value = 0, daily_trade_count = 0, positions = Array.Empty<object>() }
            });

            // 嘗試先取得真正的帳戶資訊
            if (_registry.HasAvailableWorker("trading.account"))
            {
                var accResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_account",
                    JsonSerializer.Serialize(new { exchange })));
                var posResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_positions",
                    JsonSerializer.Serialize(new { exchange })));
                // Fill poller 已在維護 trades 表，這裡查當天累計筆數餵給 risk engine 的
                // max_daily_trades 規則（之前永遠傳 0、規則形同虛設）
                var dailyResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "daily_trade_count",
                    JsonSerializer.Serialize(new { exchange })));
                var dailyCount = 0;
                if (dailyResult.Success)
                {
                    var dc = JsonDocument.Parse(dailyResult.ResultPayload ?? "{}").RootElement;
                    if (dc.TryGetProperty("count", out var cnt)) dailyCount = cnt.GetInt32();
                }

                // 每個 (exchange:symbol) 最近交易時間 → cooldown_seconds 規則用，防 signal 抖動連續開單
                var lastTradesResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "last_trade_times", "{}"));
                var lastTradesJson = lastTradesResult.Success
                    ? JsonDocument.Parse(lastTradesResult.ResultPayload ?? "{}").RootElement
                    : JsonDocument.Parse("{}").RootElement;

                if (accResult.Success && posResult.Success)
                {
                    var acc = JsonDocument.Parse(accResult.ResultPayload ?? "{}").RootElement;
                    var pos = JsonDocument.Parse(posResult.ResultPayload ?? "{}").RootElement;

                    // ── Portfolio circuit breaker（B1）──
                    // 在 risk-engine 之前先查當日 DD，超過 _maxPortfolioDdPct 就直接擋、
                    // 不浪費 risk-engine 的算力、log 也明確標 halt 而非 risk reject。
                    var portfolioValue = acc.TryGetProperty("portfolio_value", out var pvCb) ? pvCb.GetDecimal() : 0m;
                    var cb = EvaluateCircuitBreaker(exchange, portfolioValue, DateTime.UtcNow);
                    if (cb.Triggered)
                    {
                        _logger.LogWarning(
                            "⚠ Circuit breaker triggered on {Exchange}: DD {Dd}% ≥ {Threshold}% (peak={Peak:C}, current={Cur:C}). Skipping {Symbol} {Action}.",
                            exchange, cb.DdPct, cb.Threshold, cb.PeakValue, cb.CurrentValue, symbol, action);
                        AddLog(item, "halt",
                            $"⚠ Portfolio DD {cb.DdPct:F1}% ≥ {cb.Threshold}% on {exchange} (peak={cb.PeakValue:F2}, now={cb.CurrentValue:F2}) — order blocked");
                        return;
                    }

                    var lastTradesDict = new Dictionary<string, DateTime>();
                    if (lastTradesJson.TryGetProperty("last_trades", out var lt) && lt.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in lt.EnumerateObject())
                            if (prop.Value.ValueKind == JsonValueKind.String && DateTime.TryParse(prop.Value.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                                lastTradesDict[prop.Name] = dt;
                    }

                    riskPayload = JsonSerializer.Serialize(new
                    {
                        symbol, exchange, side = action,
                        quantity = item.Quantity, price,
                        portfolio = new
                        {
                            cash = acc.TryGetProperty("cash", out var cash) ? cash.GetDecimal() : 0,
                            portfolio_value = acc.TryGetProperty("portfolio_value", out var pv) ? pv.GetDecimal() : 0,
                            day_pnl = acc.TryGetProperty("day_pnl", out var dp) ? dp.GetDecimal() : 0,
                            peak_value = acc.TryGetProperty("portfolio_value", out var pk) ? pk.GetDecimal() : 0,
                            daily_trade_count = dailyCount,
                            positions = pos.TryGetProperty("positions", out var posArr2) ? posArr2 : JsonDocument.Parse("[]").RootElement,
                            last_trade_by_symbol = lastTradesDict,
                        }
                    });
                }
            }

            var riskResult = await _dispatcher.DispatchAsync(BuildRequest("risk.check", "pre_order", riskPayload));
            if (riskResult.Success)
            {
                var riskDoc = JsonDocument.Parse(riskResult.ResultPayload ?? "{}").RootElement;
                var passed = riskDoc.TryGetProperty("passed", out var p) && p.GetBoolean();
                var orderAction = riskDoc.TryGetProperty("order_action", out var oa) ? oa.GetString() : "reject";

                if (!passed && orderAction == "reject")
                {
                    AddLog(item, "blocked", "Risk check rejected order");
                    return;
                }

                if (orderAction == "reduce" && riskDoc.TryGetProperty("adjusted_qty", out var aq))
                {
                    item.Quantity = aq.GetDecimal();
                    PersistWatch($"{exchange}:{symbol}", item);
                    AddLog(item, "adjusted", $"Risk reduced qty to {item.Quantity}");
                }
            }
        }

        // Step 5: 下單 — spot 走 trading.order；perp_* 走 trading.perpetual 並做 signal→open/close 映射
        if (item.Mode == "perp_long_only" || item.Mode == "perp_both")
        {
            await PlacePerpOrderForSignalAsync(item, action, ct);
        }
        else
        {
            await PlaceSpotOrderAsync(item, action, exchange, symbol, ct);
        }
    }

    /// <summary>既有 spot 下單路徑（拆出來讓 perp 分支不亂）。</summary>
    private async Task PlaceSpotOrderAsync(WatchItem item, string action, string exchange, string symbol, CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.order"))
        {
            AddLog(item, "skip", "trading-worker not connected");
            return;
        }

        // Deterministic client_order_id：同一 5 分鐘 bucket 內 retry 同 (exchange/symbol/side/qty)
        // 都會收到同一個 ID，trading-worker 端 + 交易所端各有一道 dedup
        var bucket5min = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300;
        var clientOrderId = BuildAutoOrderKey(exchange, symbol, action, item.Quantity, bucket5min);

        var orderPayload = JsonSerializer.Serialize(new
        {
            exchange, symbol, side = action,
            quantity = item.Quantity, order_type = "market",
            client_order_id = clientOrderId,
        });

        var orderResult = await _dispatcher.DispatchAsync(BuildRequest("trading.order", "place_order", orderPayload));
        if (orderResult.Success)
        {
            var order = JsonDocument.Parse(orderResult.ResultPayload ?? "{}").RootElement;
            var orderId = order.TryGetProperty("order_id", out var oid) ? oid.GetString() : "?";
            var status = order.TryGetProperty("status", out var st) ? st.GetString() : "?";
            var isDedup = order.TryGetProperty("idempotent", out var idem) && idem.GetBoolean();
            if (isDedup)
                AddLog(item, "dedup", $"[DEDUP] {orderId} same-bucket retry, no new exchange call (existing status={status})");
            else
            {
                AddLog(item, action, $"ORDER PLACED: {orderId} {action} {item.Quantity} {symbol} @ market → {status}");
                _logger.LogInformation("AutoTrader: {Action} {Qty} {Symbol} on {Exchange} → {Status}",
                    action, item.Quantity, symbol, exchange, status);
            }
        }
        else
        {
            AddLog(item, "error", $"Order failed: {orderResult.ErrorMessage}");
        }
    }

    /// <summary>
    /// Phase 3：perp 分支——把 strategy 的 buy/sell 訊號映射到 open/close + LONG/SHORT。
    ///
    /// 映射規則：先查當前部位、再決定動作
    ///   perp_long_only:
    ///     buy  + 無倉            → open long
    ///     buy  + 已 long          → skip (不加倉)
    ///     sell + has long         → close long
    ///     sell + 無倉             → skip (long_only 不開空)
    ///   perp_both:
    ///     buy  + 無倉            → open long
    ///     buy  + has long         → skip
    ///     buy  + has short        → close short (保守、不立即翻多)
    ///     sell + 無倉             → open short
    ///     sell + has short        → skip
    ///     sell + has long         → close long (保守、不立即翻空)
    ///
    /// 翻倉等下次 cycle 才開、避免一次 cycle 內連續打交易所。
    /// </summary>
    private async Task PlacePerpOrderForSignalAsync(WatchItem item, string action, CancellationToken ct)
    {
        if (!_registry.HasAvailableWorker("trading.perpetual"))
        {
            AddLog(item, "skip", "trading-worker has no perpetual capability (BingX disabled?)");
            return;
        }

        // 拉現有部位
        var posResult = await _dispatcher.DispatchAsync(BuildRequest("trading.perpetual", "get_positions",
            JsonSerializer.Serialize(new { exchange = item.Exchange })));
        if (!posResult.Success)
        {
            AddLog(item, "skip", $"perp get_positions failed: {posResult.ErrorMessage}");
            return;
        }

        var posDoc = JsonDocument.Parse(posResult.ResultPayload ?? "{}").RootElement;
        decimal longQty = 0m, shortQty = 0m;
        if (posDoc.TryGetProperty("positions", out var posArr) && posArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in posArr.EnumerateArray())
            {
                var sym = p.TryGetProperty("symbol", out var s) ? s.GetString() : "";
                if (sym != item.Symbol) continue;
                var side = p.TryGetProperty("side", out var sd) ? sd.GetString() : "";
                var qty = p.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0m;
                if (side == "long") longQty = qty;
                else if (side == "short") shortQty = qty;
            }
        }

        // Map signal → (perpAction, sideOverride, positionSide, qtyOverride, reduceOnly)
        string? perpAction = null;
        string? perpSide = null;
        string? perpPosSide = null;
        decimal qtyToUse = item.Quantity;
        bool reduceOnly = false;

        if (action == "buy")
        {
            if (longQty > 0m)         perpAction = $"skip:already-long ({longQty})";
            else if (shortQty > 0m)   { perpAction = "close_short"; perpSide = "buy";  perpPosSide = "short"; qtyToUse = shortQty; reduceOnly = true; }
            else                      { perpAction = "open_long";   perpSide = "buy";  perpPosSide = "long";  reduceOnly = false; }
        }
        else // sell
        {
            if (shortQty > 0m)        perpAction = $"skip:already-short ({shortQty})";
            else if (longQty > 0m)    { perpAction = "close_long"; perpSide = "sell"; perpPosSide = "long";  qtyToUse = longQty; reduceOnly = true; }
            else if (item.Mode == "perp_long_only") perpAction = "skip:long-only-no-open-short";
            else                      { perpAction = "open_short"; perpSide = "sell"; perpPosSide = "short"; reduceOnly = false; }
        }

        if (perpSide == null)
        {
            AddLog(item, "skip", $"perp[{item.Mode}] {action} → {perpAction}");
            return;
        }

        // ── Risk gate（只擋開倉、平倉永遠放行）──
        // 平倉（reduceOnly=true）必須一律允許——擋出場才是真風險。
        // 開倉前 fetch mark price 估名目、把現有 positions 一起餵給 risk-worker 的 pre_perp_order。
        if (!reduceOnly && _registry.HasAvailableWorker("risk.check"))
        {
            decimal markPrice = 0m;
            var mpResult = await _dispatcher.DispatchAsync(BuildRequest("trading.perpetual", "get_mark_price",
                JsonSerializer.Serialize(new { exchange = item.Exchange, symbol = item.Symbol })));
            if (mpResult.Success)
            {
                var mpDoc = JsonDocument.Parse(mpResult.ResultPayload ?? "{}").RootElement;
                if (mpDoc.TryGetProperty("mark_price", out var mp)) markPrice = mp.GetDecimal();
            }

            // 把 get_positions 結果整理成 risk-worker 接受的 perp snapshot 形狀
            var perpPositions = new List<object>();
            decimal balance = 0m, available = 0m;
            if (posDoc.TryGetProperty("positions", out var allPos) && allPos.ValueKind == JsonValueKind.Array)
            {
                foreach (var pp in allPos.EnumerateArray())
                {
                    var pq = pp.TryGetProperty("quantity",   out var pqv) ? pqv.GetDecimal() : 0m;
                    var pmk = pp.TryGetProperty("mark_price", out var pmkv) ? pmkv.GetDecimal() : 0m;
                    perpPositions.Add(new
                    {
                        symbol        = pp.TryGetProperty("symbol",       out var pps)  ? pps.GetString() : "",
                        exchange      = pp.TryGetProperty("exchange",     out var pex)  ? pex.GetString() : "",
                        position_side = pp.TryGetProperty("side",         out var pside)? pside.GetString() : "",
                        quantity      = pq,
                        mark_price    = pmk,
                        notional      = pq * pmk,
                        leverage      = pp.TryGetProperty("leverage",     out var pl) && pl.TryGetInt32(out var pli) ? pli : 1,
                        liquidation_distance_pct = pp.TryGetProperty("liquidation_distance_pct", out var pld) ? pld.GetDecimal() : 0m,
                    });
                }
            }
            // account 也撈一下、給未來規則用（目前 3 條規則沒用到 balance、但留 payload 給後續）
            var accResult = await _dispatcher.DispatchAsync(BuildRequest("trading.perpetual", "get_account",
                JsonSerializer.Serialize(new { exchange = item.Exchange })));
            if (accResult.Success)
            {
                var ad = JsonDocument.Parse(accResult.ResultPayload ?? "{}").RootElement;
                if (ad.TryGetProperty("balance",          out var bv))  balance   = bv.GetDecimal();
                if (ad.TryGetProperty("available_margin", out var amv)) available = amv.GetDecimal();
            }

            var riskPayload = JsonSerializer.Serialize(new
            {
                symbol        = item.Symbol,
                exchange      = item.Exchange,
                side          = perpSide,
                position_side = perpPosSide,
                quantity      = qtyToUse,
                price         = markPrice,
                leverage      = item.Leverage,
                initial_sl_pct = _protectionConfig.InitialSlPct,   // 給 r14 max_loss_per_trade_pct 用
                perp = new { balance, available_margin = available, positions = perpPositions },
            });
            var riskResult = await _dispatcher.DispatchAsync(BuildRequest("risk.check", "pre_perp_order", riskPayload));
            if (riskResult.Success)
            {
                var riskDoc = JsonDocument.Parse(riskResult.ResultPayload ?? "{}").RootElement;
                var passed = riskDoc.TryGetProperty("passed", out var rp) && rp.GetBoolean();
                if (!passed)
                {
                    var msgs = new List<string>();
                    if (riskDoc.TryGetProperty("violations", out var vs) && vs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var v in vs.EnumerateArray())
                            if (v.TryGetProperty("message", out var vm)) msgs.Add(vm.GetString() ?? "");
                    }
                    var summary = msgs.Count > 0 ? string.Join("; ", msgs) : "perp risk rejected";
                    AddLog(item, "blocked", $"PERP risk blocked: {summary}");
                    _logger.LogWarning("AutoTrader perp risk blocked {Symbol} on {Exchange}: {Msg}", item.Symbol, item.Exchange, summary);
                    return;
                }
            }
            // riskResult.Success == false → risk-worker 不在線；保留 fail-open 行為跟 spot 路徑一致
        }

        var perpPayload = JsonSerializer.Serialize(new
        {
            exchange = item.Exchange,
            symbol = item.Symbol,
            side = perpSide,
            position_side = perpPosSide,
            order_type = "market",
            quantity = qtyToUse,
            leverage = item.Leverage,
            reduce_only = reduceOnly,
        });
        var orderResult = await _dispatcher.DispatchAsync(BuildRequest("trading.perpetual", "place_order", perpPayload));
        if (orderResult.Success)
        {
            var ord = JsonDocument.Parse(orderResult.ResultPayload ?? "{}").RootElement;
            var status = ord.TryGetProperty("status", out var st) ? st.GetString() : "?";
            var extId = ord.TryGetProperty("external_id", out var ei) ? ei.GetString() : "?";
            AddLog(item, perpAction!, $"PERP {perpAction!.ToUpper()}: {extId} {perpSide} {qtyToUse} {item.Symbol} {item.Leverage}x → {status}");
            _logger.LogInformation("AutoTrader perp: {PerpAction} {Side} {Qty} {Symbol} on {Exchange} {Lev}x → {Status}",
                perpAction, perpSide, qtyToUse, item.Symbol, item.Exchange, item.Leverage, status);
        }
        else
        {
            AddLog(item, "error", $"perp order failed: {orderResult.ErrorMessage}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// 算 deterministic client_order_id —— 拆出來方便單測 + 文檔化合約規則：
    ///   - 同 (exchange, symbol, action, quantity, bucket) 永遠回同一個 key
    ///   - bucket 不同 → key 不同（跨時間視窗的新意圖不會被 dedup）
    ///   - dot 換 underscore（Binance newClientOrderId 限 [a-zA-Z0-9-_]）
    ///   - 截到 36 char（Binance 上限）
    /// </summary>
    internal static string BuildAutoOrderKey(string exchange, string symbol, string action, decimal quantity, long bucket)
    {
        var rawKey = $"auto-{exchange}-{symbol}-{action}-{quantity:G}-{bucket}".Replace('.', '_');
        return rawKey.Length > 36 ? rawKey[..36] : rawKey;
    }

    private static ApprovedRequest BuildRequest(string capabilityId, string route, string payload = "{}")
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId, Route = route, Payload = payload,
            Scope = "{}", PrincipalId = "system", TaskId = "auto-trader", SessionId = "auto-trader"
        };

    private void AddLog(WatchItem item, string action, string message)
    {
        _tradeLog.Enqueue(new TradeLog
        {
            Symbol = item.Symbol, Exchange = item.Exchange,
            Action = action, Message = message,
        });
        while (_tradeLog.Count > MaxLogEntries) _tradeLog.TryDequeue(out _);
    }

    private static string TruncateReason(string reason)
        => reason.Length > 120 ? reason[..120] + "…" : reason;
}

// ── Models ──────────────────────────────────────────────────────────

public class WatchItem
{
    public string Symbol   { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Strategy { get; set; } = "composite";
    public decimal Quantity { get; set; } = 1;
    public bool Active     { get; set; } = true;
    public string? LastSignal    { get; set; }
    public decimal LastConfidence { get; set; }
    public DateTime? LastCheck    { get; set; }
    /// <summary>"spot" / "perp_long_only" / "perp_both" — 預設 spot 保持既有行為。</summary>
    public string Mode     { get; set; } = "spot";
    /// <summary>perpetual 模式開倉用槓桿。spot 模式忽略。預設 5x。</summary>
    public int Leverage    { get; set; } = 5;
}

public class TradeLog
{
    public string Symbol   { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Action   { get; set; } = "";
    public string Message  { get; set; } = "";
    public DateTime Time   { get; set; } = DateTime.UtcNow;
}
