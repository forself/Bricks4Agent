using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// Benchmark（買入持有基準）計算服務。
///
/// 用途：為 portfolio dashboard 提供「同一段期間，把 $100k 全押 SPY 會得到的權益曲線」
/// 方便使用者判斷自己策略有沒有 alpha（打贏 passive buy-and-hold）。
///
/// 資料來源：走既有 `quote.prices` capability 的 `get_ohlcv` route 取 K 線，
/// 本服務只做數學運算（不碰資料庫、不碰外部 API）。
/// </summary>
public class BenchmarkService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;

    public BenchmarkService(IExecutionDispatcher dispatcher, IWorkerRegistry registry)
    {
        _dispatcher = dispatcher;
        _registry = registry;
    }

    public async Task<BenchmarkResult?> GetAsync(string symbol, int limit, decimal initialCapital = 100_000m)
    {
        if (!_registry.HasAvailableWorker("quote.ohlcv"))
            return null;

        var payload = JsonSerializer.Serialize(new { symbol, limit });
        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "quote.ohlcv",
            Route = "get_bars",
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "benchmark",
            SessionId = "benchmark",
        };
        var dispatch = await _dispatcher.DispatchAsync(req);
        if (!dispatch.Success || string.IsNullOrWhiteSpace(dispatch.ResultPayload))
            return null;

        using var doc = JsonDocument.Parse(dispatch.ResultPayload);
        var root = doc.RootElement;

        // quote-worker 回的 get_ohlcv 格式：{"bars":[{open_time, open, high, low, close, volume}, ...]}
        JsonElement barsEl;
        if (root.TryGetProperty("bars", out var b1) && b1.ValueKind == JsonValueKind.Array)
            barsEl = b1;
        else if (root.TryGetProperty("data", out var d) && d.TryGetProperty("bars", out var b2))
            barsEl = b2;
        else
            return null;

        var bars = barsEl.EnumerateArray().ToList();
        if (bars.Count < 2) return null;

        var firstClose = TryGetDecimal(bars[0], "close");
        if (firstClose <= 0m) return null;

        var curve = new List<BenchmarkPoint>();
        decimal? lastClose = null;
        foreach (var bar in bars)
        {
            var at = TryGetDate(bar, "open_time");
            var close = TryGetDecimal(bar, "close");
            if (close <= 0m) continue;
            lastClose = close;
            // 以 buy-and-hold 的思路：假裝一開始把 initialCapital 全押 symbol
            // equity[i] = initialCapital * close[i] / close[0]
            // pnl[i]    = equity[i] - initialCapital
            var equity = initialCapital * close / firstClose;
            var pnl = equity - initialCapital;
            var retPct = (close / firstClose - 1m) * 100m;
            curve.Add(new BenchmarkPoint
            {
                At = at,
                Equity = Math.Round(equity, 4),
                Pnl = Math.Round(pnl, 4),
                ReturnPct = Math.Round(retPct, 4),
            });
        }

        if (curve.Count == 0 || lastClose == null) return null;

        return new BenchmarkResult
        {
            Symbol = symbol,
            InitialCapital = initialCapital,
            StartDate = curve.First().At,
            EndDate = curve.Last().At,
            StartPrice = firstClose,
            EndPrice = lastClose.Value,
            TotalReturnPct = Math.Round((lastClose.Value / firstClose - 1m) * 100m, 4),
            Curve = curve,
        };
    }

    private static decimal TryGetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var ds)) return ds;
        return 0m;
    }

    private static DateTime TryGetDate(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return DateTime.MinValue;
        if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
            return dt.ToUniversalTime();
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var ts))
            return DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
        return DateTime.MinValue;
    }
}

public class BenchmarkResult
{
    public string Symbol { get; set; } = "";
    public decimal InitialCapital { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal StartPrice { get; set; }
    public decimal EndPrice { get; set; }
    public decimal TotalReturnPct { get; set; }
    public List<BenchmarkPoint> Curve { get; set; } = new();
}

public class BenchmarkPoint
{
    public DateTime At { get; set; }
    public decimal Equity { get; set; }
    public decimal Pnl { get; set; }
    public decimal ReturnPct { get; set; }
}
