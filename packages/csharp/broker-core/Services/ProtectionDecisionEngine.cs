namespace BrokerCore.Services;

// D1 Phase 2 — 從 AutoTraderService 抽出來的「保護決策」單一職責。
//
// 抽出的：
//   - ProtectionConfig（SL / partial exit / BE / trailing 配置）
//   - PositionProtectionState / PerpetualPositionState（per-position 跟蹤狀態）
//   - ProtectionAction enum + ProtectionDecision（spot）
//   - PerpProtectionAction enum + PerpProtectionDecision（perp）
//   - SlHitRecord（SL flush 滑動視窗）
//   - EvaluateProtection / EvaluatePerpetualProtection（pure decision functions）
//   - ParseProtectionConfig（env → config）
//
// 沒抽的（留 AutoTraderService）：
//   - _protectionConfig instance field（讓 sweep 直接用）
//   - _positionState / _perpPositionState ConcurrentDictionary（per-watch state cache）
//   - Persist / Hydrate from DB（PerpetualPositionStateEntry）
//   - Sweep 主迴圈（呼叫 dispatcher 平倉、寫 audit、推 notification）
//
// 為什麼：pure decisions 是 protection 邏輯最核心、也最 well-defined 的部分。先抽這個、
// 其他帶 IO / state 的之後再切（避免一次動太大、real-money risk）。
//
// 所有類型放 Broker.Services namespace 而非 AutoTraderService nested、以後 test
// 直接 reference X 不用 AutoTraderService.X。

// ── Config ───────────────────────────────────────────────────────────

/// <summary>
/// 保護機制配置（從 env 讀、AutoTrader ctor 跑一次帶進來）。
/// spot 跟 perp 共用同一份 config、實際決策由 EvaluateProtection / EvaluatePerpetualProtection 分流。
/// </summary>
public class ProtectionConfig
{
    public decimal InitialSlPct        { get; init; }  // 進場時 SL 距離 entry 的 %（fixed fallback）
    public decimal PartialExitPct      { get; init; }  // 漲 ≥ 此 % 觸發部分平倉
    public decimal PartialExitRatio    { get; init; }  // 部分平倉的數量比例 [0, 1]
    public decimal BreakevenTriggerPct { get; init; }  // 漲 ≥ 此 % SL 移到 BE
    public decimal BreakevenBufferPct  { get; init; }  // BE 上方留多少 buffer（避免價差掃損）

    /// <summary>
    /// Trailing lock：peak gain 達此 % 後啟動拖移停損鎖獲利。
    /// 跟 BE 互補——BE 只把 SL 移到開倉價、trailing 是把 SL 隨 peak 上移、鎖住更多獲利。
    /// 0 = 關閉（純 BE 模式、向下相容）。預設 0。
    /// </summary>
    public decimal TrailingTriggerPct  { get; init; }

    /// <summary>Trailing 距離：啟動後 SL = PeakPrice × (1 - TrailingDistancePct/100)。預設 2%。</summary>
    public decimal TrailingDistancePct { get; init; }

    /// <summary>
    /// ATR-based SL multiplier。&gt; 0 啟用 ATR 模式：SL = entry ± multiplier × ATR；
    /// 同時把 effective SL% clamp 在 [InitialSlPct × 0.5, InitialSlPct × 3]。
    /// 0 = fixed mode。預設 0（向後相容）。
    /// </summary>
    public decimal AtrSlMultiplier     { get; init; }
    public int     AtrPeriod           { get; init; }
    public string  AtrInterval         { get; init; } = "1h";
}

// ── State 容器 ───────────────────────────────────────────────────────

/// <summary>Spot position 保護狀態（per-watch、由 sweep 維護）。</summary>
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

