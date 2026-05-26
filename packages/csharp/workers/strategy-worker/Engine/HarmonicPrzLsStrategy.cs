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
    // H6 per-pattern breakdown(2026-05-26):允許 ctor 限定只交易某些 pattern(white list)。
    // null = 全部 10 個 pattern(預設 = harmonic_prz_ls);傳子集 = 變體(命名 harm_prz_<pattern>)。
    private readonly HashSet<string>? _patternWhitelist;
    private readonly string _name;
    // H12/H13 regime filter(2026-05-26):null = 不過濾、傳 set = 只允許這些 regime 進場
    private readonly HashSet<Indicators.RegimeDetector.RegimeType>? _regimeWhitelist;
    // H15 多窗口掃描(2026-05-26):掃最近 N 個 4-pivot 窗,取第一個有效 XABC+PRZ 命中。
    // 預設 1 = 現行行為(只看最後 4);> 1 = 鬆 trigger、撈更多形態。
    private readonly int _scanWindows;
    // H14 PRZ 浮動(2026-05-26):PRZ 區間額外往外擴 X% 讓更多當前價算進。預設 0 = 不擴。
    private readonly decimal _przWidening;

    public HarmonicPrzLsStrategy(
        IEnumerable<string>? patternWhitelist = null,
        string? name = null,
        IEnumerable<Indicators.RegimeDetector.RegimeType>? regimeWhitelist = null,
        int scanWindows = 1,
        decimal przWidening = 0m)
    {
        _patternWhitelist = patternWhitelist == null
            ? null
            : new HashSet<string>(patternWhitelist, StringComparer.OrdinalIgnoreCase);
        _name = name ?? "harmonic_prz_ls";
        _regimeWhitelist = regimeWhitelist == null ? null : new HashSet<Indicators.RegimeDetector.RegimeType>(regimeWhitelist);
        _scanWindows = Math.Max(1, scanWindows);
        _przWidening = Math.Max(0m, przWidening);
    }

    public string Name => _name;
    public string Description => _patternWhitelist == null
        ? "H5-Harmonic-PRZ:Carney 教科書用法 — 4 點 XABC + 從 A 投影 PRZ + 當前價進 PRZ 才進場"
        : $"H6-Harmonic-PRZ {string.Join("/", _patternWhitelist)}:單 pattern 變體";
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

        // H12/H13 regime gate(可選、null = 不過濾)— 部分變體只在某種 regime 進場
        if (_regimeWhitelist != null)
        {
            var regime = RegimeDetector.Detect(bars);
            if (!_regimeWhitelist.Contains(regime.Type))
                return Hold(config, $"regime {regime.Type} 不在白名單");
        }
        var pivots = HarmonicPatterns.FindPivots(bars, pivotWindow);
        if (pivots.Count < 4) return Hold(config, $"pivots 不足 4(目前 {pivots.Count})");

        // H15:掃最近 _scanWindows 個 4-pivot 窗,取第一個有效 XABC+PRZ 命中的。
        // _scanWindows=1 → 等同舊版(只看最後 4 個 pivot);> 1 → 多窗口、放鬆 trigger。
        string lastHoldReason = "no valid XABC + PRZ match";
        for (int ci = pivots.Count - 1; ci >= 3 && (pivots.Count - 1 - ci) < _scanWindows; ci--)
        {
            var X = pivots[ci - 3]; var A = pivots[ci - 2]; var B = pivots[ci - 1]; var C = pivots[ci];

            // 嚴格交替驗證
            string direction;
            if (!X.IsHigh && A.IsHigh && !B.IsHigh && C.IsHigh)
                direction = "bullish";
            else if (X.IsHigh && !A.IsHigh && B.IsHigh && !C.IsHigh)
                direction = "bearish";
            else
            {
                lastHoldReason = $"XABC[{ci}] 不交替";
                continue;
            }

            // C 不能太老(往回掃只會更老、可以提早 break;但用 continue 保留別的窗口可能恰好通過)
            int ageC = bars.Count - 1 - C.Index;
            if (ageC > maxAgeC)
            {
                lastHoldReason = $"XABC[{ci}] C 太老({ageC} > {maxAgeC})";
                break;   // 往回的 C 只會更老
            }

            // 4-點投影(帶 PRZ widening H14)
            var proj = HarmonicPatterns.ProjectFromXabc(direction, X.Price, A.Price, B.Price, C.Price, slBuffer, _przWidening);
            if (proj == null) { lastHoldReason = $"XABC[{ci}] 比率不符 pattern"; continue; }

            // pattern whitelist 過濾
            if (_patternWhitelist != null && !_patternWhitelist.Contains(proj.PatternName))
            {
                lastHoldReason = $"XABC[{ci}] pattern {proj.PatternName} 不在白名單";
                continue;
            }

            // 當前價是否在 PRZ
            decimal currentPrice = bars[^1].Close;
            bool inPrz = currentPrice >= proj.PrzLow && currentPrice <= proj.PrzHigh;
            if (!inPrz)
            {
                lastHoldReason = $"{proj.PatternName}[{ci}] 價 {currentPrice:F2} 不在 PRZ [{proj.PrzLow:F2}, {proj.PrzHigh:F2}]";
                continue;
            }

            // PRZ 失效檢查
            if (direction == "bullish" && bars[^1].Low < proj.PrzLow * (1m - slBuffer)) { lastHoldReason = $"{proj.PatternName}[{ci}] PRZ 失效"; continue; }
            if (direction == "bearish" && bars[^1].High > proj.PrzHigh * (1m + slBuffer)) { lastHoldReason = $"{proj.PatternName}[{ci}] PRZ 失效"; continue; }

            // 確認 + 信心
            int dIdx = bars.Count - 1;
            var (hasCandle, candleSig) = HarmonicPatterns.DetectCandleConfirmation(bars, dIdx, direction);
            var (hasRsiDiv, rsiB, rsiD) = HarmonicPatterns.DetectRsiDivergence(bars, B.Index, dIdx, direction);

            decimal conf = proj.Fit;
            if (hasCandle) conf += 0.15m;
            if (hasRsiDiv) conf += 0.15m;
            conf = Math.Clamp(conf, 0m, 0.95m);
            if (conf < minConf) { lastHoldReason = $"{proj.PatternName}[{ci}] conf {conf:F2} < {minConf}"; continue; }

            // ★ 找到有效訊號,emit
            string action = direction == "bullish" ? "buy" : "sell";
            string reason = $"PRZ-mode {proj.PatternName}[{ci}] {direction}:價 {currentPrice:F2} 在 PRZ[{proj.PrzLow:F2},{proj.PrzHigh:F2}] fit {proj.Fit:F2}"
                          + (hasCandle ? $", 燭線確認({candleSig})" : "")
                          + (hasRsiDiv ? ", RSI 背離" : "")
                          + $" — {(action == "buy" ? "做多" : "做空")}";

            return new Signal
            {
                SignalId = $"sig-{Guid.NewGuid():N}"[..16],
                Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
                Action = action, Confidence = Math.Round(conf, 2), Reason = reason,
                Interval = config.Interval,
                StopPrice = proj.SlPrice,
                TargetPrice = Math.Round(proj.Tp1, 4),
                Indicators = new()
                {
                    ["pattern_fit"]   = proj.Fit,
                    ["direction"]     = direction == "bullish" ? 1m : -1m,
                    ["ab_xa"]         = proj.AbRatio,
                    ["bc_ab"]         = proj.BcRatio,
                    ["prz_low"]       = proj.PrzLow,
                    ["prz_high"]      = proj.PrzHigh,
                    ["sl_price"]      = proj.SlPrice,
                    ["tp1"]           = proj.Tp1,
                    ["age_c"]         = ageC,
                    ["window_idx"]    = ci,            // 第幾個 pivot 窗找到(_scanWindows>1 時有用)
                    ["price"]         = Math.Round(currentPrice, 4),
                },
            };
        }
        return Hold(config, lastHoldReason);
    }

    private Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = _name,
        Symbol = c.Symbol, Exchange = c.Exchange, Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
