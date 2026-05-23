using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Dual Thrust 區間突破 —— 經典 CTA/期貨日內框架的日線版。用「前 N 根的區間幅度」
/// 設一條買進線、一條賣出線:價格站上買線才追多、跌破賣線才出場。
///
///   Range    = max(HH − LC, HC − LL)   (前 N 根:最高高 / 最低收 / 最高收 / 最低低)
///   BuyLine  = 今日開盤 + K1 × Range
///   SellLine = 今日開盤 − K2 × Range
///
/// 跟 donchian(純前高/前低通道)、volatility_breakout(先擠壓才追)的差異:
/// 觸發是「今日開盤 + 不對稱係數 × 前 N 根區間」,對漲跌可給不同靈敏度,訊號出現在
/// 「當根已實現足夠衝量」時 → 進場時點跟通道型/趨勢型錯開、是另一種去相關來源。
///
/// 多空對稱:順勢上破 buy(做多)、順勢下破 sell(做空)、逆勢假突破/區間內 hold。
/// 無 lookahead:今日開盤、收盤都是「已完成當根」的歷史值;Range 只取前 N 根(不含當根)。
/// </summary>
public class DualThrustStrategy : IStrategy
{
    public string Name => "dual_thrust";
    public string Description => "Dual Thrust — 前 N 根區間幅度設買賣線的不對稱突破";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 30;
    public decimal MinCapitalUsdt => 120m;

    private const int RangeLookback = 20;
    private const int TrendSma = 50;   // 趨勢過濾:只在 close>SMA(此值) 才追上破
    private const decimal K1 = 0.5m;   // 上破係數
    private const decimal K2 = 0.5m;   // 下破係數

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["dt_lookback"]  = new() { Type = "int",     Default = RangeLookback, Min = 5,    Max = 40,   Step = 5,   Description = "區間回看根數" },
        ["dt_trend_sma"] = new() { Type = "int",     Default = TrendSma,      Min = 20,   Max = 100,  Step = 10,  Description = "趨勢過濾均線(只在其上追多)" },
        ["dt_k1"]        = new() { Type = "decimal", Default = K1,            Min = 0.2m, Max = 1.0m, Step = 0.1m, Description = "上破(買)係數" },
        ["dt_k2"]        = new() { Type = "decimal", Default = K2,            Min = 0.2m, Max = 1.0m, Step = 0.1m, Description = "下破(賣)係數" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int n = config.GetParam("dt_lookback", RangeLookback);
        int trendSma = config.GetParam("dt_trend_sma", TrendSma);
        decimal k1 = config.GetParam("dt_k1", K1);
        decimal k2 = config.GetParam("dt_k2", K2);

        if (bars.Count < Math.Max(n + 2, trendSma + 1)) return Hold(config, $"資料不足(需 {Math.Max(n + 2, trendSma + 1)}+ 根)");

        // 前 N 根(不含當根)的 HH / LL / HC / LC
        int end = bars.Count - 1;     // 當根 index
        int start = end - n;          // 前 N 根起點
        decimal hh = decimal.MinValue, ll = decimal.MaxValue, hc = decimal.MinValue, lc = decimal.MaxValue;
        for (int i = start; i < end; i++)
        {
            if (bars[i].High  > hh) hh = bars[i].High;
            if (bars[i].Low   < ll) ll = bars[i].Low;
            if (bars[i].Close > hc) hc = bars[i].Close;
            if (bars[i].Close < lc) lc = bars[i].Close;
        }
        decimal range = Math.Max(hh - lc, hc - ll);
        if (range <= 0m) return Hold(config, "前 N 根區間幅度為 0");

        decimal open  = bars[end].Open;
        decimal close = bars[end].Close;
        decimal buyLine  = open + k1 * range;
        decimal sellLine = open - k2 * range;
        decimal sma = Sma(bars, trendSma);
        bool uptrend = close > sma;
        bool downtrend = close < sma;

        // 對稱多空:順勢上破做多、順勢下破做空、逆勢假突破或區間內維持。
        string action; decimal confidence; string reason;
        if (close > buyLine && uptrend)
        {
            action = "buy";
            decimal over = (close - buyLine) / range;     // 突破深度(以區間為單位)
            confidence = Math.Clamp(0.6m + over, 0.6m, 0.95m);
            reason = $"收 {close:F2} > 買線 {buyLine:F2}(開 {open:F2}+{k1}×{range:F2})且 >SMA{trendSma} {sma:F2} — 順勢做多";
        }
        else if (close < sellLine && downtrend)
        {
            action = "sell";
            decimal over = (sellLine - close) / range;
            confidence = Math.Clamp(0.6m + over, 0.6m, 0.95m);
            reason = $"收 {close:F2} < 賣線 {sellLine:F2}(開 {open:F2}−{k2}×{range:F2})且 <SMA{trendSma} {sma:F2} — 順勢做空";
        }
        else if (close > buyLine || close < sellLine)
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"突破但與 SMA{trendSma} {sma:F2} 趨勢不一致(假突破)— 不追、維持";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"收 {close:F2} 在 [{sellLine:F2}, {buyLine:F2}] 內 — 等突破";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["buy_line"]  = Math.Round(buyLine, 4),
                ["sell_line"] = Math.Round(sellLine, 4),
                ["range"]     = Math.Round(range, 4),
                ["price"]     = Math.Round(close, 4),
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
        Strategy = "dual_thrust", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
