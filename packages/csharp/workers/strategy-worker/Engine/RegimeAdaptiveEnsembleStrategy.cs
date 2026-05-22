using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Regime 條件式加權集成（Regime-Adaptive Ensemble）。
///
/// 跟既有兩個 meta-strategy 的差異：
///   - ensemble      = 跑「固定一組」成員、權重 = 近期 Sharpe（backward-looking）
///   - auto_select   = 偵測 regime → 挑「單一」策略
///   - regime_adaptive（本檔）= 偵測 regime → 跑「該 regime 專屬的一組策略 + 各自固定權重」做加權投票
///
/// 概念來源：朋友 ai-quant-starter2/app/services/dynamic_strategy.py 的「6 種行情 × 策略組合映射」
/// （只學概念、C# 重寫、權重表用我們自己的策略 + 6 種 RegimeType 對應）。
///
/// 每個 regime 配一組「擅長該行情」的策略：趨勢市給趨勢跟隨、震盪市給均值回歸、
/// 收斂給突破、高波動給跨時段確認。權重固定（forward-looking、換 regime 立刻換組），
/// 投票方式跟 ensemble 一致：hold 棄權、buy/sell 各自取同向票加權平均信心。
/// </summary>
public class RegimeAdaptiveEnsembleStrategy : IStrategy
{
    public string Name => "regime_adaptive";
    public string Description => "Regime-Adaptive Ensemble — 偵測行情類型 → 跑該 regime 專屬策略組合（固定權重加權投票）";
    public StrategyCategory Category => StrategyCategory.Composite;
    public int MinBars => 50;                  // RegimeDetector 需 50 根
    public decimal MinCapitalUsdt => 200m;

    private readonly Dictionary<RegimeDetector.RegimeType, List<(IStrategy Strat, decimal Weight)>> _regimeCombos;
    private readonly List<(IStrategy Strat, decimal Weight)> _fallback;

    public RegimeAdaptiveEnsembleStrategy(
        Dictionary<RegimeDetector.RegimeType, List<(IStrategy, decimal)>> regimeCombos,
        List<(IStrategy, decimal)> fallback)
    {
        if (regimeCombos == null || regimeCombos.Count == 0)
            throw new ArgumentException("RegimeAdaptiveEnsembleStrategy 需要至少 1 個 regime combo");
        if (fallback == null || fallback.Count == 0)
            throw new ArgumentException("RegimeAdaptiveEnsembleStrategy 需要 fallback combo");
        _regimeCombos = regimeCombos;
        _fallback = fallback;
    }

    /// <summary>
    /// 從已註冊策略建出預設 regime → 權重組合表。某策略沒註冊就跳過該條目；
    /// 整組空了就退回 fallback（broad 投票）。權重不需總和為 1、內部會 normalize。
    /// </summary>
    public static RegimeAdaptiveEnsembleStrategy DefaultFrom(IReadOnlyDictionary<string, IStrategy> reg)
    {
        List<(IStrategy, decimal)> Combo(params (string Name, decimal W)[] items)
        {
            var outl = new List<(IStrategy, decimal)>();
            foreach (var (name, w) in items)
                if (reg.TryGetValue(name, out var s)) outl.Add((s, w));
            return outl;
        }

        // broad fallback = 跟 ensemble 同一組廣譜成員
        var fallback = Combo(
            ("sma_cross", 1m), ("rsi_oversold", 1m), ("macd_divergence", 1m), ("multi_timeframe", 1m));
        if (fallback.Count == 0 && reg.TryGetValue("composite", out var comp))
            fallback = new() { (comp, 1m) };

        var combos = new Dictionary<RegimeDetector.RegimeType, List<(IStrategy, decimal)>>
        {
            // 強勢上漲：趨勢跟隨為主
            [RegimeDetector.RegimeType.TrendingUp] = Combo(
                ("vegas_tunnel", 0.25m), ("super_trend", 0.22m), ("adx_di", 0.18m),
                ("ichimoku", 0.15m), ("macd_divergence", 0.12m), ("smc", 0.08m)),

            // 下跌：反轉停損 + 動能轉弱偵測
            [RegimeDetector.RegimeType.TrendingDown] = Combo(
                ("super_trend", 0.25m), ("parabolic_sar", 0.22m), ("adx_di", 0.18m),
                ("rsi_stoch", 0.15m), ("sma_cross", 0.12m), ("chaikin_mf", 0.08m)),

            // 震盪：均值回歸 + 通道
            [RegimeDetector.RegimeType.RangeBound] = Combo(
                ("rsi_oversold", 0.22m), ("bollinger_bands", 0.20m), ("cci", 0.18m),
                ("rsi_stoch", 0.15m), ("vwap", 0.13m), ("keltner", 0.12m)),

            // 收斂蓄勢：突破系 + 量能確認
            [RegimeDetector.RegimeType.Squeeze] = Combo(
                ("bollinger_bands", 0.25m), ("donchian", 0.22m), ("keltner", 0.18m),
                ("obv", 0.15m), ("adx_di", 0.12m), ("price_action", 0.08m)),

            // 高波動：跨時段確認 + 結構，避免雜訊
            [RegimeDetector.RegimeType.HighVol] = Combo(
                ("multi_timeframe", 0.30m), ("super_trend", 0.22m), ("vwap", 0.18m),
                ("mfi", 0.15m), ("harmonic_pattern", 0.15m)),

            // 不明：廣譜投票
            [RegimeDetector.RegimeType.Unclear] = fallback,
        };

        // 任何 regime 組合空了（策略沒註冊）→ 用 fallback 補
        foreach (var key in combos.Keys.ToList())
            if (combos[key].Count == 0) combos[key] = fallback;

        return new RegimeAdaptiveEnsembleStrategy(combos, fallback);
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars)
            return Hold(config, $"Not enough data for regime detection (need ≥ {MinBars} bars)");