/// <summary>
/// Perpetual position 保護狀態（per-(owner, exchange, symbol, side)）。
/// 跟 spot 故意分開：side / liquidationPrice / leverage 是 perp 獨有。
/// </summary>
public class PerpetualPositionState
{
    /// <summary>A2.5b PASS 2：擁有者 principal_id。state key = {owner}:{exchange}:{symbol}:{side}</summary>
    public string OwnerPrincipalId { get; set; } = "prn_dashboard";
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

/// <summary>SL flush 滑動視窗紀錄（觸發 SL 後寫一筆、過期淘汰）。</summary>
public class SlHitRecord
{
    public string Exchange { get; init; } = "";
    public string Symbol   { get; init; } = "";
    public DateTime At     { get; init; }
}

// ── Decision 回傳 ────────────────────────────────────────────────────

public enum ProtectionAction { None, SlHit, PartialExit, BeMove, TrailingLock }

public class ProtectionDecision
{
    public ProtectionAction Action { get; init; }
    public decimal PartialQty      { get; init; }
    public decimal NewSlPrice      { get; init; }
    public decimal PnlPct          { get; init; }
    public string Reason           { get; init; } = "";
}

public enum PerpProtectionAction { None, SlHit, PartialExit, BeMove, LiquidationEmergency, TrailingLock }

public class PerpProtectionDecision
{
    public PerpProtectionAction Action { get; init; }
    public decimal PartialQty       { get; init; }
    public decimal NewSlPrice       { get; init; }
    public decimal PnlPct           { get; init; }
    public decimal LiqDistancePct   { get; init; }
    public string Reason            { get; init; } = "";
}

// ── Decision engine static class ─────────────────────────────────────

public static class ProtectionDecisionEngine
{
    /// <summary>
    /// 從 env 讀 ProtectionConfig（被 AutoTraderService ctor 呼叫一次）。
    /// AUTOTRADER_INITIAL_SL_PCT 等 env 維持相容。
    /// </summary>
    public static ProtectionConfig ParseConfig() => new()
    {
        InitialSlPct        = ParsePctEnv("AUTOTRADER_INITIAL_SL_PCT",        5m,   0.5m, 50m),
        PartialExitPct      = ParsePctEnv("AUTOTRADER_PARTIAL_EXIT_PCT",      5m,   0.5m, 100m),
        PartialExitRatio    = ParseRatioEnv("AUTOTRADER_PARTIAL_EXIT_RATIO",  0.5m),
        BreakevenTriggerPct = ParsePctEnv("AUTOTRADER_BREAKEVEN_TRIGGER_PCT", 3m,   0.5m, 100m),
        BreakevenBufferPct  = ParsePctEnv("AUTOTRADER_BREAKEVEN_BUFFER_PCT",  0.5m, 0m,   10m),
        TrailingTriggerPct  = ParsePctEnv("AUTOTRADER_TRAILING_TRIGGER_PCT",  0m,   0m,   100m),
        TrailingDistancePct = ParsePctEnv("AUTOTRADER_TRAILING_DISTANCE_PCT", 2m,   0.1m, 50m),
        AtrSlMultiplier     = ParsePctEnv("AUTOTRADER_ATR_SL_MULTIPLIER",     0m,   0m,   10m),
        AtrPeriod           = (int)ParsePctEnv("AUTOTRADER_ATR_PERIOD",       14m,  5m,   100m),
        AtrInterval         = Environment.GetEnvironmentVariable("AUTOTRADER_ATR_INTERVAL") ?? "1h",
    };

    /// <summary>
    /// Spot 保護決策（pure function、無 IO）。
    /// 優先順序：SL hit &gt; Partial exit &gt; BE move &gt; Trailing lock &gt; None
    /// 一次只回一個 action；下次 sweep 自然接著走。
    ///
    /// BE 跟 trailing 都用 peak gain 判斷（從歷史 peak 計算）、不用 current pnl。
    /// 避免「曾經 +5% 但回檔 +0.5%、BE 沒鎖到、又跌回 -X% 全賠」的悲劇。
    /// </summary>
    public static ProtectionDecision EvaluateProtection(
        PositionProtectionState state, decimal currentPrice, decimal qty, ProtectionConfig config)
    {
        if (state.EntryPrice <= 0m || qty <= 0m || currentPrice <= 0m)
            return new ProtectionDecision { Action = ProtectionAction.None, Reason = "invalid inputs" };

        var pnlPct  = (currentPrice    - state.EntryPrice) / state.EntryPrice * 100m;
        var effectivePeak = Math.Max(state.PeakPrice, currentPrice);
        var peakPct = (effectivePeak - state.EntryPrice) / state.EntryPrice * 100m;

        // 1) SL hit (含 BE / trailing 後挪過的 SL)
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

        // 3) BE SL move (peak-based)
        if (!state.BeMoved && peakPct >= config.BreakevenTriggerPct)
        {
            var newSl = state.EntryPrice * (1m + config.BreakevenBufferPct / 100m);
            if (newSl > state.SlPrice)
            {
                return new ProtectionDecision
                {
                    Action = ProtectionAction.BeMove,
                    NewSlPrice = newSl,
                    PnlPct = pnlPct,
                    Reason = $"SL → BE +{config.BreakevenBufferPct}% (was {state.SlPrice:F4}, now {newSl:F4}) at peak +{peakPct:F2}%",
                };
            }
        }

        // 4) Trailing lock
        if (config.TrailingTriggerPct > 0m && peakPct >= config.TrailingTriggerPct)
        {
            var trailSl = effectivePeak * (1m - config.TrailingDistancePct / 100m);
            if (trailSl > state.SlPrice)
            {
                return new ProtectionDecision
                {
                    Action = ProtectionAction.TrailingLock,
                    NewSlPrice = trailSl,
                    PnlPct = pnlPct,
                    Reason = $"Trailing lock: SL {state.SlPrice:F4} → {trailSl:F4} (peak {effectivePeak:F4} −{config.TrailingDistancePct}%) at peak +{peakPct:F2}%",
                };
            }
        }

        return new ProtectionDecision { Action = ProtectionAction.None, PnlPct = pnlPct };
    }

