using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// Kelly Criterion 動態倉位計算（建議層，不執行）。
///
/// 公式：
///   f* = (bp - q) / b
/// 其中：
///   b = 平均獲利 / 平均虧損（odds ratio）
///   p = 勝率
///   q = 1 - p
///
/// f* 是「應該押多少比例的資金」。例如 f* = 0.2 代表每次下注本金的 20%。
///
/// 實務上通常用 **Fractional Kelly**（½-Kelly 或 ¼-Kelly）以避免過大波動：
///   使用者可指定 fraction ∈ (0, 1]，最後建議倉位 = f* × fraction × capital。
///
/// 這個服務**不整合進 AutoTraderService**——它是獨立的建議層，
/// 使用者問：「這個策略給我多少資金我該下多少？」，由外部呼叫決定用不用。
/// 未來若要接 Auto-Trader 再另外做 wrapper，不破壞既有邏輯。
/// </summary>
public class KellyPositionSizingService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;

    public KellyPositionSizingService(IExecutionDispatcher dispatcher, IWorkerRegistry registry)
    {
        _dispatcher = dispatcher;
        _registry = registry;
    }

    /// <summary>
    /// 對某 strategy × symbol 跑回測取得歷史勝率與盈虧比，再算 Kelly 建議倉位。
    /// </summary>
    public async Task<KellySuggestion> SuggestAsync(
        string strategy, string symbol, decimal capital,
        decimal fraction = 0.5m, int dataLimit = 300, CancellationToken ct = default)
    {
        if (!_registry.HasAvailableWorker("quote.ohlcv"))
            return KellySuggestion.Fail("quote-worker not connected");
        if (!_registry.HasAvailableWorker("strategy.signal"))
            return KellySuggestion.Fail("strategy-worker not connected");

        fraction = Math.Clamp(fraction, 0.1m, 1m);
        capital = Math.Max(0m, capital);

        // 1. 抓 K 線
        var barsPayload = await FetchBarsAsync(symbol, dataLimit, ct);
        if (string.IsNullOrEmpty(barsPayload))
            return KellySuggestion.Fail($"No bars for {symbol}");

        using var barsDoc = JsonDocument.Parse(barsPayload);
        var barsEl = barsDoc.RootElement.GetProperty("bars");

        // 2. 跑 backtest
        var btPayload = JsonSerializer.Serialize(new
        {
            strategy,
            symbol,
            bars = JsonSerializer.Deserialize<JsonElement>(barsEl.GetRawText()),
            initial_cash = 100_000m,
        });
        var btResult = await _dispatcher.DispatchAsync(BuildReq("strategy.signal", "backtest", btPayload));
        if (!btResult.Success || string.IsNullOrEmpty(btResult.ResultPayload))
            return KellySuggestion.Fail($"Backtest failed: {btResult.ErrorMessage}");

        // 3. 解析勝率、avg_win、avg_loss
        using var btDoc = JsonDocument.Parse(btResult.ResultPayload);
        var r = btDoc.RootElement;
        // backtest 回傳的 win_rate 是百分比（例如 60.0）不是小數，要轉成 0-1
        var winRateRaw = GetDec(r, "win_rate");
        var winRate = winRateRaw > 1m ? winRateRaw / 100m : winRateRaw;
        var avgWin = Math.Abs(GetDec(r, "avg_win"));
        var avgLoss = Math.Abs(GetDec(r, "avg_loss"));
        var totalTrades = GetInt(r, "total_trades");

        if (totalTrades < 5)
            return KellySuggestion.Fail($"Not enough historical trades ({totalTrades}) for reliable Kelly — need ≥ 5");

        if (avgLoss <= 0m)
            return KellySuggestion.Fail("Strategy has no losing trades historically — Kelly undefined (would suggest 100% leverage)");

        // 4. Kelly 公式
        var b = avgWin / avgLoss;          // odds
        var p = winRate;
        var q = 1m - p;
        var fStar = (b * p - q) / b;        // raw Kelly fraction

        // 5. 保護措施
        //    - 負 Kelly = 該策略期望值為負 → 不建議下注
        //    - Fractional Kelly = 真實下注用一半以降低波動
        //    - 總 cap = 25%（避免過度集中）
        var effectiveFraction = Math.Max(0m, fStar) * fraction;
        effectiveFraction = Math.Min(effectiveFraction, 0.25m);
        var suggestedCapital = Math.Round(capital * effectiveFraction, 2);

        return new KellySuggestion
        {
            Success = true,
            Strategy = strategy,
            Symbol = symbol,
            WinRate = Math.Round(p, 4),
            AvgWin = Math.Round(avgWin, 4),
            AvgLoss = Math.Round(avgLoss, 4),
            OddsRatio = Math.Round(b, 4),
            TotalHistoricalTrades = totalTrades,
            RawKellyFraction = Math.Round(fStar, 4),
            AppliedFraction = Math.Round(fraction, 4),
            EffectiveFraction = Math.Round(effectiveFraction, 4),
            CapitalInput = capital,
            SuggestedCapital = suggestedCapital,
            Interpretation = fStar <= 0m
                ? "⚠️ 此策略的歷史期望值為負或邊緣，Kelly 建議不進場（fraction=0）"
                : fStar > 0.5m
                    ? $"⚠️ Raw Kelly 超過 50%（{fStar:P1}）表示樣本可能過度樂觀，已套用 Fractional Kelly + 25% 上限"
                    : $"✅ 建議用本金的 {effectiveFraction:P1}（fractional={fraction:P0} × raw={fStar:P1}）",
        };
    }

    private async Task<string?> FetchBarsAsync(string symbol, int limit, CancellationToken ct)
    {
        var p = JsonSerializer.Serialize(new { symbol, limit });
        var r = await _dispatcher.DispatchAsync(BuildReq("quote.ohlcv", "get_bars", p));
        return r.Success ? r.ResultPayload : null;
    }

    private static ApprovedRequest BuildReq(string cap, string route, string payload = "{}")
        => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = cap,
            Route = route,
            Payload = payload,
            Scope = "{}",
            PrincipalId = "system",
            TaskId = "kelly",
            SessionId = "kelly",
        };

    private static decimal GetDec(JsonElement e, string n)
    {
        if (!e.TryGetProperty(n, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        return 0m;
    }

    private static int GetInt(JsonElement e, string n)
    {
        if (!e.TryGetProperty(n, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        return 0;
    }
}

public class KellySuggestion
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string Strategy { get; set; } = "";
    public string Symbol { get; set; } = "";
    public decimal WinRate { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public decimal OddsRatio { get; set; }
    public int TotalHistoricalTrades { get; set; }
    public decimal RawKellyFraction { get; set; }
    public decimal AppliedFraction { get; set; }
    public decimal EffectiveFraction { get; set; }
    public decimal CapitalInput { get; set; }
    public decimal SuggestedCapital { get; set; }
    public string Interpretation { get; set; } = "";

    public static KellySuggestion Fail(string e) => new() { Success = false, Error = e };
}
