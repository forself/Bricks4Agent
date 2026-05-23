using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 諧波形態反轉(多空)—— XABCD 諧波在 D 點是反轉訊號:bullish 形態 buy、bearish 形態 sell。
/// 諧波偵測重用已測過的 HarmonicPatterns(900 行、no-lookahead);策略邏輯自寫,且刻意採
/// Carney 真正的交易法(非盲目機械進場,後者經實證無 OOS edge):
///   - 只在 D 點附近(BarsSinceD ≤ entryWindow)且形態尚未失效(Status=Open)才考慮。
///   - 信心 = 形態 fit;有燭線確認(Hammer/Engulf)或 RSI 背離再加分。
///   - 最終信心 ≥ 0.6 才進場(否則 hold)→ 過濾掉低品質/未確認的形態。
/// 多空對稱:bullish→buy、bearish→sell、其餘 hold。無 lookahead:偵測只用 D 之前 + 確認用 D 之後但
/// 在「當下視窗」內(逐根回測自然只看得到已發生的 K)。
/// </summary>
public class HarmonicLsStrategy : IStrategy
{
    public string Name => "harmonic_ls";
    public string Description => "諧波反轉(多空)— XABCD 在 D 點 + PRZ/確認過濾,bullish 做多/bearish 做空";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 120m;

    private const int PivotWindow = 3;
    private const int EntryWindow = 8;       // D 之後幾根內才進場(近 D)
    private const decimal MinConf = 0.6m;    // 最終信心門檻

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["hm_pivot_window"] = new() { Type = "int",     Default = PivotWindow, Min = 2,    Max = 5,    Step = 1,    Description = "fractal pivot 窗" },
        ["hm_entry_window"] = new() { Type = "int",     Default = EntryWindow, Min = 2,    Max = 20,   Step = 2,    Description = "D 後可進場根數" },
        ["hm_min_conf"]     = new() { Type = "decimal", Default = MinConf,     Min = 0.5m, Max = 0.85m, Step = 0.05m, Description = "進場最低信心" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int pivotWindow = config.GetParam("hm_pivot_window", PivotWindow);
        int entryWindow = config.GetParam("hm_entry_window", EntryWindow);
        decimal minConf = config.GetParam("hm_min_conf", MinConf);
        if (bars.Count < MinBars) return Hold(config, $"資料不足(需 {MinBars}+ 根)");

        var det = HarmonicPatterns.Detect(bars, pivotWindow);
        if (det.PatternName == "none" || det.Direction == "none")
            return Hold(config, "無諧波形態");

        // 近 D + 形態未失效才考慮(失效/已停損的不追)
        bool fresh = det.BarsSinceD <= entryWindow;
        bool alive = det.Status != HarmonicPatterns.PatternStatus.SlHit
                  && det.Status != HarmonicPatterns.PatternStatus.Invalidated;
        if (!fresh || !alive)
            return Hold(config, $"形態 {det.PatternName}({det.Direction}) D 已過 {det.BarsSinceD} 根 或 已失效({det.Status})");

        // 信心 = fit + 確認加分
        decimal conf = det.Confidence;
        if (det.HasCandleConfirmation) conf += 0.15m;
        if (det.HasRsiDivergence) conf += 0.15m;
        conf = Math.Clamp(conf, 0m, 0.95m);
        if (conf < minConf)
            return Hold(config, $"{det.PatternName}({det.Direction}) 信心 {conf:F2} < {minConf} — 未確認、不追");

        string action = det.Direction == "bullish" ? "buy" : "sell";
        string reason = $"{det.PatternName} {det.Direction} @D(過 {det.BarsSinceD} 根, fit {det.Confidence:F2}"
            + (det.HasCandleConfirmation ? ", 燭線確認" : "")
            + (det.HasRsiDivergence ? ", RSI 背離" : "")
            + $") — {(action == "buy" ? "做多" : "做空")}";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["pattern_fit"]  = det.Confidence,
                ["direction"]    = det.Direction == "bullish" ? 1m : -1m,
                ["bars_since_d"] = det.BarsSinceD,
                ["prz_low"]      = det.PrzLow,
                ["prz_high"]     = det.PrzHigh,
                ["price"]        = Math.Round(bars[^1].Close, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "harmonic_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