    /// <summary>
    /// Perpetual 保護決策（pure function、無 IO）。
    /// 優先順序：LiquidationEmergency &gt; SlHit &gt; PartialExit &gt; BeMove &gt; TrailingLock &gt; None
    /// 強平距離保護排第一：即使 SL 還沒到、若離強平太近也要先平。
    ///
    /// 雙向 SL math：long mark ≤ sl 觸發、short mark ≥ sl 觸發。
    /// 雙向 PnL%：long = (mark-entry)/entry、short = (entry-mark)/entry。
    /// 雙向 peak：long 看最高 mark、short 看最低 mark。
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
        var effectivePeak = isLong
            ? Math.Max(state.PeakMark, markPrice)
            : (state.PeakMark > 0m ? Math.Min(state.PeakMark, markPrice) : markPrice);
        var peakPct = isLong
            ? (effectivePeak  - state.EntryPrice) / state.EntryPrice * 100m
            : (state.EntryPrice - effectivePeak)  / state.EntryPrice * 100m;

        // 1) Liquidation emergency
        if (state.LiquidationPrice > 0m && liqDistancePct > 0m && liqDistancePct <= liqEmergencyPct)
        {
            return new PerpProtectionDecision
            {
                Action = PerpProtectionAction.LiquidationEmergency,
                PartialQty = qty, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                Reason = $"⚠ liquidation emergency: distance {liqDistancePct:F2}% ≤ {liqEmergencyPct}% (mark {markPrice:F4}, liq {state.LiquidationPrice:F4})",
            };
        }

        // 2) SL hit
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

        // 4) BE SL move (peak-based、雙向)
        if (!state.BeMoved && peakPct >= config.BreakevenTriggerPct)
        {
            var newSl = isLong
                ? state.EntryPrice * (1m + config.BreakevenBufferPct / 100m)
                : state.EntryPrice * (1m - config.BreakevenBufferPct / 100m);
            var moves = isLong ? newSl > state.SlPrice : newSl < state.SlPrice;
            if (moves)
            {
                return new PerpProtectionDecision
                {
                    Action = PerpProtectionAction.BeMove,
                    NewSlPrice = newSl, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                    Reason = $"SL → BE ({state.Side}) {(isLong ? "+" : "−")}{config.BreakevenBufferPct}% (was {state.SlPrice:F4}, now {newSl:F4}) at peak +{peakPct:F2}%",
                };
            }
        }

        // 5) Trailing lock — peak 後拖移 SL（雙向）
        if (config.TrailingTriggerPct > 0m && peakPct >= config.TrailingTriggerPct)
        {
            var trailSl = isLong
                ? effectivePeak * (1m - config.TrailingDistancePct / 100m)
                : effectivePeak * (1m + config.TrailingDistancePct / 100m);
            var moves = isLong ? trailSl > state.SlPrice : trailSl < state.SlPrice;
            if (moves)
            {
                return new PerpProtectionDecision
                {
                    Action = PerpProtectionAction.TrailingLock,
                    NewSlPrice = trailSl, PnlPct = pnlPct, LiqDistancePct = liqDistancePct,
                    Reason = $"Trailing lock ({state.Side}): SL {state.SlPrice:F4} → {trailSl:F4} (peak {effectivePeak:F4} {(isLong ? "−" : "+")}{config.TrailingDistancePct}%) at peak +{peakPct:F2}%",
                };
            }
        }

        return new PerpProtectionDecision { Action = PerpProtectionAction.None, PnlPct = pnlPct, LiqDistancePct = liqDistancePct };
    }

    // ── Env parsing（複製自 AutoTraderService 避免反向依賴） ──────────────

    private static decimal ParsePctEnv(string envName, decimal defaultValue, decimal min, decimal max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return defaultValue;
        return Math.Clamp(v, min, max);
    }

    private static decimal ParseRatioEnv(string envName, decimal defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return defaultValue;
        return Math.Clamp(v, 0.01m, 1m);
    }
}