        var regime = RegimeDetector.Detect(bars);
        var combo = _regimeCombos.GetValueOrDefault(regime.Type);
        if (combo == null || combo.Count == 0) combo = _fallback;

        var totalWeight = combo.Sum(c => c.Weight);
        if (totalWeight <= 0m) totalWeight = combo.Count;

        decimal buyScore = 0m, sellScore = 0m, buyWeight = 0m, sellWeight = 0m;
        var indicators = new Dictionary<string, decimal>();
        var reasonParts = new List<string>();

        var sigs = new Dictionary<string, Signal>();
        foreach (var (strat, weight) in combo)
        {
            var sig = SafeEvaluate(strat, bars, config);
            sigs[strat.Name] = sig;
            var wNorm = weight / totalWeight;

            switch (sig.Action)
            {
                case "buy":  buyScore  += sig.Confidence * wNorm; buyWeight  += wNorm; break;
                case "sell": sellScore += sig.Confidence * wNorm; sellWeight += wNorm; break;
                // hold = 棄權
            }

            indicators[$"weight.{strat.Name}"] = Math.Round(wNorm, 4);
            indicators[$"vote.{strat.Name}"] = sig.Action switch { "buy" => 1m, "sell" => -1m, _ => 0m };
            reasonParts.Add($"[{strat.Name} w={wNorm:P0}] {sig.Action}({sig.Confidence:P0})");
        }

        string action;
        decimal confidence;
        if (buyScore > sellScore && buyWeight > 0m)      { action = "buy";  confidence = buyScore / buyWeight; }
        else if (sellScore > buyScore && sellWeight > 0m) { action = "sell"; confidence = sellScore / sellWeight; }
        else                                              { action = "hold"; confidence = 0.3m; }

        // agreement 複用第一遍 signals、不再重跑成員(回測省一半成員 Evaluate)
        var agreements = combo.Count(c => sigs[c.Strat.Name].Action == action);
        var agreementRatio = combo.Count == 0 ? 0m : Math.Round((decimal)agreements / combo.Count, 4);

        indicators["regime.type"]        = (decimal)(int)regime.Type;
        indicators["regime.sma50_slope"] = regime.Sma50Slope;
        indicators["regime.atr_pct"]     = regime.AtrPct;
        indicators["regime.bb_width"]    = regime.BbWidth;
        indicators["agreement_ratio"]    = agreementRatio;
        indicators["buy_score"]          = Math.Round(buyScore, 4);
        indicators["sell_score"]         = Math.Round(sellScore, 4);
        indicators["combo_size"]         = combo.Count;

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name,
            Symbol = config.Symbol,
            Exchange = config.Exchange,
            Action = action,
            Confidence = Math.Round(confidence, 2),
            Reason = $"[regime:{regime.Type} {regime.Description}] " + string.Join(" | ", reasonParts),
            Interval = config.Interval,
            Indicators = indicators,
        };
    }

    private static Signal SafeEvaluate(IStrategy s, List<BarData> bars, StrategyConfig config)
    {
        try { return s.Evaluate(bars, config); }
        catch
        {
            return new Signal
            {
                SignalId = $"sig-{Guid.NewGuid():N}"[..16],
                Strategy = s.Name, Symbol = config.Symbol, Exchange = config.Exchange,
                Action = "hold", Confidence = 0m, Reason = "error", Interval = config.Interval,
            };
        }
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "regime_adaptive",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
