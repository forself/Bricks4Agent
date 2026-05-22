using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 趨勢過濾裝飾器(risk-off 大腦的第一塊)—— 套在任何策略外層,**只在上升趨勢裡放行做多**:
///   SMA(trend_period) 斜率向上(SMA_now > SMA_{slope_lookback 根前})→ 放行 inner 的 buy
///   SMA 斜率向下/持平 → 把 buy 降級成 hold(跌勢不接刀)
///   sell / hold → 原樣放行(出場不受過濾、平倉永遠允許)
///
/// **為什麼用「SMA 斜率」而非「price > SMA」**:rsi_stoch 等買的是「超賣的 dip」,而 dip 的價格常常
/// 正好跌破短 MA → price>SMA 會把「牛市裡會反彈的好 dip」也擋掉(實測淨值反而更差)。改看趨勢「方向」:
/// SMA 上升=多頭格局(dip 照買)、SMA 下降=跌勢(才擋)。這樣留住賺的、只砍跌勢接刀。
///
/// trend_period 預設 50 + slope_lookback 10:AutoTrader 即時抓 100 根夠算。刻意不放進 ParamSchema
/// (regime 過濾不該被 grid search curve-fit;沿用 inner 的 ParamSchema 讓 inner 仍可優化)。
/// </summary>
public class TrendGatedStrategy : IStrategy
{
    private readonly IStrategy _inner;
    private readonly string _name;
    private readonly int _trendPeriod;
    private readonly int _slopeLookback;

    public TrendGatedStrategy(IStrategy inner, string name, int trendPeriod = 50, int slopeLookback = 10)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _name = name;
        _trendPeriod = trendPeriod;
        _slopeLookback = slopeLookback;
    }

    public string Name => _name;
    public string Description => $"{_inner.Name} + 趨勢過濾(SMA{_trendPeriod} 斜率向上才放行做多;跌勢不接刀)";
    public StrategyCategory Category => _inner.Category;
    public int MinBars => Math.Max(_inner.MinBars, _trendPeriod + _slopeLookback + 1);
    public decimal MinCapitalUsdt => _inner.MinCapitalUsdt;
    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => _inner.ParamSchema;

    /// <summary>SMA(period) 以 bars[^(1+offset)] 為最後一根算。offset=0 = 最新。</summary>
    private static decimal Sma(List<BarData> bars, int period, int offset)
    {
        int end = bars.Count - offset;            // exclusive
        decimal sum = 0m;
        for (int i = end - period; i < end; i++) sum += bars[i].Close;
        return sum / period;
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var sig = _inner.Evaluate(bars, config);
        if (sig.Action != "buy") return sig;   // 出場 / 觀望原樣放行

        if (bars.Count < _trendPeriod + _slopeLookback)
            return Gated(sig, $"趨勢過濾:bars 不足算 SMA{_trendPeriod} 斜率");

        var smaNow = Sma(bars, _trendPeriod, 0);
        var smaPast = Sma(bars, _trendPeriod, _slopeLookback);

        if (smaNow <= smaPast)
            return Gated(sig, $"趨勢過濾擋多:SMA{_trendPeriod} 斜率向下/持平({smaPast:F2}→{smaNow:F2})、跌勢不接刀");

        // 多頭格局(SMA 上升):原樣放行 inner 的 buy(保留 TargetPrice/StopPrice/confidence/indicators)
        sig.Reason = $"{sig.Reason} · trend-OK(SMA{_trendPeriod}↑ {smaPast:F2}→{smaNow:F2})";
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
