using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// H5-Harmonic-PRZ 研究實驗(2026-05-26、見 docs/reports/HarmonicResearch-Log.md):
///   教科書 Scott Carney 用法的諧波 — 4 點 XABC 完成 → 從 A 投影 PRZ → 等價格進入 PRZ 才進場。
///
/// vs 現行 harmonic_ls 的關鍵差異:
///   現行:必須 5 點(XABCD)全是已確認 pivot → D 形成後至少過 3 根才被偵測 → 實際進場在 D 之後 3-8 根
///         = **反轉開始之後才追單**,違反諧波交易原意。
///   PRZ:只要 XABC 4 點(C 是最新 pivot)確認 → 從 A 投影 D 該落在的區間 → 等當前價進入 PRZ 即可進場
///         = 反轉前/中入場,符合 Carney 原意。
///
/// 邏輯:
///   1. FindPivots,取最近 4 個 pivot 當 X/A/B/C(C 是最新 pivot、X/A/B 在它之前)
///   2. 驗證 LHLH(bullish)或 HLHL(bearish)嚴格交替
///   3. ProjectFromXabc 比對 10 種 pattern 的 AB/XA + BC/AB 比率,挑最 fit 的、算 PRZ
///   4. 當前 close 在 PRZ 內 + 反轉 K 線或 RSI 背離確認 + confidence ≥ 門檻 → 進場
///   5. Signal.StopPrice 設為 X ± 0.5% buffer(textbook Fib 失效停損,LongShortBacktestEngine 已支援)
///
/// 預期:OOS 應顯著勝現行 harmonic_ls。若仍是負期望,才能真正下諧波線「在 crypto 沒 edge」的結論。
/// </summary>
public class HarmonicPrzLsStrategy : IStrategy
{
    public string Name => "harmonic_prz_ls";
    public string Description => "H5-Harmonic-PRZ:Carney 教科書用法 — 4 點 XABC + 從 A 投影 PRZ + 當前價進 PRZ 才進場";
    public StrategyCategory Category => StrategyCategory.Pattern;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 120m;

