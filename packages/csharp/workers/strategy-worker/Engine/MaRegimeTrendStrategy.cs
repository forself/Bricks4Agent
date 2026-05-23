using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 均線斜率 Regime 趨勢(MA Slope Regime)—— 最樸素也最耐操的趨勢過濾:
/// 只在「價格站上中長均線、且均線本身在上彎(斜率為正)」時做多,均線轉平/下彎或跌破就空手。
/// 跟 ts_momentum(看 ROC 報酬幅度 + 波動率閘門)、chandelier_trend(突破 + 吊燈停)機制不同:
/// 它不看報酬大小、不看突破,只看「趨勢結構在不在」,因此在緩漲行情留得住、在頂部轉折早一步離場。
///
///   ma     = SMA(maPeriod)
///   maPrev = slopeLookback 根前的 SMA(maPeriod)
///   做多: close > ma 且 ma > maPrev(均線上彎)
///   出場: close < ma(跌破中軌、regime 結束)
///
/// 多空對稱:站上上彎均線 buy、跌破下彎均線 sell(做空)、其餘 hold。無 lookahead:SMA 與其過去值皆回看。
/// </summary>
public class MaRegimeTrendStrategy : IStrategy
{
    public string Name => "ma_regime_trend";
    public string Description => "MA Slope Regime — 站上中長均線且均線上彎才做多、跌破即空手";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 70;
    public decimal MinCapitalUsdt => 100m;

    private const int MaPeriod      = 50;
    private const int SlopeLookback = 10;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["mar_ma_period"]      = new() { Type = "int", Default = MaPeriod,      Min = 20, Max = 150, Step = 10, Description = "趨勢均線週期" },
        ["mar_slope_lookback"] = new() { Type = "int", Default = SlopeLookback, Min = 3,  Max = 30,  Step = 1,  Description = "斜率比較間隔(根)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int maPeriod = config.GetParam("mar_ma_period", MaPeriod);
        int slopeLb  = config.GetParam("mar_slope_lookback", SlopeLookback);

        int need = maPeriod + slopeLb + 1;
        if (bars.Count < need) return Hold(config, $"資料不足(需 {need}+ 根)");

        decimal close = bars[^1].Close;
        decimal ma     = Sma(bars, maPeriod, 0);
        decimal maPrev = Sma(bars, maPeriod, slopeLb);
        bool slopeUp = ma > maPrev;
        bool aboveMa = close > ma;

        // 對稱多空:站上上彎均線做多、跌破下彎均線做空、其餘維持。
        string action; decimal confidence; string reason;
        if (aboveMa && slopeUp)
        {
            action = "buy";
            decimal dist = ma > 0 ? (close - ma) / ma : 0m;
            confidence = Math.Clamp(0.6m + dist * 3m, 0.6m, 0.9m);
            reason = $"close {close:F2} > SMA{maPeriod} {ma:F2} 且均線上彎(Δ{slopeLb}根 {ma - maPrev:F2}) — 做多";
        }
        else if (!aboveMa && !slopeUp)
        {
            action = "sell";
            decimal dist = ma > 0 ? (ma - close) / ma : 0m;
            confidence = Math.Clamp(0.6m + dist * 3m, 0.6m, 0.9m);
            reason = $"close {close:F2} < SMA{maPeriod} {ma:F2} 且均線下彎(Δ{slopeLb}根 {ma - maPrev:F2}) — 做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"close/均線方向不一致(close-ma {close - ma:F2}, slope {ma - maPrev:F2}) — 等趨勢確認、維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["ma"]       = Math.Round(ma, 4),
                ["ma_prev"]  = Math.Round(maPrev, 4),
                ["ma_slope"] = Math.Round(ma - maPrev, 4),
                ["price"]    = Math.Round(close, 4),
            },
        };
    }

    /// <summary>SMA(period),結束點往前推 offset 根(offset=0 = 當根);只用回看資料。</summary>
    private static decimal Sma(List<BarData> bars, int period, int offset)
    {
        int endExclusive = bars.Count - offset;     // [endExclusive-period, endExclusive)
        decimal sum = 0m;
        for (int i = endExclusive - period; i < endExclusive; i++) sum += bars[i].Close;
        return sum / period;
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "ma_regime_trend", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
