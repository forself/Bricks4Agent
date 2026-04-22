using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// 投資組合績效分析 — 從 trading-worker 抓成交紀錄，計算 P&L / Sharpe / MaxDD / Win Rate 等指標。
/// 全部計算即時進行，不持久化；若未來要快取可再包一層。
/// </summary>
public class PortfolioAnalyticsService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;

    public PortfolioAnalyticsService(IExecutionDispatcher dispatcher, IWorkerRegistry registry)
    {
        _dispatcher = dispatcher;
        _registry = registry;
    }

    public async Task<PortfolioMetrics> GetMetricsAsync(string exchange, int tradeLimit = 500)
    {
        var trades = await FetchTradesAsync(exchange, tradeLimit);
        var closed = MatchRoundTripsFifo(trades);
        var curve = BuildEquityCurve(closed);
        var dailyReturns = ComputeDailyReturns(curve);

        // Topic O：未平倉部位的 mark-to-market
        var livePositions = await FetchLivePositionsAsync(exchange);
        var totalUnrealizedPnl = livePositions.Sum(p => p.UnrealizedPnl);
        var totalMarketValue   = livePositions.Sum(p => p.MarketValue);
        var realizedPnl        = closed.Sum(t => t.Pnl);

        var winners = closed.Where(t => t.Pnl > 0).ToList();
        var losers = closed.Where(t => t.Pnl < 0).ToList();

        return new PortfolioMetrics
        {
            Exchange = exchange,
            GeneratedAt = DateTime.UtcNow,
            TradeCount = trades.Count,
            RoundTripCount = closed.Count,
            WinningTrades = winners.Count,
            LosingTrades = losers.Count,
            WinRate = closed.Count == 0 ? 0m : Math.Round((decimal)winners.Count / closed.Count, 4),

            // 三種 P&L 口徑（一個 view 看全部）
            RealizedPnl = Math.Round(realizedPnl, 4),
            UnrealizedPnl = Math.Round(totalUnrealizedPnl, 4),
            TotalEquity = Math.Round(totalMarketValue + realizedPnl, 4),
            LivePositions = livePositions,

            TotalPnl = Math.Round(realizedPnl + totalUnrealizedPnl, 4),  // 合計（已實現+未實現）
            GrossProfit = Math.Round(winners.Sum(t => t.Pnl), 4),
            GrossLoss = Math.Round(Math.Abs(losers.Sum(t => t.Pnl)), 4),
            ProfitFactor = ComputeProfitFactor(winners, losers),
            AvgWin = winners.Count == 0 ? 0m : Math.Round(winners.Average(t => t.Pnl), 4),
            AvgLoss = losers.Count == 0 ? 0m : Math.Round(losers.Average(t => t.Pnl), 4),
            LargestWin = closed.Count == 0 ? 0m : Math.Round(closed.Max(t => t.Pnl), 4),
            LargestLoss = closed.Count == 0 ? 0m : Math.Round(closed.Min(t => t.Pnl), 4),
            MaxDrawdown = Math.Round(ComputeMaxDrawdown(curve), 4),
            MaxDrawdownPct = Math.Round(ComputeMaxDrawdownPct(curve) * 100m, 4),
            SharpeRatio = Math.Round(ComputeSharpe(dailyReturns), 4),
            SortinoRatio = Math.Round(ComputeSortino(dailyReturns), 4),
            EquityCurve = curve,
            PerSymbol = closed.GroupBy(t => t.Symbol)
                .Select(g =>
                {
                    var tradesInGroup = g.ToList();
                    var wins = tradesInGroup.Count(t => t.Pnl > 0);
                    return new SymbolStats
                    {
                        Symbol = g.Key,
                        Trades = tradesInGroup.Count,
                        Pnl = Math.Round(tradesInGroup.Sum(t => t.Pnl), 4),
                        WinRate = tradesInGroup.Count == 0 ? 0m : Math.Round((decimal)wins / tradesInGroup.Count, 4),
                    };
                })
                .OrderByDescending(s => s.Pnl)
                .ToList(),
            RecentRoundTrips = closed
                .OrderByDescending(t => t.ExitAt)
                .Take(20)
                .Select(t => new
                {
                    symbol = t.Symbol,
                    entry_at = t.EntryAt,
                    exit_at = t.ExitAt,
                    entry_price = t.EntryPrice,
                    exit_price = t.ExitPrice,
                    quantity = t.Quantity,
                    pnl = Math.Round(t.Pnl, 4),
                    pnl_pct = Math.Round(t.PnlPct * 100m, 4),
                })
                .Cast<object>()
                .ToList(),
        };
    }

    // ── 資料取得 ─────────────────────────────────────────────────────

    /// <summary>
    /// 抓當前持倉、對每個 symbol 取最新報價、算 mark-to-market 浮動損益。
    /// 任何一步失敗就回空列表——portfolio 頁仍能顯示已實現 P&L。
    /// </summary>
    private async Task<List<LivePosition>> FetchLivePositionsAsync(string exchange)
    {
        if (!_registry.HasAvailableWorker("trading.account"))
            return new List<LivePosition>();

        // 1. 拿持倉
        var posPayload = JsonSerializer.Serialize(new { exchange });
        var posResult = await _dispatcher.DispatchAsync(BuildRequest("trading.account", "get_positions", posPayload));
        if (!posResult.Success || string.IsNullOrWhiteSpace(posResult.ResultPayload))
            return new List<LivePosition>();

        var rawPositions = new List<(string symbol, decimal qty, decimal avgEntry)>();
        try
        {
            using var doc = JsonDocument.Parse(posResult.ResultPayload);
            if (doc.RootElement.TryGetProperty("positions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    var symbol = GetStr(p, "symbol");
                    if (string.IsNullOrEmpty(symbol)) continue;
                    var qty = GetDec(p, "quantity", "qty");
                    var avg = GetDec(p, "avg_entry_price", "avg_price", "cost_basis");
                    if (qty != 0m && avg > 0m)
                        rawPositions.Add((symbol, qty, avg));
                }
            }
        }
        catch { return new List<LivePosition>(); }

        if (rawPositions.Count == 0) return new List<LivePosition>();

        // 2. 對每個 symbol 拿最新報價（批次走 quote.prices）
        var quotes = await FetchCurrentPricesAsync();

        // 3. 算 mark-to-market
        var result = new List<LivePosition>();
        foreach (var (symbol, qty, avg) in rawPositions)
        {
            var current = quotes.TryGetValue(symbol, out var p) ? p : avg;  // 沒報價 fallback 到成本價（= 0 浮動損益）
            var marketValue = qty * current;
            var unrealized = qty * (current - avg);
            var unrealizedPct = avg == 0m ? 0m : (current - avg) / avg * 100m;
            result.Add(new LivePosition
            {
                Symbol = symbol,
                Quantity = Math.Round(qty, 4),
                AvgEntryPrice = Math.Round(avg, 4),
                CurrentPrice = Math.Round(current, 4),
                MarketValue = Math.Round(marketValue, 4),
                UnrealizedPnl = Math.Round(unrealized, 4),
                UnrealizedPnlPct = Math.Round(unrealizedPct, 4),
            });
        }
        return result.OrderByDescending(p => Math.Abs(p.UnrealizedPnl)).ToList();
    }

    private async Task<Dictionary<string, decimal>> FetchCurrentPricesAsync()
    {
        var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (!_registry.HasAvailableWorker("quote.prices")) return dict;

        var result = await _dispatcher.DispatchAsync(BuildRequest("quote.prices", "get_prices"));
        if (!result.Success || string.IsNullOrWhiteSpace(result.ResultPayload)) return dict;

        try
        {
            using var doc = JsonDocument.Parse(result.ResultPayload);
            var root = doc.RootElement;
            JsonElement quotes;
            if (root.TryGetProperty("quotes", out var q1)) quotes = q1;
            else if (root.TryGetProperty("data", out var d) && d.TryGetProperty("quotes", out var q2)) quotes = q2;
            else return dict;

            foreach (var q in quotes.EnumerateArray())
            {
                var sym = GetStr(q, "symbol");
                var price = GetDec(q, "price", "last");
                if (!string.IsNullOrEmpty(sym) && price > 0m)
                    dict[sym] = price;
            }
        }
        catch { }
        return dict;
    }

    private async Task<List<Trade>> FetchTradesAsync(string exchange, int limit)
    {
        if (!_registry.HasAvailableWorker("trading.account"))
            return new List<Trade>();

        var payload = JsonSerializer.Serialize(new { exchange, limit });
        var req = BuildRequest("trading.account", "get_trades", payload);
        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success || string.IsNullOrWhiteSpace(result.ResultPayload))
            return new List<Trade>();

        try
        {
            using var doc = JsonDocument.Parse(result.ResultPayload);
            var root = doc.RootElement;

            // 嘗試從多個可能結構找 trades 陣列
            JsonElement tradesEl = default;
            bool found = false;
            if (root.TryGetProperty("trades", out var t1) && t1.ValueKind == JsonValueKind.Array)
            {
                tradesEl = t1; found = true;
            }
            else if (root.TryGetProperty("data", out var d) && d.TryGetProperty("trades", out var t2) && t2.ValueKind == JsonValueKind.Array)
            {
                tradesEl = t2; found = true;
            }
            if (!found) return new List<Trade>();

            var list = new List<Trade>();
            foreach (var t in tradesEl.EnumerateArray())
            {
                var trade = new Trade
                {
                    Symbol = GetStr(t, "symbol"),
                    Side = GetStr(t, "side").ToLowerInvariant(),
                    Quantity = GetDec(t, "quantity", "qty", "filled_qty"),
                    Price = GetDec(t, "price", "filled_avg_price", "avg_price"),
                    FilledAt = GetDate(t, "filled_at", "timestamp", "created_at"),
                };
                if (trade.Quantity > 0 && !string.IsNullOrEmpty(trade.Symbol))
                    list.Add(trade);
            }
            return list.OrderBy(t => t.FilledAt).ToList();
        }
        catch
        {
            return new List<Trade>();
        }
    }

    // ── 配對 (FIFO round-trip) ────────────────────────────────────────

    private List<ClosedTrade> MatchRoundTripsFifo(List<Trade> trades)
    {
        var closed = new List<ClosedTrade>();
        // per-symbol FIFO lots (LinkedList for in-place head adjustment)
        var lots = new Dictionary<string, LinkedList<LotEntry>>();

        foreach (var t in trades)
        {
            if (!lots.ContainsKey(t.Symbol))
                lots[t.Symbol] = new LinkedList<LotEntry>();
            var q = lots[t.Symbol];

            if (t.Side == "buy")
            {
                q.AddLast(new LotEntry { Qty = t.Quantity, Price = t.Price, At = t.FilledAt });
            }
            else if (t.Side == "sell")
            {
                var remaining = t.Quantity;
                while (remaining > 0m && q.First != null)
                {
                    var head = q.First.Value;
                    var matched = Math.Min(head.Qty, remaining);
                    closed.Add(new ClosedTrade
                    {
                        Symbol = t.Symbol,
                        EntryAt = head.At,
                        ExitAt = t.FilledAt,
                        EntryPrice = head.Price,
                        ExitPrice = t.Price,
                        Quantity = matched,
                        Pnl = matched * (t.Price - head.Price),
                        PnlPct = head.Price == 0m ? 0m : (t.Price - head.Price) / head.Price,
                    });
                    remaining -= matched;
                    if (matched == head.Qty)
                    {
                        q.RemoveFirst();
                    }
                    else
                    {
                        q.First.Value = new LotEntry { Qty = head.Qty - matched, Price = head.Price, At = head.At };
                    }
                }
                // 剩餘 sell 數量沒配到 = 空單或資料不全，這裡直接忽略
            }
        }
        return closed;
    }

    // ── 指標計算 ─────────────────────────────────────────────────────

    private List<EquityPoint> BuildEquityCurve(List<ClosedTrade> closed)
    {
        var cum = 0m;
        var list = new List<EquityPoint>();
        foreach (var t in closed.OrderBy(t => t.ExitAt))
        {
            cum += t.Pnl;
            list.Add(new EquityPoint { At = t.ExitAt, Equity = Math.Round(cum, 4) });
        }
        return list;
    }

    private List<decimal> ComputeDailyReturns(List<EquityPoint> curve)
    {
        if (curve.Count < 2) return new List<decimal>();
        var byDay = curve.GroupBy(p => p.At.Date)
            .Select(g => new { Day = g.Key, Equity = g.Last().Equity })
            .OrderBy(x => x.Day)
            .ToList();

        var returns = new List<decimal>();
        for (int i = 1; i < byDay.Count; i++)
        {
            var prev = byDay[i - 1].Equity;
            var curr = byDay[i].Equity;
            // 用「相對前一日絕對值」避免負權益時正負號反轉
            var baseVal = Math.Abs(prev);
            if (baseVal > 0) returns.Add((curr - prev) / baseVal);
        }
        return returns;
    }

    private decimal ComputeMaxDrawdown(List<EquityPoint> curve)
    {
        if (curve.Count == 0) return 0m;
        decimal peak = curve[0].Equity;
        decimal maxDD = 0m;
        foreach (var p in curve)
        {
            if (p.Equity > peak) peak = p.Equity;
            var dd = peak - p.Equity;
            if (dd > maxDD) maxDD = dd;
        }
        return maxDD;
    }

    private decimal ComputeMaxDrawdownPct(List<EquityPoint> curve)
    {
        if (curve.Count == 0) return 0m;
        decimal peak = curve[0].Equity;
        decimal maxDD = 0m;
        foreach (var p in curve)
        {
            if (p.Equity > peak) peak = p.Equity;
            if (peak > 0m)
            {
                var dd = (peak - p.Equity) / peak;
                if (dd > maxDD) maxDD = dd;
            }
        }
        return maxDD;
    }

    private decimal ComputeSharpe(List<decimal> returns)
    {
        if (returns.Count < 2) return 0m;
        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        var stdDev = (decimal)Math.Sqrt((double)variance);
        if (stdDev == 0m) return 0m;
        return mean / stdDev * (decimal)Math.Sqrt(252.0);
    }

    private decimal ComputeSortino(List<decimal> returns)
    {
        if (returns.Count < 2) return 0m;
        var mean = returns.Average();
        var downside = returns.Where(r => r < 0m).ToList();
        if (downside.Count == 0) return 0m;
        var downsideVar = downside.Sum(r => r * r) / downside.Count;
        var downsideDev = (decimal)Math.Sqrt((double)downsideVar);
        if (downsideDev == 0m) return 0m;
        return mean / downsideDev * (decimal)Math.Sqrt(252.0);
    }

    private decimal ComputeProfitFactor(List<ClosedTrade> winners, List<ClosedTrade> losers)
    {
        var gross = winners.Sum(t => t.Pnl);
        var loss = Math.Abs(losers.Sum(t => t.Pnl));
        if (loss == 0m) return gross > 0m ? 9999m : 0m;
        return Math.Round(gross / loss, 4);
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static decimal GetDec(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var dn)) return dn;
            if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds)) return ds;
        }
        return 0m;
    }

    private static DateTime GetDate(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (!el.TryGetProperty(n, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
                return dt.ToUniversalTime();
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ts))
                return DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
        }
        return DateTime.UtcNow;
    }

    private static ApprovedRequest BuildRequest(string capabilityId, string route, string payload = "{}")
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId,
            Route = route,
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "portfolio",
            SessionId = "portfolio",
        };

    // ── DTO ──────────────────────────────────────────────────────────

    private class LotEntry
    {
        public decimal Qty { get; set; }
        public decimal Price { get; set; }
        public DateTime At { get; set; }
    }
}

