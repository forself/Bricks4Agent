using System.Text.Json;
using WorkerSdk;
using QuoteWorker.Indicators;
using QuoteWorker.Storage;

namespace QuoteWorker.Handlers;

/// <summary>
/// quote.indicator — 技術指標計算。
///
/// Routes:
///   sma  — Simple Moving Average（參數：symbol, interval, period, limit）
///   ema  — Exponential Moving Average（參數同上）
///   rsi  — Relative Strength Index（參數同上）
///   macd — MACD（參數：symbol, interval, fast, slow, signal, limit）
/// </summary>
public class QuoteIndicatorHandler : ICapabilityHandler
{
    private readonly QuoteDbStorage _db;
    public string CapabilityId => "quote.indicator";

    public QuoteIndicatorHandler(QuoteDbStorage db) => _db = db;

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        var opts = string.IsNullOrWhiteSpace(payload)
            ? new JsonElement()
            : JsonDocument.Parse(payload).RootElement;

        var result = route switch
        {
            "sma"       => CalcSma(opts),
            "ema"       => CalcEma(opts),
            "rsi"       => CalcRsi(opts),
            "macd"      => CalcMacd(opts),
            "bbands"    => CalcBBands(opts),
            "atr"       => CalcAtr(opts),
            "stochastic" => CalcStochastic(opts),
            "obv"       => CalcObv(opts),
            _ => (false, (string?)null, $"Unknown route: {route}")
        };

        return Task.FromResult(result);
    }

    private (bool, string?, string?) CalcSma(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var period = opts.TryGetProperty("period", out var p) ? p.GetInt32() : 20;

        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < period)
            return (false, null, $"Not enough data: {bars.Count} bars, need at least {period}");

        var result = TechnicalIndicators.SMA(bars, period);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcEma(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var period = opts.TryGetProperty("period", out var p) ? p.GetInt32() : 20;

        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < period)
            return (false, null, $"Not enough data: {bars.Count} bars, need at least {period}");

        var result = TechnicalIndicators.EMA(bars, period);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcRsi(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var period = opts.TryGetProperty("period", out var p) ? p.GetInt32() : 14;

        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < period + 1)
            return (false, null, $"Not enough data: {bars.Count} bars, need at least {period + 1}");

        var result = TechnicalIndicators.RSI(bars, period);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcMacd(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var fast   = opts.TryGetProperty("fast",   out var f) ? f.GetInt32() : 12;
        var slow   = opts.TryGetProperty("slow",   out var s) ? s.GetInt32() : 26;
        var signal = opts.TryGetProperty("signal", out var sg) ? sg.GetInt32() : 9;

        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < slow + signal)
            return (false, null, $"Not enough data: {bars.Count} bars, need at least {slow + signal}");

        var result = TechnicalIndicators.MACD(bars, fast, slow, signal);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcBBands(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var period = opts.TryGetProperty("period", out var p) ? p.GetInt32() : 20;
        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < period) return (false, null, $"Not enough data: {bars.Count} bars, need {period}");
        var result = TechnicalIndicators.BollingerBands(bars, period);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcAtr(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var period = opts.TryGetProperty("period", out var p) ? p.GetInt32() : 14;
        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < period + 1) return (false, null, $"Not enough data: {bars.Count} bars, need {period + 1}");
        var result = TechnicalIndicators.ATR(bars, period);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcStochastic(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var kPeriod = opts.TryGetProperty("k_period", out var kp) ? kp.GetInt32() : 14;
        var dPeriod = opts.TryGetProperty("d_period", out var dp) ? dp.GetInt32() : 3;
        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < kPeriod + dPeriod) return (false, null, $"Not enough data");
        var result = TechnicalIndicators.Stochastic(bars, kPeriod, dPeriod);
        return (true, SerializeResult(result), null);
    }

    private (bool, string?, string?) CalcObv(JsonElement opts)
    {
        var (symbol, interval, limit, err) = ParseCommon(opts);
        if (err != null) return (false, null, err);
        var bars = _db.GetBars(symbol!, interval, limit);
        if (bars.Count < 2) return (false, null, "Not enough data");
        var result = TechnicalIndicators.OBV(bars);
        return (true, SerializeResult(result), null);
    }

    private static (string? symbol, string interval, int limit, string? error) ParseCommon(JsonElement opts)
    {
        var symbol   = opts.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
        var interval = opts.TryGetProperty("interval", out var i) ? i.GetString() ?? "1d" : "1d";
        var limit    = opts.TryGetProperty("limit",    out var l) ? l.GetInt32() : 500;

        if (string.IsNullOrEmpty(symbol))
            return (null, interval, limit, "Missing required parameter: symbol");

        return (symbol, interval, limit, null);
    }

    private static string SerializeResult(Models.IndicatorResult r) =>
        JsonSerializer.Serialize(new
        {
            symbol    = r.Symbol,
            indicator = r.Indicator,
            interval  = r.Interval,
            period    = r.Period,
            timestamp = r.Timestamp,
            value     = r.Value,
            signal    = r.Signal,
            histogram = r.Histogram,
            series_count = r.Series.Count,
            series    = r.Series.TakeLast(50).Select(sv => new { time = sv.Time, value = sv.Value }),
        });
}
