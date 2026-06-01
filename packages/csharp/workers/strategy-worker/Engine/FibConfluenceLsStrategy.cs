using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Fib 匯流(多空)── 使用者完整斐波規則 v1(2026-06-02,Phase 1:全倉進出、無部分平倉)。
/// 對照簡化版 fib_retrace_sl_ls。規則(使用者親定):
///   進場:順勢回撤/反彈到「分級點位」0.382 / 0.5 / 0.618 / 0.707,且【連 2 根收盤站穩該位】才進(防接刀)。
///   目標:= 1 + 回撤深度(0.382→1.382、0.5→1.5、0.618→1.618、0.707→1.707)。
///   止損:進場位的「下一階」fib(升勢取下方、跌勢取上方)。
///   防重複:只在「剛站穩的那根」(fresh 轉折)發訊號,不是每根都發。
/// 升勢:回撤到 level 站穩 → buy、目標在高之上、SL 在 level 下一階。
/// 跌勢:反彈到 level 被拒(2 根收盤壓在 level 下)→ sell、目標在低之下、SL 在 level 上一階。
/// 1.13~1.272 反轉區的「平 50%」屬 Phase 2(需引擎 scale-out),v1 不做(全倉抱到目標/SL)。
/// 0.886 二次進場屬待測 A/B,v1 不做。無 lookahead:全用回看資料。
/// </summary>
public class FibConfluenceLsStrategy : IStrategy
{
    public string Name => "fib_confluence_ls";
    public string Description => "Fib 匯流(多空)— 分級點位+連2根站穩進場、目標=1+深度、SL=下一階(使用者完整規則 v1)";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 70;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback = 60;
    private const decimal MinRangePct = 6m;
    private const int PullWin = 8;            // 找近期回撤/反彈極值的視窗
    private const decimal TouchTol = 0.04m;   // 「碰到該位」的容差(× range)

    // (進場位, 目標=1+位, 升勢SL下一階, 跌勢SL上一階)
    private static readonly (decimal lvl, decimal tgt, decimal slUp, decimal slDn)[] Levels =
    {
        (0.382m, 1.382m, 0.236m, 0.5m),
        (0.5m,   1.5m,   0.382m, 0.618m),
        (0.618m, 1.618m, 0.5m,   0.707m),
        (0.707m, 1.707m, 0.618m, 0.786m),
    };

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["fib_lookback"]      = new() { Type = "int",     Default = Lookback,    Min = 30, Max = 120, Step = 10, Description = "擺動回看根數" },
        ["fib_min_range_pct"] = new() { Type = "decimal", Default = MinRangePct, Min = 3,  Max = 15,  Step = 1,  Description = "最小擺動幅度 %" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int lookback = config.GetParam("fib_lookback", Lookback);
        decimal minRangePct = config.GetParam("fib_min_range_pct", MinRangePct);
        if (bars.Count < lookback + 3) return Hold(config, "資料不足");

        int start = bars.Count - lookback;
        decimal high = decimal.MinValue, low = decimal.MaxValue;
        int hi = start, li = start;
        for (int i = start; i < bars.Count; i++)
        {
            if (bars[i].High > high) { high = bars[i].High; hi = i; }
            if (bars[i].Low  < low)  { low  = bars[i].Low;  li = i; }
        }
        decimal range = high - low;
        if (low <= 0m || range <= 0m || range / low * 100m < minRangePct)
            return Hold(config, $"擺動幅度不足(<{minRangePct}%)");

        bool uptrend = hi > li;
        decimal close = bars[^1].Close;
        decimal prevClose = bars[^2].Close;   // 連 2 根站穩的第 2 根
        decimal pre2Close = bars[^3].Close;   // fresh 判定:站穩前那根

        // 近 PullWin 根的回撤/反彈極值(找價格「探到」哪一階)
        decimal pullLow = decimal.MaxValue, pullHigh = decimal.MinValue;
        int ws = Math.Max(start, bars.Count - PullWin);
        for (int i = ws; i < bars.Count; i++)
        {
            if (bars[i].Low  < pullLow)  pullLow  = bars[i].Low;
            if (bars[i].High > pullHigh) pullHigh = bars[i].High;
        }
        decimal tol = TouchTol * range;

        decimal Price(decimal ratio) => low + ratio * range;

        if (uptrend)
        {
            // 由淺到深(高位先):取「目前 2 根收盤站穩、且近期回撤曾探到該位、且剛轉折」的最高位
            for (int k = Levels.Length - 1; k >= 0; k--)
            {
                var L = Levels[k];
                decimal lp = Price(L.lvl);
                bool held    = close >= lp && prevClose >= lp;       // 連 2 根收在該位之上
                bool touched = pullLow <= lp + tol;                  // 近期確實回撤到該位(或更深)
                bool fresh   = pre2Close < lp;                       // 站穩前那根在該位之下 = 剛站回
                if (held && touched && fresh)
                    return Make(config, "buy", high, low, range, L.lvl, Price(L.tgt), Price(L.slUp), close, true);
            }
            return Hold(config, "升勢:無剛站穩的分級點位");
        }
        else
        {
            // 跌勢鏡像:反彈到 level 被拒(2 根收盤壓在 level 之下)、近期反彈曾觸該位、剛轉折
            for (int k = 0; k < Levels.Length; k++)
            {
                var L = Levels[k];
                decimal lp = Price(L.lvl);
                bool held    = close <= lp && prevClose <= lp;       // 連 2 根收在該位之下
                bool touched = pullHigh >= lp - tol;                 // 近期確實反彈到該位
                bool fresh   = pre2Close > lp;                       // 站穩前那根在該位之上 = 剛壓回
                if (held && touched && fresh)
                {
                    // 跌勢目標在低之下:high − (1+lvl)×range;SL = 上一階
                    decimal tgtDn = high - L.tgt * range;
                    return Make(config, "sell", high, low, range, L.lvl, tgtDn, Price(L.slDn), close, false);
                }
            }
            return Hold(config, "跌勢:無剛被拒的分級點位");
        }
    }

    private Signal Make(StrategyConfig config, string action, decimal high, decimal low, decimal range,
        decimal lvl, decimal tgtPrice, decimal slPrice, decimal close, bool up)
    {
        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = 0.7m, Interval = config.Interval,
            Reason = $"{(up ? "升勢回撤" : "跌勢反彈")}至 fib {lvl:F3} 連2根站穩 → {(up ? "做多" : "做空")}"
                   + $"(SL@{slPrice:F2} TP@{tgtPrice:F2}, 高{high:F2}/低{low:F2})",
            StopPrice = Math.Round(slPrice, 4),
            TargetPrice = Math.Round(tgtPrice, 4),
            Indicators = new()
            {
                ["swing_high"]  = Math.Round(high, 4),
                ["swing_low"]   = Math.Round(low, 4),
                ["entry_level"] = lvl,
                ["sl_price"]    = Math.Round(slPrice, 4),
                ["tp_price"]    = Math.Round(tgtPrice, 4),
                ["price"]       = Math.Round(close, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "fib_confluence_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