    private const int PivotWindow = 3;
    private const int MaxAgeC = 30;           // C pivot 離當下不能太老(避免老 XABC 拖)
    private const decimal MinConf = 0.6m;
    private const decimal SlBuffer = 0.005m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["hm_pivot_window"] = new() { Type = "int",     Default = PivotWindow, Min = 2,    Max = 5,    Step = 1,    Description = "fractal pivot 窗" },
        ["hm_max_age_c"]    = new() { Type = "int",     Default = MaxAgeC,     Min = 10,   Max = 60,   Step = 10,   Description = "C pivot 最老距今幾根 K(避免老 XABC)" },
        ["hm_min_conf"]     = new() { Type = "decimal", Default = MinConf,     Min = 0.5m, Max = 0.85m, Step = 0.05m, Description = "進場最低信心" },
        ["hm_sl_buffer"]    = new() { Type = "decimal", Default = SlBuffer,    Min = 0m,   Max = 0.02m, Step = 0.005m, Description = "SL 緩衝(X ± buffer)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int pivotWindow = config.GetParam("hm_pivot_window", PivotWindow);
        int maxAgeC     = config.GetParam("hm_max_age_c",    MaxAgeC);
        decimal minConf = config.GetParam("hm_min_conf",     MinConf);
        decimal slBuffer = config.GetParam("hm_sl_buffer",   SlBuffer);
        if (bars.Count < MinBars) return Hold(config, "資料不足");

        var pivots = HarmonicPatterns.FindPivots(bars, pivotWindow);
        if (pivots.Count < 4) return Hold(config, $"pivots 不足 4(目前 {pivots.Count})");

        // 取最近 4 個 pivot 當 XABC
        var X = pivots[^4]; var A = pivots[^3]; var B = pivots[^2]; var C = pivots[^1];

        // 嚴格交替驗證
        string direction;
        if (!X.IsHigh && A.IsHigh && !B.IsHigh && C.IsHigh)
            direction = "bullish";
        else if (X.IsHigh && !A.IsHigh && B.IsHigh && !C.IsHigh)
            direction = "bearish";
        else
            return Hold(config, $"XABC 不交替(X{(X.IsHigh ? "H" : "L")}A{(A.IsHigh ? "H" : "L")}B{(B.IsHigh ? "H" : "L")}C{(C.IsHigh ? "H" : "L")})");

        // C 不能太老
        int ageC = bars.Count - 1 - C.Index;
        if (ageC > maxAgeC)
            return Hold(config, $"XABC 太老(C 距今 {ageC} 根 > {maxAgeC})");

        // 4-點投影:選最 fit 的 pattern + 算 PRZ
        var proj = HarmonicPatterns.ProjectFromXabc(direction, X.Price, A.Price, B.Price, C.Price, slBuffer);
        if (proj == null) return Hold(config, "XABC 比率不符任何 pattern");

        // 當前價是否在 PRZ
        decimal currentPrice = bars[^1].Close;
        bool inPrz = currentPrice >= proj.PrzLow && currentPrice <= proj.PrzHigh;
        if (!inPrz)
            return Hold(config, $"{proj.PatternName} XABC fit {proj.Fit:F2} 但價 {currentPrice:F2} 不在 PRZ [{proj.PrzLow:F2}, {proj.PrzHigh:F2}]");

        // PRZ 失效檢查:bullish 不能跌破 PrzLow、bearish 不能漲破 PrzHigh(已過則本 XABC 失效)
        if (direction == "bullish" && bars[^1].Low < proj.PrzLow * (1m - slBuffer))
            return Hold(config, $"{proj.PatternName} PRZ 已被跌破、失效");
        if (direction == "bearish" && bars[^1].High > proj.PrzHigh * (1m + slBuffer))
            return Hold(config, $"{proj.PatternName} PRZ 已被漲破、失效");

        // 確認:當前 bar 視為 D 候選,看燭線 + RSI 背離
        int dIdx = bars.Count - 1;
        var (hasCandle, candleSig) = HarmonicPatterns.DetectCandleConfirmation(bars, dIdx, direction);
        var (hasRsiDiv, rsiB, rsiD) = HarmonicPatterns.DetectRsiDivergence(bars, B.Index, dIdx, direction);

        decimal conf = proj.Fit;
        if (hasCandle) conf += 0.15m;
        if (hasRsiDiv) conf += 0.15m;
        conf = Math.Clamp(conf, 0m, 0.95m);
        if (conf < minConf)
            return Hold(config, $"{proj.PatternName}({direction}) PRZ 命中但 conf {conf:F2} < {minConf}");

        string action = direction == "bullish" ? "buy" : "sell";
        string reason = $"PRZ-mode {proj.PatternName} {direction}:價 {currentPrice:F2} 在 PRZ[{proj.PrzLow:F2},{proj.PrzHigh:F2}] fit {proj.Fit:F2}"
                      + (hasCandle ? $", 燭線確認({candleSig})" : "")
                      + (hasRsiDiv ? ", RSI 背離" : "")
                      + $" — {(action == "buy" ? "做多" : "做空")}";

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(conf, 2), Reason = reason,
            Interval = config.Interval,
            StopPrice = proj.SlPrice,                  // ★ engine 會讀(2026-05-26 LS engine 加 SL 支援)
            TargetPrice = Math.Round(proj.Tp1, 4),     // TP 目前 LS engine 還未讀、留著供以後
            Indicators = new()
            {
                ["pattern_name_hash"] = (decimal)proj.PatternName.Length, // 名字打 hash 給 dashboard 看
                ["pattern_fit"]  = proj.Fit,
                ["direction"]    = direction == "bullish" ? 1m : -1m,
                ["ab_xa"]        = proj.AbRatio,
                ["bc_ab"]        = proj.BcRatio,
                ["prz_low"]      = proj.PrzLow,
                ["prz_high"]     = proj.PrzHigh,
                ["sl_price"]     = proj.SlPrice,
                ["tp1"]          = proj.Tp1,
                ["age_c"]        = ageC,
                ["price"]        = Math.Round(currentPrice, 4),
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = "harmonic_prz_ls",
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
