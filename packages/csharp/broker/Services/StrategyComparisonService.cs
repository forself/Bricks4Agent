using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// Strategy Comparison — 同一個 symbol 對所有可用策略跑 backtest，
/// 並排比較 Sharpe / MaxDD / Return / Win rate。
///
/// 架構位置：broker 側的 orchestration layer，
/// 不動 strategy-worker、不動 quote-worker。
/// 所有重活都透過既有的 IExecutionDispatcher 分派出去：
///
///   1. quote.ohlcv/get_bars   ←── 只抓一次 K 線
///   2. strategy.signal/list   ←── 查有哪些策略
///   3. strategy.signal/backtest  ←── 每個策略並行跑一次
///
/// 最後整理成排名 + 冠軍榜 + 每個策略的權益曲線。
/// </summary>
public class StrategyComparisonService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;

    public StrategyComparisonService(IExecutionDispatcher dispatcher, IWorkerRegistry registry)
    {
        _dispatcher = dispatcher;
        _registry = registry;
    }

    public async Task<ComparisonResult?> CompareAsync(
        string symbol,
        int limit = 300,
        decimal initialCash = 100_000m,
        List<string>? strategyFilter = null)
    {
        if (!_registry.HasAvailableWorker("quote.ohlcv"))
            return new ComparisonResult { Error = "quote-worker not connected" };
        if (!_registry.HasAvailableWorker("strategy.signal"))
            return new ComparisonResult { Error = "strategy-worker not connected" };

        // 1. 抓 K 線
        var barsJson = await FetchBarsAsync(symbol, limit);
        if (string.IsNullOrEmpty(barsJson))
            return new ComparisonResult { Error = $"No bars for {symbol}" };

        var barsDoc = JsonDocument.Parse(barsJson);
        var barsArray = barsDoc.RootElement.GetProperty("bars");
        var barCount = barsArray.GetArrayLength();
        if (barCount < 30)
            return new ComparisonResult { Error = $"Too few bars: {barCount} (need ≥ 30)" };

        var firstBarDate = ParseDate(barsArray[0], "open_time");
        var lastBarDate = ParseDate(barsArray[barCount - 1], "open_time");

        // 2. 列出策略。預設排除需要外部 LLM API 的策略（避免 timeout 污染對照）
        var strategies = await ListStrategiesAsync();
        var excludeByDefault = new HashSet<string> { "llm", "news_sentiment" };
        if (strategyFilter != null && strategyFilter.Count > 0)
            strategies = strategies.Where(s => strategyFilter.Contains(s)).ToList();
        else
            strategies = strategies.Where(s => !excludeByDefault.Contains(s)).ToList();

        // 3. 循序跑每個策略的 backtest（並行會搶 dispatcher；這裡量小 seq 就夠快）
        var entries = new List<StrategyEntry>();
        foreach (var s in strategies)
        {
            var entry = await RunBacktestAsync(s, symbol, barsJson, initialCash);
            if (entry != null) entries.Add(entry);
        }

        // 4. 排名 + 冠軍
        var ranked = entries.OrderByDescending(e => e.SharpeRatio).ToList();
        for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;

        var winners = new Dictionary<string, string>();
        if (entries.Count > 0)
        {
            winners["sharpe"]   = entries.OrderByDescending(e => e.SharpeRatio).First().Strategy;
            winners["return"]   = entries.OrderByDescending(e => e.TotalReturnPct).First().Strategy;
            winners["drawdown"] = entries.OrderBy(e => e.MaxDrawdownPct).First().Strategy;
            winners["winrate"]  = entries.OrderByDescending(e => e.WinRate).First().Strategy;
            winners["profit_factor"] = entries.OrderByDescending(e => e.ProfitFactor).First().Strategy;
        }

        return new ComparisonResult
        {
            Symbol = symbol,
            TotalBars = barCount,
            StartDate = firstBarDate,
            EndDate = lastBarDate,
            InitialCash = initialCash,
            Ranked = ranked,
            WinnerBy = winners,
        };
    }

    // ── 底層 dispatcher 包裝 ──────────────────────────────────────────

    private async Task<string?> FetchBarsAsync(string symbol, int limit)
    {
        var payload = JsonSerializer.Serialize(new { symbol, limit });
        var r = await _dispatcher.DispatchAsync(BuildReq("quote.ohlcv", "get_bars", payload));
        return r.Success ? r.ResultPayload : null;
    }

    private async Task<List<string>> ListStrategiesAsync()
    {
        var r = await _dispatcher.DispatchAsync(BuildReq("strategy.signal", "list"));
        if (!r.Success || string.IsNullOrEmpty(r.ResultPayload)) return new();
        try
        {
            using var doc = JsonDocument.Parse(r.ResultPayload);
            var root = doc.RootElement;
            JsonElement listEl;
            if (root.TryGetProperty("strategies", out var l1)) listEl = l1;
            else if (root.TryGetProperty("data", out var d) && d.TryGetProperty("strategies", out var l2)) listEl = l2;
            else return new();
            return listEl.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .ToList();
        }
        catch { return new(); }
    }

    private async Task<StrategyEntry?> RunBacktestAsync(string strategy, string symbol, string barsPayload, decimal initialCash)
    {
        // barsPayload 是 {"bars":[...]} — 我要組成 backtest 需要的 payload
        try
        {
            using var barsDoc = JsonDocument.Parse(barsPayload);
            var barsArr = barsDoc.RootElement.GetProperty("bars");
            var payload = JsonSerializer.Serialize(new
            {
                strategy,
                symbol,
                bars = JsonSerializer.Deserialize<JsonElement>(barsArr.GetRawText()),
                initial_cash = initialCash,
            });
            var r = await _dispatcher.DispatchAsync(BuildReq("strategy.signal", "backtest", payload));
            if (!r.Success || string.IsNullOrEmpty(r.ResultPayload)) return null;
            return ParseBacktest(strategy, r.ResultPayload);
        }
        catch { return null; }
    }

    private StrategyEntry? ParseBacktest(string strategy, string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var r = doc.RootElement;
            var entry = new StrategyEntry
            {
                Strategy = strategy,
                TotalReturnPct = GetDec(r, "total_return_pct"),
                SharpeRatio = GetDec(r, "sharpe_ratio"),
                MaxDrawdownPct = GetDec(r, "max_drawdown_pct"),
                WinRate = GetDec(r, "win_rate"),
                TotalTrades = GetInt(r, "total_trades"),
                WinTrades = GetInt(r, "win_trades"),
                LoseTrades = GetInt(r, "lose_trades"),
                ProfitFactor = GetDec(r, "profit_factor"),
                FinalValue = GetDec(r, "final_value"),
            };
            if (r.TryGetProperty("equity_curve", out var ec) && ec.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in ec.EnumerateArray())
                {
                    entry.EquityCurve.Add(new BacktestEquityPoint
                    {
                        At = ParseDate(p, "date"),
                        Value = GetDec(p, "value"),
                    });
                }
            }
            return entry;
        }
        catch { return null; }
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

    private static ApprovedRequest BuildReq(string cap, string route, string payload = "{}")
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = cap,
            Route = route,
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "strategy-lab",
            SessionId = "strategy-lab",
        };

    private static decimal GetDec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds)) return ds;
        return 0m;
    }

    private static int GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        return 0;
    }

    private static DateTime ParseDate(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return DateTime.MinValue;
        if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
            return dt.ToUniversalTime();
        return DateTime.MinValue;
    }
}

// ── DTO ─────────────────────────────────────────────────────────────

public class ComparisonResult
{
    public string Symbol { get; set; } = "";
    public int TotalBars { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal InitialCash { get; set; }
    public List<StrategyEntry> Ranked { get; set; } = new();
    public Dictionary<string, string> WinnerBy { get; set; } = new();
    public string? Error { get; set; }
}

public class StrategyEntry
{
    public int Rank { get; set; }
    public string Strategy { get; set; } = "";
    public decimal TotalReturnPct { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdownPct { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public int WinTrades { get; set; }
    public int LoseTrades { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal FinalValue { get; set; }
    public List<BacktestEquityPoint> EquityCurve { get; set; } = new();
}

public class BacktestEquityPoint
{
    public DateTime At { get; set; }
    public decimal Value { get; set; }
}