public class Trade
{
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime FilledAt { get; set; }
}

public class ClosedTrade
{
    public string Symbol { get; set; } = "";
    public DateTime EntryAt { get; set; }
    public DateTime ExitAt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Pnl { get; set; }
    public decimal PnlPct { get; set; }
}

public class EquityPoint
{
    public DateTime At { get; set; }
    public decimal Equity { get; set; }
}

public class SymbolStats
{
    public string Symbol { get; set; } = "";
    public int Trades { get; set; }
    public decimal Pnl { get; set; }
    public decimal WinRate { get; set; }
}

public class PortfolioMetrics
{
    public string Exchange { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public int TradeCount { get; set; }
    public int RoundTripCount { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }

    // Topic O：三種 P&L 口徑
    public decimal RealizedPnl { get; set; }         // 已平倉累積損益
    public decimal UnrealizedPnl { get; set; }        // 持倉浮動損益 (mark-to-market)
    public decimal TotalPnl { get; set; }             // 合計 = Realized + Unrealized
    public decimal TotalEquity { get; set; }          // 持倉市值 + 已實現 P&L（近似 account equity）
    public List<LivePosition> LivePositions { get; set; } = new();

    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPct { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    public List<EquityPoint> EquityCurve { get; set; } = new();
    public List<SymbolStats> PerSymbol { get; set; } = new();
    public List<object> RecentRoundTrips { get; set; } = new();
}

public class LivePosition
{
    public string Symbol { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal AvgEntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MarketValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal UnrealizedPnlPct { get; set; }  // percentage
}
