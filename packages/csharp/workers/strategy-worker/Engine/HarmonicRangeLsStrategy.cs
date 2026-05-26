using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// H1 研究實驗(2026-05-26、見 docs/reports/HarmonicResearch-Log.md):
///   諧波 + RegimeDetector 閘門 —— 只在 RangeBound(橫盤)允許進場、其餘 regime hold。
///
/// 假設:baseline harmonic_ls 失敗根因是「強趨勢輾過反轉訊號」。
/// 若只在橫盤跑、避開趨勢段,諧波的反轉邏輯才有機會生效。
///
/// 邏輯與 HarmonicLsStrategy 一致(PRZ/燭線確認/RSI 背離/min_conf 0.6),只多加一層 regime gate:
///   RegimeDetector.Detect(bars).Type == RangeBound → 才繼續判;否則 hold(理由帶 regime 名稱方便檢查)。
///
/// 若 H1 成立(OOS 中位 > 0, Sharpe > 0),再考慮合併回 harmonic_ls 加參數;否則保留供研究、勿實盤。
/// </summary>
public class HarmonicRangeLsStrategy : IStrategy
{
    public string Name => "harmonic_range_ls";
    public string Description => "H1: 諧波 + RegimeDetector 橫盤閘門 — 只在 RangeBound 進場";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 70;   // RegimeDetector 要 70 根才算 SMA50 斜率
    public decimal MinCapitalUsdt => 120m;

    private const int PivotWindow = 3;
    private const int EntryWindow = 8;
    private const decimal MinConf = 0.6m;

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

        // ── H1 閘門:只在橫盤 regime 繼續、其餘 regime 直接 hold ──
        var regime = RegimeDetector.Detect(bars);
        if (regime.Type != RegimeDetector.RegimeType.RangeBound)
            return Hold(config, $"非橫盤(regime={regime.Type}) — 諧波不進場");

        var det = HarmonicPatterns.Detect(bars, pivotWindow);
        if (det.PatternName == "none" || det.Direction == "none")
            return Hold(config, "橫盤 regime ✓ 但無諧波形態");

        bool fresh = det.BarsSinceD <= entryWindow;
        bool alive = det.Status != HarmonicPatterns.PatternStatus.SlHit
                  && det.Status != HarmonicPatterns.PatternStatus.Invalidated;
        if (!fresh || !alive)
            return Hold(config, $"形態 {det.PatternName}({det.Direction}) D 已過 {det.BarsSinceD} 根 或 已失效({det.Status})");

        decimal conf = det.Confidence;
        if (det.HasCandleConfirmation) conf += 0.15m;
        if (det.HasRsiDivergence) conf += 0.15m;
        conf = Math.Clamp(conf, 0m, 0.95m);
        if (conf < minConf)
            return Hold(config, $"{det.PatternName}({det.Direction}) 信心 {conf:F2} < {minConf}");

        string action = det.Direction == "bullish" ? "buy" : "sell";
        string reason = $"[橫盤] {det.PatternName} {det.Direction} @D(過 {det.BarsSinceD} 根, fit {det.Confidence:F2}"
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
                ["pattern_fit"]    = det.Confidence,
                ["direction"]      = det.Direction == "bullish" ? 1m : -1m,
                ["bars_since_d"]   = det.BarsSinceD,
                ["regime"]         = (decimal)(int)regime.Type,
                ["sma50_slope_pct"] = regime.Sma50Slope,
                ["price"]          = Math.Round(bars[^1].Close, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "harmonic_range_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
