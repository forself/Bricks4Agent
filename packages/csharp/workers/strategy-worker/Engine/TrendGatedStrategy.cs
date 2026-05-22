using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 趨勢過濾裝飾器(risk-off 大腦的第一塊)—— 套在任何策略外層,**只在上升趨勢裡放行做多**:
///   close > SMA(trend_period) → 放行 inner 的 buy(保留其 TargetPrice/StopPrice/confidence)
///   close ≤ SMA(trend_period) → 把 buy 降級成 hold(跌勢不接刀)
///   sell / hold → 原樣放行(出場不受過濾、平倉永遠允許)
///
/// 為什麼:均值回歸(rsi_stoch 等)的失血來源是「在跌勢/震盪反覆接刀」,而 AutoTrader 的 regime
/// gate 只擋明確 TrendingDown、擋不了這個。價格 vs 長期 MA 是經典且穩健的趨勢過濾。
///
/// trend_period 預設 50:AutoTrader 即時只抓 100 根,SMA50 在 live 算得出來(SMA200 會因 bar 不足而
/// 永遠 hold)。回測(bars 充足)可調更長。trend_period 刻意不放進 ParamSchema(這是 regime 過濾、
/// 不該被 grid search curve-fit;沿用 inner 的 ParamSchema 讓 inner 仍可優化)。
/// </summary>
public class TrendGatedStrategy : IStrategy
{
    private readonly IStrategy _inner;
    private readonly string _name;
    private readonly int _defaultTrendPeriod;

    public TrendGatedStrategy(IStrategy inner, string name, int trendPeriod = 50)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _name = name;
        _defaultTrendPeriod = trendPeriod;
    }

    public string Name => _name;
    public string Description => $"{_inner.Name} + 趨勢過濾(close > SMA{_defaultTrendPeriod} 才放行做多;跌勢不接刀)";
    public StrategyCategory Category => _inner.Category;
    public int MinBars => Math.Max(_inner.MinBars, _defaultTrendPeriod + 1);
    public decimal MinCapitalUsdt => _inner.MinCapitalUsdt;
    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => _inner.ParamSchema;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var sig = _inner.Evaluate(bars, config);
        if (sig.Action != "buy") return sig;   // 出場 / 觀望原樣放行

        var trendPeriod = config.GetParam("trend_period", _defaultTrendPeriod);
        if (bars.Count < trendPeriod)
            return Gated(sig, $"趨勢過濾:bars 不足算 SMA{trendPeriod}");

        decimal sum = 0m;
        for (int i = bars.Count - trendPeriod; i < bars.Count; i++) sum += bars[i].Close;
        var sma = sum / trendPeriod;
        var price = bars[^1].Close;

        if (price <= sma)
            return Gated(sig, $"趨勢過濾擋多:price {price:F2} ≤ SMA{trendPeriod} {sma:F2}(跌勢/震盪、不接刀)");

        // 上升趨勢:原樣放行 inner 的 buy(保留 TargetPrice/StopPrice/confidence/indicators)
        sig.Reason = $"{sig.Reason} · trend-OK(>SMA{trendPeriod} {sma:F2})";
        return sig;
    }

    private Signal Gated(Signal inner, string reason) => new()
    {
        SignalId = inner.SignalId,
        Strategy = _name,
        Symbol = inner.Symbol,
        Exchange = inner.Exchange,
        Action = "hold",
        Confidence = 0,
        Reason = reason,
        Interval = inner.Interval,
        Indicators = inner.Indicators,
    };
}
