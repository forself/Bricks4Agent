using RiskWorker.Models;

namespace RiskWorker.Engine;

/// <summary>
/// 風控引擎 — 在下單前檢查一系列規則，決定是否放行。
///
/// 規則類型（spot, 走 Check 路徑）：
/// - max_position:        單一標的最大持倉市值
/// - max_portfolio_pct:   單一標的佔投組最大比例
/// - max_order_size:      單筆訂單最大金額
/// - max_daily_loss:      當日最大虧損金額
/// - max_drawdown_pct:    最大回撤百分比（從歷史峰值算）
/// - max_daily_trades:    當日最大交易次數
/// - min_cash_reserve:    買入後須保留的現金底（避免梭哈耗光現金）
/// - max_position_count:  最多同時持有幾個不同標的（避免過度分散 / 防呆）
/// - cooldown_seconds:    同一 (exchange,symbol) 兩次成交之間的最短間隔，防 signal 抖動連續開單
/// - time_window:         只允許在指定 UTC 時段下單（Params: {"start_hm":"HH:mm","end_hm":"HH:mm"}）
///
/// 規則類型（perp, 走 CheckPerp 路徑）：
/// - max_leverage:              開倉允許的最高槓桿倍數
/// - max_total_notional:        所有開倉的「淨曝險」名目（每 symbol |long-short| 加總、
///                              對沖友善：同 symbol 多空互抵）+ 本筆預判
/// - max_liquidation_distance:  最低可接受的「mark→liq」距離百分比；
///                              預估值 = (1/leverage − 0.005) × 100，過低就拒
/// - max_loss_per_trade_pct:    單筆預估最大損 ≤ 合約資金 N%（用 InitialSlPct 線性估）
/// - max_positions_per_side:    同方向最多 N 倉；若反向有 K 倉則放寬到 N+K（淨方向 ≤ N）
/// 注意：perp 的「平倉」（SELL+LONG / BUY+SHORT）永遠放行——任何規則都不該擋出場、
/// 否則就跑不掉爛單。CheckPerp 會在最前面短路掉這種請求。
/// </summary>
public class RiskEngine
{
    private readonly List<RiskRule> _rules;

