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
        _logger.LogInformation(
            "AutoTrader thresholds: confidence={Conf:P0} portfolio_dd={Dd}% · " +
            "protection: initial_sl={IniSl}% partial_exit={Pe}% (sell {Per:P0}) BE_trigger={Bet}% (buffer {Beb}%)",
            _minConfidence, _maxPortfolioDdPct,
            _protectionConfig.InitialSlPct, _protectionConfig.PartialExitPct, _protectionConfig.PartialExitRatio,
            _protectionConfig.BreakevenTriggerPct, _protectionConfig.BreakevenBufferPct);

        LoadWatchListFromDb();
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
                    CreatedAt = now, UpdatedAt = now,
                });
            }
            else
            {
                existing.Symbol = item.Symbol; existing.Exchange = item.Exchange;
                existing.Strategy = item.Strategy; existing.Quantity = item.Quantity; existing.Active = item.Active;
                existing.LastSignal = item.LastSignal; existing.LastConfidence = item.LastConfidence; existing.LastCheck = item.LastCheck;
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

    // ── 外部控制 API ────────────────────────────────────────────────

    public bool IsEnabled => _enabled;
    public int IntervalSeconds => _intervalSeconds;
    public IReadOnlyDictionary<string, WatchItem> WatchList => _watchList;
    public IEnumerable<TradeLog> RecentLogs => _tradeLog.ToArray().Take(MaxLogEntries);

    public void Enable() { _enabled = true; _logger.LogInformation("AutoTrader ENABLED"); }
    public void Disable() { _enabled = false; _logger.LogInformation("AutoTrader DISABLED"); }
    public void SetInterval(int seconds) { _intervalSeconds = Math.Max(60, seconds); }

    public void AddWatch(string symbol, string exchange, string strategy = "composite", decimal quantity = 1)
    {
        var key = $"{exchange}:{symbol}";
        var item = new WatchItem
        {
            Symbol = symbol, Exchange = exchange, Strategy = strategy,
            Quantity = quantity, Active = true,
        };
        _watchList[key] = item;
        PersistWatch(key, item);
        _logger.LogInformation("AutoTrader: watching {Key} strategy={Strategy} qty={Qty}", key, strategy, quantity);
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

            // Step 0: Position protection sweep——先處理現有部位的部分平倉 / SL hit / BE 移動，
            // 之後的 watch loop 才不會在已被 SL 平掉的 symbol 又開新單
            try { await SweepPositionProtectionAsync(ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "AutoTrader protection sweep failed"); }

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

        // Step 5: 下單
        if (!_registry.HasAvailableWorker("trading.order"))
        {
            AddLog(item, "skip", "trading-worker not connected");
            return;
        }

        // Deterministic client_order_id：同一 5 分鐘 bucket 內 retry 同 (exchange/symbol/side/qty)
        // 都會收到同一個 ID，trading-worker 端 + 交易所端各有一道 dedup（DB 查 + Alpaca/Binance
        // client_order_id unique 約束）。bucket = 5 分鐘正好對到預設 poll interval；
        // 跨 bucket 是新意圖、會用新 ID（不會被 dedup 擋）。
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
            // idempotent=true → trading-worker DB 命中 client_order_id dedup、根本沒打交易所，
            // log 加 [DEDUP] 前綴讓 webhook / dashboard 知道這不是新成交
            var isDedup = order.TryGetProperty("idempotent", out var idem) && idem.GetBoolean();
            if (isDedup)
            {
                AddLog(item, "dedup", $"[DEDUP] {orderId} same-bucket retry, no new exchange call (existing status={status})");
            }
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
}

public class TradeLog
{
    public string Symbol   { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Action   { get; set; } = "";
    public string Message  { get; set; } = "";
    public DateTime Time   { get; set; } = DateTime.UtcNow;
}
