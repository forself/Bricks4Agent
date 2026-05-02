using RiskWorker.Models;

namespace RiskWorker.Engine;

/// <summary>
/// 風控引擎 — 在下單前檢查一系列規則，決定是否放行。
///
/// 規則類型：
/// - max_position:        單一標的最大持倉市值
/// - max_portfolio_pct:   單一標的佔投組最大比例
/// - max_order_size:      單筆訂單最大金額
/// - max_daily_loss:      當日最大虧損金額
/// - max_drawdown_pct:    最大回撤百分比（從歷史峰值算）
/// - max_daily_trades:    當日最大交易次數
/// - min_cash_reserve:    買入後須保留的現金底（避免梭哈耗光現金）
/// - max_position_count:  最多同時持有幾個不同標的（避免過度分散 / 防呆）
/// - cooldown_seconds:    同一 (exchange,symbol) 兩次成交之間的最短間隔，防 signal 抖動連續開單
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
    };
}