    public RiskEngine(List<RiskRule> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// 檢查一筆預計下單是否通過風控。
    /// </summary>
    public RiskCheckResult Check(
        string symbol,
        string exchange,
        string side,
        decimal quantity,
        decimal estimatedPrice,
        PortfolioSnapshot portfolio)
    {
        var violations = new List<RiskViolation>();
        var orderValue = quantity * estimatedPrice;

        foreach (var rule in _rules.Where(r => r.Enabled))
        {
            // 檢查規則是否適用於此 symbol/exchange
            if (rule.Symbol != null && !rule.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            if (rule.Exchange != null && !rule.Exchange.Equals(exchange, StringComparison.OrdinalIgnoreCase))
                continue;

            var violation = rule.Type switch
            {
                "max_position"        => CheckMaxPosition(rule, symbol, orderValue, side, portfolio),
                "max_portfolio_pct"   => CheckMaxPortfolioPct(rule, symbol, orderValue, side, portfolio),
                "max_order_size"      => CheckMaxOrderSize(rule, orderValue),
                "max_daily_loss"      => CheckMaxDailyLoss(rule, portfolio),
                "max_drawdown_pct"    => CheckMaxDrawdown(rule, portfolio),
                "max_daily_trades"    => CheckMaxDailyTrades(rule, portfolio),
                "min_cash_reserve"    => CheckMinCashReserve(rule, orderValue, side, portfolio),
                "max_position_count"  => CheckMaxPositionCount(rule, symbol, side, portfolio),
                "cooldown_seconds"    => CheckCooldown(rule, symbol, exchange, portfolio),
                "time_window"         => CheckTimeWindow(rule),
                _ => null
            };

            if (violation != null)
                violations.Add(violation);
        }

        if (violations.Count == 0)
        {
            return new RiskCheckResult
            {
                Passed      = true,
                OrderAction = "allow",
            };
        }

        // 嘗試 reduce：看 max_position / max_order_size 能否縮小數量
        decimal? adjustedQty = TryReduce(symbol, side, quantity, estimatedPrice, portfolio);

        return new RiskCheckResult
        {
            Passed      = false,
            OrderAction = adjustedQty.HasValue && adjustedQty.Value > 0 ? "reduce" : "reject",
            AdjustedQty = adjustedQty,
            Violations  = violations,
        };
    }

    // ── 各規則檢查 ──────────────────────────────────────────────────

    private RiskViolation? CheckMaxPosition(RiskRule rule, string symbol, decimal orderValue, string side, PortfolioSnapshot portfolio)
    {
        if (side == "sell") return null; // 賣出不會增加曝險

        var existing = portfolio.Positions
            .Where(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.MarketValue);

        var projected = existing + orderValue;
        if (projected <= rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Position in {symbol} would be ${projected:N0}, exceeds limit ${rule.Threshold:N0}",
            Current  = projected,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxPortfolioPct(RiskRule rule, string symbol, decimal orderValue, string side, PortfolioSnapshot portfolio)
    {
        if (side == "sell" || portfolio.PortfolioValue <= 0) return null;

        var existing = portfolio.Positions
            .Where(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.MarketValue);

        var projectedPct = (existing + orderValue) / portfolio.PortfolioValue * 100;
        if (projectedPct <= rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"{symbol} would be {projectedPct:F1}% of portfolio, exceeds {rule.Threshold}% limit",
            Current  = Math.Round(projectedPct, 1),
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxOrderSize(RiskRule rule, decimal orderValue)
    {
        if (orderValue <= rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Order size ${orderValue:N0} exceeds limit ${rule.Threshold:N0}",
            Current  = orderValue,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxDailyLoss(RiskRule rule, PortfolioSnapshot portfolio)
    {
        var dailyLoss = Math.Abs(Math.Min(0, portfolio.DayPnl));
        if (dailyLoss <= rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Daily loss ${dailyLoss:N0} exceeds limit ${rule.Threshold:N0}. Trading halted.",
            Current  = dailyLoss,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxDrawdown(RiskRule rule, PortfolioSnapshot portfolio)
    {
        if (portfolio.PeakValue <= 0) return null;

        var drawdownPct = (portfolio.PeakValue - portfolio.PortfolioValue) / portfolio.PeakValue * 100;
        if (drawdownPct <= rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Drawdown {drawdownPct:F1}% exceeds limit {rule.Threshold}%. Trading halted.",
            Current  = Math.Round(drawdownPct, 1),
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxDailyTrades(RiskRule rule, PortfolioSnapshot portfolio)
    {
        if (portfolio.DailyTradeCount < (int)rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Daily trade count {portfolio.DailyTradeCount} reached limit {(int)rule.Threshold}",
            Current  = portfolio.DailyTradeCount,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMinCashReserve(RiskRule rule, decimal orderValue, string side, PortfolioSnapshot portfolio)
    {
        // 只擋買入（賣出會增加現金、不會違反 reserve）
        if (side != "buy") return null;

        var projectedCash = portfolio.Cash - orderValue;
        if (projectedCash >= rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Buy would leave cash at ${projectedCash:N0}, below reserve floor ${rule.Threshold:N0}",
            Current  = projectedCash,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckTimeWindow(RiskRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Params)) return null;

        TimeOnly start, end;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rule.Params);
            var root = doc.RootElement;
            var startStr = root.TryGetProperty("start_hm", out var s) ? s.GetString() : null;
            var endStr   = root.TryGetProperty("end_hm",   out var e) ? e.GetString() : null;
            if (!TimeOnly.TryParse(startStr, out start) || !TimeOnly.TryParse(endStr, out end))
                return null;   // params 解析不出來就 silently 不擋（避免被打死）
        }
        catch
        {
            return null;
        }

        var nowUtc = TimeOnly.FromDateTime(DateTime.UtcNow);
        // 跨午夜支援：start <= end 是同日視窗、start > end 是跨午夜（如 22:00-04:00）
        var inWindow = start <= end ? (nowUtc >= start && nowUtc <= end) : (nowUtc >= start || nowUtc <= end);
        if (inWindow) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Outside trading window {start:HH\\:mm}-{end:HH\\:mm} UTC (now {nowUtc:HH\\:mm})",
            Current  = nowUtc.Hour * 100 + nowUtc.Minute,
            Limit    = end.Hour * 100 + end.Minute,
        };
    }

    private RiskViolation? CheckCooldown(RiskRule rule, string symbol, string exchange, PortfolioSnapshot portfolio)
    {
        // cooldown 同時擋 buy 和 sell —— signal 抖動會兩個方向都觸發
        var key = $"{exchange}:{symbol}";
        if (!portfolio.LastTradeBySymbol.TryGetValue(key, out var lastTrade)) return null;

        var elapsedSec = (DateTime.UtcNow - lastTrade).TotalSeconds;
        if (elapsedSec >= (double)rule.Threshold) return null;

        var remaining = (int)((double)rule.Threshold - elapsedSec);
        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"{symbol} traded {(int)elapsedSec}s ago; cooldown requires {(int)rule.Threshold}s ({remaining}s remaining)",
            Current  = (decimal)elapsedSec,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxPositionCount(RiskRule rule, string symbol, string side, PortfolioSnapshot portfolio)
    {
        // 只擋會「新增」標的的買入；既有 symbol 的加碼不算新位
        if (side != "buy") return null;

        var alreadyHeld = portfolio.Positions.Any(p =>
            p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && p.Quantity > 0);
        if (alreadyHeld) return null;

        var distinctCount = portfolio.Positions.Where(p => p.Quantity > 0).Select(p => p.Symbol.ToUpperInvariant()).Distinct().Count();
        if (distinctCount < (int)rule.Threshold) return null;

        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Already holding {distinctCount} symbols (limit {(int)rule.Threshold}); buying {symbol} would add a new position",
            Current  = distinctCount,
            Limit    = rule.Threshold,
        };
    }

    // ── 永續合約檢查 ────────────────────────────────────────────────

    /// <summary>
    /// 檢查一筆永續合約預計下單。平倉永遠放行；只擋開倉。
    /// </summary>
    public RiskCheckResult CheckPerp(
        string symbol,
        string exchange,
        string side,            // BUY / SELL
        string positionSide,    // LONG / SHORT
        decimal quantity,
        decimal estimatedPrice,
        int leverage,
        PerpetualSnapshot snapshot,
        decimal initialSlPct = 5m)   // 從 broker 的 protection_config 帶來、給 max_loss_per_trade_pct 用
    {
        // hedge mode：(side, positionSide) 組合決定開/平。平倉永遠放行——擋不出場才是真風險。
        var sideUp = side?.ToUpperInvariant() ?? "";
        var psUp = positionSide?.ToUpperInvariant() ?? "";
        var isClosing = (sideUp == "SELL" && psUp == "LONG") || (sideUp == "BUY" && psUp == "SHORT");
        if (isClosing)
        {
            return new RiskCheckResult { Passed = true, OrderAction = "allow" };
        }

        var violations = new List<RiskViolation>();
        var orderNotional = quantity * estimatedPrice;

        foreach (var rule in _rules.Where(r => r.Enabled))
        {
            if (rule.Symbol != null && !rule.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            if (rule.Exchange != null && !rule.Exchange.Equals(exchange, StringComparison.OrdinalIgnoreCase))
                continue;

            var v = rule.Type switch
            {
                "max_leverage"               => CheckMaxLeverage(rule, leverage),
                "max_total_notional"         => CheckMaxTotalNotional(rule, symbol, psUp, orderNotional, snapshot),
                "max_liquidation_distance"   => CheckMaxLiquidationDistance(rule, leverage),
                "max_loss_per_trade_pct"     => CheckMaxLossPerTradePct(rule, orderNotional, initialSlPct, snapshot),
                "max_positions_per_side"     => CheckMaxPositionsPerSide(rule, psUp, snapshot),
                "max_perp_daily_loss_pct"    => CheckMaxPerpDailyLossPct(rule, snapshot),
                _ => null
            };
            if (v != null) violations.Add(v);
        }

        return violations.Count == 0
            ? new RiskCheckResult { Passed = true, OrderAction = "allow" }
            : new RiskCheckResult { Passed = false, OrderAction = "reject", Violations = violations };
    }

    private RiskViolation? CheckMaxLeverage(RiskRule rule, int leverage)
    {
        if (leverage <= rule.Threshold) return null;
        return new RiskViolation
        {
            RuleId = rule.RuleId, RuleName = rule.Name,
            Message = $"Leverage {leverage}x exceeds limit {(int)rule.Threshold}x",
            Current = leverage, Limit = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxTotalNotional(RiskRule rule, string orderSymbol, string orderPositionSide, decimal orderNotional, PerpetualSnapshot snap)
    {
        // 用「淨曝險」算總名目：每個 symbol 的 long 跟 short 互相抵銷、再加總跨 symbol。
        // 這樣對沖部位（同 symbol 多空都有）對 r12 的計入就只剩|long-short|、給策略留空間做避險。
        // 純單邊（沒對沖）行為跟 gross 完全一樣。
        decimal netSum = 0m;
        var symbols = snap.Positions.Select(p => p.Symbol).Append(orderSymbol).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var sym in symbols)
        {
            var symLong = snap.Positions
                .Where(p => p.Symbol.Equals(sym, StringComparison.OrdinalIgnoreCase) && p.PositionSide.Equals("long", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Notional);
            var symShort = snap.Positions
                .Where(p => p.Symbol.Equals(sym, StringComparison.OrdinalIgnoreCase) && p.PositionSide.Equals("short", StringComparison.OrdinalIgnoreCase))
                .Sum(p => p.Notional);
            // 預判：把這筆新單也加進去算
            if (sym.Equals(orderSymbol, StringComparison.OrdinalIgnoreCase))
            {
                if (orderPositionSide == "LONG") symLong += orderNotional;
                else if (orderPositionSide == "SHORT") symShort += orderNotional;
            }
            netSum += Math.Abs(symLong - symShort);
        }
        if (netSum <= rule.Threshold) return null;
        return new RiskViolation
        {
            RuleId = rule.RuleId, RuleName = rule.Name,
            Message = $"Net perp notional {netSum:F0} USDT (after this order, hedge-aware) exceeds limit {rule.Threshold:F0}",
            Current = netSum, Limit = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxLossPerTradePct(RiskRule rule, decimal orderNotional, decimal initialSlPct, PerpetualSnapshot snap)
    {
        // 預估單筆最大損失 = 名目 × SL 距離百分比（線性、不考慮 BE 移動跟部分平倉、保守）
        // 例：0.0001 BTC × $80k = $8 名目；SL 5% → 觸發時損 $0.40
        // 跟 equity（= snap.Balance）比、超過 threshold% 就拒。equity = 0 時跳過避免除零。
        if (snap.Balance <= 0m || initialSlPct <= 0m) return null;
        var estimatedLoss = orderNotional * initialSlPct / 100m;
        var lossPct = estimatedLoss / snap.Balance * 100m;
        if (lossPct <= rule.Threshold) return null;
        return new RiskViolation
        {
            RuleId = rule.RuleId, RuleName = rule.Name,
            Message = $"Estimated loss {estimatedLoss:F2} USDT ({lossPct:F2}% of equity {snap.Balance:F2}, SL {initialSlPct}%) exceeds limit {rule.Threshold:F2}%",
            Current = lossPct, Limit = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxPositionsPerSide(RiskRule rule, string orderPositionSide, PerpetualSnapshot snap)
    {
        // 「同方向最多 N 倉」、但對沖時放寬：
        // 若反向已有 K 倉、本側上限 = N + K（淨方向部位 ≤ N）。
        // 若反向 0 倉（純單邊）、就嚴格 N。
        var sameSideKey = orderPositionSide == "LONG" ? "long" : "short";
        var oppositeKey = orderPositionSide == "LONG" ? "short" : "long";
        var sameCount = snap.Positions.Count(p => p.PositionSide.Equals(sameSideKey, StringComparison.OrdinalIgnoreCase));
        var oppositeCount = snap.Positions.Count(p => p.PositionSide.Equals(oppositeKey, StringComparison.OrdinalIgnoreCase));
        var effectiveLimit = (int)rule.Threshold + oppositeCount;
        if (sameCount < effectiveLimit) return null;
        var hedgeNote = oppositeCount > 0 ? $" (relaxed by {oppositeCount} hedge positions)" : "";
        return new RiskViolation
        {
            RuleId = rule.RuleId, RuleName = rule.Name,
            Message = $"Already holding {sameCount} {sameSideKey} positions (limit {(int)rule.Threshold}{hedgeNote})",
            Current = sameCount, Limit = effectiveLimit,
        };
    }

    private RiskViolation? CheckMaxPerpDailyLossPct(RiskRule rule, PerpetualSnapshot snap)
    {
        // DayPnlPct 是 (current - today_open) / today_open × 100；負值 = 賠錢。
        // 規則 threshold 是「容許今天最多虧 N %」(正值)。比較 |loss| ≥ threshold。
        // caller 不填 → 0、永遠 pass（不誤觸熔斷）。
        var todayLossPct = -Math.Min(0m, snap.DayPnlPct);
        if (todayLossPct < rule.Threshold) return null;
        return new RiskViolation
        {
            RuleId   = rule.RuleId,
            RuleName = rule.Name,
            Message  = $"Daily perp loss {todayLossPct:F2}% reached limit {rule.Threshold:F1}% — circuit breaker tripped, no new opens until UTC reset.",
            Current  = todayLossPct,
            Limit    = rule.Threshold,
        };
    }

    private RiskViolation? CheckMaxLiquidationDistance(RiskRule rule, int leverage)
    {
        // Threshold 語意：可接受的最低「mark→liq」距離百分比（越大越保守）。
        // 開倉前我們手上沒實際 liq price（要等 BingX 回 position 才有）、
        // 所以用保守預估：liq_dist ≈ (1/leverage − maintenance_rate) × 100
        // BingX USDT-M 各幣 maintenance margin rate 不同（BTC 0.4%、ETH 0.5%、alts 1%+）
        // 用 0.5% 做近似；要更精準就要 fetch 各幣 maintenance rate（之後優化）。
        if (leverage <= 0) return null;
        var forecastPct = (1m / leverage - 0.005m) * 100m;
        if (forecastPct >= rule.Threshold) return null;
        return new RiskViolation
        {
            RuleId = rule.RuleId, RuleName = rule.Name,
            Message = $"Forecasted liquidation distance {forecastPct:F2}% (lev={leverage}x) below minimum {rule.Threshold:F2}%",
            Current = forecastPct, Limit = rule.Threshold,
        };
    }

    // ── 嘗試縮小訂單 ────────────────────────────────────────────────

    private decimal? TryReduce(string symbol, string side, decimal quantity, decimal price, PortfolioSnapshot portfolio)
    {
        if (side == "sell") return null;

        // 找 max_position 規則的最小可用額度
        var positionRule = _rules.FirstOrDefault(r =>
            r.Enabled && r.Type == "max_position" &&
            (r.Symbol == null || r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)));

        if (positionRule == null || price <= 0) return null;

        var existing = portfolio.Positions
            .Where(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.MarketValue);

        var available = positionRule.Threshold - existing;
        if (available <= 0) return null;

        var maxQty = available / price;
        return maxQty >= 1 ? Math.Floor(maxQty) : null; // 至少要能買 1 單位
    }

    /// <summary>預設風控規則集。</summary>
    public static List<RiskRule> DefaultRules() => new()
    {
        new() { RuleId = "r1", Name = "Max Position Size",        Type = "max_position",       Threshold = 10_000 },
        new() { RuleId = "r2", Name = "Max Portfolio Allocation", Type = "max_portfolio_pct",  Threshold = 25 },
        new() { RuleId = "r3", Name = "Max Single Order",         Type = "max_order_size",     Threshold = 5_000 },
        new() { RuleId = "r4", Name = "Max Daily Loss",           Type = "max_daily_loss",     Threshold = 1_000 },
        new() { RuleId = "r5", Name = "Max Drawdown",             Type = "max_drawdown_pct",   Threshold = 10 },
        new() { RuleId = "r6", Name = "Max Daily Trades",         Type = "max_daily_trades",   Threshold = 20 },
        new() { RuleId = "r7", Name = "Min Cash Reserve",         Type = "min_cash_reserve",   Threshold = 500 },
        new() { RuleId = "r8", Name = "Max Position Count",       Type = "max_position_count", Threshold = 10 },
        new() { RuleId = "r9", Name = "Symbol Cooldown",          Type = "cooldown_seconds",   Threshold = 60 },
        // r10 預設 disabled —— 24/7 加密貨幣不需要、美股使用者再啟動並設 params:
        // {"start_hm":"13:30","end_hm":"20:00"} 對應 NYSE 9:30-16:00 ET（DST 期間 UTC-4）
        new() { RuleId = "r10", Name = "Trading Hours Window",    Type = "time_window",        Threshold = 0, Enabled = false,
                Params = "{\"start_hm\":\"13:30\",\"end_hm\":\"20:00\"}" },

        // ── 永續合約規則（給 BingX perp 用、走 CheckPerp 路徑、平倉永遠放行）────
        // r11: 槓桿上限 10x——對 30 USDT 起步帳戶這已經很激進，但留空間給之後調大
        new() { RuleId = "r11", Name = "Max Perp Leverage",         Type = "max_leverage",            Threshold = 10 },
        // r12: 所有開倉名目（含本筆）≤ 1000 USDT——首次實盤期保守設小，accustomed 後再放
        new() { RuleId = "r12", Name = "Max Perp Total Notional",   Type = "max_total_notional",      Threshold = 1000 },
        // r13: 最低距離爆倉 5%——10x 預估 ~9.5% 過、20x ~4.5% 擋。算保守。
        new() { RuleId = "r13", Name = "Min Liquidation Distance",  Type = "max_liquidation_distance",Threshold = 5 },
        // r14: 單筆預估損 ≤ 合約資金 2%（用 InitialSlPct 線性估、保守）
        new() { RuleId = "r14", Name = "Max Loss Per Trade %",      Type = "max_loss_per_trade_pct",  Threshold = 2 },
        // r15: 同方向最多 5 倉；對沖時放寬到 5+反向倉數
        new() { RuleId = "r15", Name = "Max Positions Per Side",    Type = "max_positions_per_side",  Threshold = 5 },
        // r16: 當日 perp 帳戶虧損 > 6% → 整天熔斷不再開新倉。
        // 用 (current_balance - today_open_balance) / today_open_balance × 100 算、
        // 由 AutoTraderService 維護 perp_daily_open_balance 表跨日 reset。平倉永遠放行不受影響。
        // 6% = 例如 $99 容許今日內 $5.94 虧損；3 筆 r14 約滿就觸發、留 1 筆緩衝。
        new() { RuleId = "r16", Name = "Max Perp Daily Loss %",     Type = "max_perp_daily_loss_pct", Threshold = 6 },
    };
}
