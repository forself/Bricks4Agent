using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// ADX + DI 趨勢方向 + 強度策略。
///
/// 訊號規則：
///   ADX > 25 + |+DI - -DI| ≥ 5：
///     +DI > -DI → buy（強趨勢多頭）
///     +DI < -DI → sell（強趨勢空頭）
///   ADX < 20 → hold（震盪、不適合趨勢策略）
///   20 ≤ ADX ≤ 25：中性區、需 |+DI - -DI| ≥ 10 才動作（要明顯方向）
///   |+DI - -DI| < 3 → hold（DI 接近交叉、可能轉折、不追）
///
/// 信心度：
///   ADX 越大、DI 差距越大 → confidence 越高
///   conf = clamp(0.5 + (ADX-25)/50 + |+DI - -DI|/50, 0.5, 0.95)
///
/// 設計對標：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_adx_di。
/// </summary>
public class AdxDiStrategy : IStrategy
{
    public string Name => "adx_di";
    public string Description => "ADX + DI — 趨勢強度（ADX）× 方向（+DI/-DI）的雙重確認";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 30;
    public decimal MinCapitalUsdt => 100m;

    private const int Period = 14;
    private const decimal StrongAdx = 25m;
    private const decimal WeakAdx   = 20m;
    private const decimal MinDiGap  = 3m;
    private const decimal StrongDiGap = 5m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["period"] = new() { Type = "int", Default = 14, Min = 7, Max = 30, Step = 1, Description = "ADX/DI 週期" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var r = AdxDi.Compute(bars, Period);
        if (r == null) return Hold(config, "Not enough data for ADX/DI");

        var diGap = Math.Abs(r.PlusDi - r.MinusDi);
        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (r.Adx < WeakAdx)
        {
            reason = $"ADX={r.Adx:F1} 弱趨勢（<{WeakAdx}）— 震盪、不交易";
        }
        else if (diGap < MinDiGap)
        {
            reason = $"+DI={r.PlusDi:F1} -DI={r.MinusDi:F1} 差距 {diGap:F1} < {MinDiGap} — DI 接近交叉、觀望";
        }
        else if (r.Adx >= StrongAdx && diGap >= StrongDiGap)
        {
            if (r.PlusDi > r.MinusDi)
            {
                action = "buy";
                confidence = ScoreConfidence(r.Adx, diGap);
                reason = $"ADX={r.Adx:F1} 強趨勢 + +DI({r.PlusDi:F1}) > -DI({r.MinusDi:F1}) → 多頭";
            }
            else
            {
                action = "sell";
                confidence = ScoreConfidence(r.Adx, diGap);
                reason = $"ADX={r.Adx:F1} 強趨勢 + -DI({r.MinusDi:F1}) > +DI({r.PlusDi:F1}) → 空頭";
            }
        }
        else
        {
            // 20 ≤ ADX < 25 中性區、需要更大 DI gap（10+）才確認
            if (diGap >= 10m)
            {
                action = r.PlusDi > r.MinusDi ? "buy" : "sell";
                confidence = 0.55m;
                reason = $"ADX={r.Adx:F1} 中性區但 DI 差距大（{diGap:F1}） → 小信心 {action}";
            }
            else
            {
                reason = $"ADX={r.Adx:F1} 中性區、DI 差距 {diGap:F1} 不夠 — 觀望";
            }
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                ["adx"]      = r.Adx,
                ["plus_di"]  = r.PlusDi,
                ["minus_di"] = r.MinusDi,
                ["di_gap"]   = Math.Round(diGap, 4),
            },
        };
    }

    private static decimal ScoreConfidence(decimal adx, decimal diGap)
    {
        var c = 0.5m + (adx - StrongAdx) / 50m + diGap / 50m;
        return Math.Clamp(c, 0.5m, 0.95m);
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "adx_di",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
