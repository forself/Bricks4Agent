using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 動量加速度(Momentum Acceleration / ROC 二階變化)—— 看的不是「動量水平」而是「動量在加速還減速」。
/// ts_momentum 用 ROC 的「正負(水平)」做中期趨勢;這支用 ROC 的「變化(一階差)」抓「趨勢點火」:
/// 動量翻正且還在變強時進場、開始減速就先撤,進出比水平型更早 → 兩者持有時點錯開、來源不同。
///
///   rocNow  = ROC(period)               (當根)
///   rocPrev = ROC(period) 於 gap 根前    (= 對截掉最後 gap 根的序列算 ROC)
///   accel   = rocNow − rocPrev
///   rocNow > 0 且 accel > 0  → buy(動量正且加速)
///   accel < 0                → sell(動量減速、撤)
///
/// 無 lookahead:兩個 ROC 都用已測過的 Roc 指標、對「回看切片」計算,不碰未來資料。
/// </summary>
public class AccelMomentumStrategy : IStrategy
{
    public string Name => "accel_momentum";
    public string Description => "Momentum Acceleration — ROC 二階變化多空對稱(正加速做多/負加速做空)";
    public StrategyCategory Category => StrategyCategory.Momentum;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 100m;

    private const int RocPeriod = 14;
    private const int AccelGap  = 5;     // 跟幾根前的 ROC 比較
    private const int TrendSma  = 50;    // 趨勢過濾:只在 close>SMA(此值) 才點火做多

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["accel_roc_period"] = new() { Type = "int", Default = RocPeriod, Min = 7,  Max = 30, Step = 1,  Description = "ROC 週期" },
        ["accel_gap"]        = new() { Type = "int", Default = AccelGap,  Min = 2,  Max = 14, Step = 1,  Description = "加速度比較間隔(根)" },
        ["accel_trend_sma"]  = new() { Type = "int", Default = TrendSma,  Min = 20, Max = 100, Step = 10, Description = "趨勢過濾均線(只在其上點火)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int rocPeriod = config.GetParam("accel_roc_period", RocPeriod);
        int gap       = config.GetParam("accel_gap", AccelGap);
        int trendSma  = config.GetParam("accel_trend_sma", TrendSma);

        int need = Math.Max(rocPeriod + gap + 1, trendSma + 1);
        if (bars.Count < need) return Hold(config, $"資料不足(需 {need}+ 根)");

        var rocNow = Roc.Compute(bars, rocPeriod);
        // gap 根前的 ROC = 對「切掉最後 gap 根」的序列算 ROC(純回看、無 lookahead)
        var rocPrev = Roc.Compute(bars.Take(bars.Count - gap).ToList(), rocPeriod);
        if (rocNow == null || rocPrev == null) return Hold(config, "ROC 無法計算");

        decimal accel = rocNow.Value - rocPrev.Value;
        decimal sma = Sma(bars, trendSma);
        decimal close = bars[^1].Close;
        bool uptrend = close > sma;
        bool downtrend = close < sma;

        // 對稱多空:動量正且加速且多頭 → 做多;動量負且加速向下且空頭 → 做空;其餘維持。
        string action; decimal confidence; string reason;
        if (rocNow.Value > 0m && accel > 0m && uptrend)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + accel / 50m, 0.6m, 0.95m);
            reason = $"ROC{rocPeriod}={rocNow:F1}%>0、加速 +{accel:F1}、close>SMA{trendSma} {sma:F2} — 順勢點火做多";
        }
        else if (rocNow.Value < 0m && accel < 0m && downtrend)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (-accel) / 50m, 0.6m, 0.95m);
            reason = $"ROC{rocPeriod}={rocNow:F1}%<0、向下加速 {accel:F1}、close<SMA{trendSma} {sma:F2} — 順勢做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"ROC{rocPeriod}={rocNow:F1}%、加速 {accel:F1}、close-SMA{trendSma} {close - sma:F2} — 中性、維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["roc_now"]  = rocNow.Value,
                ["roc_prev"] = rocPrev.Value,
                ["accel"]    = Math.Round(accel, 4),
                ["price"]    = Math.Round(bars[^1].Close, 4),
            },
        };
    }

    private static decimal Sma(List<BarData> bars, int period)
    {
        decimal sum = 0m;
        for (int i = bars.Count - period; i < bars.Count; i++) sum += bars[i].Close;
        return sum / period;
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "accel_momentum", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
