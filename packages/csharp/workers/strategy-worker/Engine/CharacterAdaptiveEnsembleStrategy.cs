using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 性格自適應集成(Character-Adaptive Ensemble)—— 用「市場性格」當 meta 閘門去調成員權重,
/// 而不是再多投一票。這是把 Hurst(記憶性)+ 波動率百分位這兩個正交維度真正用起來的方式。
///
/// 跟既有兩個 meta-strategy 的差異:
///   - ensemble        = 權重 = 成員近期 Sharpe(backward-looking、「最近誰賺」)。
///   - regime_adaptive = RegimeDetector 離散分類(SMA 斜率/ATR/BB 寬)→ 換「一組固定權重」。
///   - character(本檔)= 用 Hurst + 波動率百分位「連續」調每個成員的權重乘數:
///       H 高(趨勢延續)→ 放大 Trend/Momentum/Breakout、靜音 MeanReversion;
///       H 低(均值回歸)→ 反過來;隨機漫步 → 不偏(全 ×1)。
///       再疊一層波動率:擠壓 → 放大 Breakout;高波動 → 偏好跨週期確認、其餘降噪。
///
/// 為什麼這比「再加一個投票成員」有區別性:它跟 Sharpe 權重正交 ——
/// Sharpe 看「最近哪個成員賺」,性格閘門看「現在這種市場結構,哪『類』策略本來就該被信」。
/// 兩者相乘 = 既看績效、又看市場性格,擋掉「趨勢策略在盤整裡亂進、回歸策略在單邊裡接刀」。
/// </summary>
public class CharacterAdaptiveEnsembleStrategy : IStrategy
{
    public string Name => "character_ensemble";
    public string Description => "Character-Adaptive Ensemble — Hurst+波動率當 meta 閘門,依市場性格連續調成員權重";
    public StrategyCategory Category => StrategyCategory.Composite;
    public int MinBars => 60;
    public decimal MinCapitalUsdt => 200m;

    private const int HurstLookback = 100;
    private const decimal TrendTh = 0.55m;
    private const decimal MeanRevTh = 0.45m;
    private const decimal HighVolPct = 0.8m;
    private const decimal SqueezePct = 0.2m;
    private const decimal SkewTh = -0.5m;            // 偏度低於此 = 負偏(左尾長)
    private const decimal KurtTh = 1.0m;             // 超額峰度高於此 = 肥尾
    private const decimal TailRiskDiscount = 0.7m;   // 尾部風險高時、多頭信心打折
    private const decimal FundingHotPct = 0.9m;      // 資金費率百分位高於此 = 多頭過熱(contrarian)
    private const decimal FundingColdPct = 0.1m;     // 低於此 = 空頭過熱
    private const decimal FundingDiscount = 0.8m;    // 過熱方向的信心打折

    private readonly List<IStrategy> _constituents;

    public CharacterAdaptiveEnsembleStrategy(List<IStrategy> constituents)
    {
        if (constituents == null || constituents.Count == 0)
            throw new ArgumentException("CharacterAdaptiveEnsembleStrategy 需要至少 1 個 constituent");
        _constituents = constituents;
    }

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["char_trend_th"]   = new() { Type = "decimal", Default = 0.55m, Choices = new object[] { 0.52m, 0.55m, 0.58m, 0.6m }, Description = "判定趨勢市的 Hurst 門檻(以上放大順勢類)" },
        ["char_meanrev_th"] = new() { Type = "decimal", Default = 0.45m, Choices = new object[] { 0.4m, 0.42m, 0.45m, 0.48m }, Description = "判定均值回歸市的 Hurst 門檻(以下放大回歸類)" },
        // 尾部風險閘門(skew/kurt 門檻)刻意「不」放進 ParamSchema:風控閾值不該被 grid search
        // curve-fit(會過擬合歷史崩盤),用穩健固定值;且只優化兩個核心門檻 → grid 16 組、不爆。
    };

    /// <summary>
    /// 從已註冊策略挑一批「跨類別」的廣譜成員(這樣按類別調權重才有差異)。
    /// 缺哪個就跳過;整組空了退回 composite。權重不需總和=1,內部會 normalize。
    /// </summary>
    public static CharacterAdaptiveEnsembleStrategy DefaultFrom(IReadOnlyDictionary<string, IStrategy> reg)
    {
        var names = new[]
        {
            "sma_cross", "super_trend",        // Trend
            "macd_divergence",                 // Momentum
            "rsi_oversold", "bollinger_bands", // MeanReversion
            "donchian",                        // Breakout
            "multi_timeframe",                 // MultiTimeframe
            "obv",                             // Volume
            "harmonic_pattern",                // Pattern
        };
        var members = new List<IStrategy>();
        foreach (var n in names)
            if (reg.TryGetValue(n, out var s)) members.Add(s);
        if (members.Count == 0 && reg.TryGetValue("composite", out var comp))
            members.Add(comp);
        return new CharacterAdaptiveEnsembleStrategy(members);
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars)
            return Hold(config, $"Not enough data (need ≥ {MinBars} bars)");

        var trendTh   = config.GetParam("char_trend_th", TrendTh);
        var meanRevTh = config.GetParam("char_meanrev_th", MeanRevTh);

        // meta 特徵:Hurst(性格)+ 波動率百分位 + 報酬分布(尾部風險)。算不出來就降級。
        var hurst  = Hurst.Compute(bars, HurstLookback);
        var volPct = VolatilityRegime.Compute(bars)?.Percentile;
        var dist   = ReturnDistribution.Compute(bars, HurstLookback);

        decimal character = 0m;  // 1=趨勢、-1=回歸、0=隨機/未知
        if (hurst != null)
        {
            if (hurst.Value >= trendTh) character = 1m;
            else if (hurst.Value <= meanRevTh) character = -1m;
        }

        decimal buyScore = 0m, sellScore = 0m, buyWeight = 0m, sellWeight = 0m;
        var indicators = new Dictionary<string, decimal>();
        var reasonParts = new List<string>();

        // 先算每個成員的性格權重乘數(基礎權重 1.0 × 性格乘數 × 波動率乘數)
        var weights = new Dictionary<string, decimal>();
        foreach (var s in _constituents)
            weights[s.Name] = CharacterMult(s.Category, hurst, trendTh, meanRevTh) * VolMult(s.Category, volPct);

        var totalWeight = weights.Values.Sum();
        if (totalWeight <= 0m)  // 理論上不會(最低 0.5),保險退回等權
        {
            foreach (var k in weights.Keys.ToList()) weights[k] = 1m;
            totalWeight = _constituents.Count;
        }

        var signals = new Dictionary<string, Signal>();
        foreach (var s in _constituents)
        {
            var sig  = SafeEvaluate(s, bars, config);
            signals[s.Name] = sig;
            var wNorm = weights[s.Name] / totalWeight;

            switch (sig.Action)
            {
                case "buy":  buyScore  += sig.Confidence * wNorm; buyWeight  += wNorm; break;
                case "sell": sellScore += sig.Confidence * wNorm; sellWeight += wNorm; break;
                // hold = 棄權
            }

            indicators[$"weight.{s.Name}"] = Math.Round(wNorm, 4);
            indicators[$"vote.{s.Name}"]   = sig.Action switch { "buy" => 1m, "sell" => -1m, _ => 0m };
            reasonParts.Add($"[{s.Name} w={wNorm:P0}] {sig.Action}({sig.Confidence:P0})");
        }

        string action;
        decimal confidence;
        if (buyScore > sellScore && buyWeight > 0m)       { action = "buy";  confidence = buyScore / buyWeight; }
        else if (sellScore > buyScore && sellWeight > 0m) { action = "sell"; confidence = sellScore / sellWeight; }
        else                                              { action = "hold"; confidence = 0.3m; }

        // 尾部風險閘門:負偏 + 高峰 = 下行脆弱(易暴跌)。風險不對稱、做多更危險,
        // 故只砍多頭信心(不動做空/觀望)。閾值用穩健固定值(不 curve-fit)。
        decimal tailRisk = 0m;
        if (dist != null && dist.Value.Skew < SkewTh && dist.Value.Kurtosis > KurtTh)
        {
            tailRisk = 1m;
            if (action == "buy") confidence *= TailRiskDiscount;
        }

        // 資金費率擁擠閘門:funding 在近期極端高 = 多頭過熱(contrarian)→ 砍多頭;極端低 = 空頭過熱 → 砍空頭。
        // 與方向正交、純風控;無 perp 資料時 fb=null 自動降級(不影響行為)。
        var fb = FundingBias.Compute(bars, HurstLookback);
        decimal fundingGate = 0m;
        if (fb != null)
        {
            if (fb.Value.FundingPercentile > FundingHotPct && action == "buy")
            { confidence *= FundingDiscount; fundingGate = 1m; }
            else if (fb.Value.FundingPercentile < FundingColdPct && action == "sell")
            { confidence *= FundingDiscount; fundingGate = -1m; }
        }

        // agreement 複用第一遍的 signals、不再重跑成員(回測時省一半成員 Evaluate)
        var agreements = _constituents.Count(s => signals[s.Name].Action == action);
        var agreementRatio = _constituents.Count == 0 ? 0m
            : Math.Round((decimal)agreements / _constituents.Count, 4);

        indicators["hurst"]           = hurst ?? -1m;     // -1 = 算不出(資料不足)
        indicators["vol_percentile"]  = volPct ?? -1m;
        indicators["character"]       = character;
        indicators["skew"]            = dist?.Skew ?? 0m;
        indicators["kurtosis"]        = dist?.Kurtosis ?? 0m;
        indicators["tail_risk"]       = tailRisk;
        indicators["funding_rate"]    = fb?.FundingRate ?? 0m;
        indicators["funding_pctile"]  = fb?.FundingPercentile ?? -1m;
        indicators["oi_change_pct"]   = fb?.OiChangePct ?? 0m;
        indicators["funding_gate"]    = fundingGate;
        indicators["agreement_ratio"] = agreementRatio;
        indicators["buy_score"]       = Math.Round(buyScore, 4);
        indicators["sell_score"]      = Math.Round(sellScore, 4);

        var charLabel = character == 1m ? "趨勢" : character == -1m ? "回歸" : "隨機";
        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = $"[性格:{charLabel} H={(hurst.HasValue ? hurst.Value.ToString("F3") : "n/a")} vol={(volPct.HasValue ? volPct.Value.ToString("P0") : "n/a")}{(tailRisk == 1m ? " 尾部風險:砍多頭" : "")}{(fundingGate != 0m ? $" funding擁擠:砍{(fundingGate == 1m ? "多" : "空")}" : "")}] "
                         + string.Join(" | ", reasonParts),
            Interval   = config.Interval,
            Indicators = indicators,
        };
    }

    /// <summary>性格乘數:H 高放大順勢類、H 低放大回歸類、隨機/未知不偏。</summary>
    private static decimal CharacterMult(StrategyCategory cat, decimal? hurst, decimal trendTh, decimal meanRevTh)
    {
        if (hurst == null) return 1m;
        decimal h = hurst.Value;

        if (h >= trendTh) return cat switch
        {
            StrategyCategory.Trend          => 1.5m,
            StrategyCategory.Momentum       => 1.4m,
            StrategyCategory.Breakout       => 1.3m,
            StrategyCategory.MultiTimeframe => 1.2m,
            StrategyCategory.MeanReversion  => 0.5m,
            _                               => 1m,
        };

        if (h <= meanRevTh) return cat switch
        {
            StrategyCategory.MeanReversion  => 1.5m,
            StrategyCategory.Pattern        => 1.2m,
            StrategyCategory.Volume         => 1.1m,
            StrategyCategory.Trend          => 0.5m,
            StrategyCategory.Momentum       => 0.6m,
            StrategyCategory.Breakout       => 0.7m,
            _                               => 1m,
        };

        return 1m;  // 隨機漫步:不偏
    }

    /// <summary>波動率乘數:擠壓放大突破系、高波動偏好跨週期確認且其餘降噪。</summary>
    private static decimal VolMult(StrategyCategory cat, decimal? volPct)
    {
        if (volPct == null) return 1m;
        decimal p = volPct.Value;

        if (p >= HighVolPct) return cat switch
        {
            StrategyCategory.MultiTimeframe => 1.3m,
            StrategyCategory.Pattern        => 1.1m,
            StrategyCategory.Breakout       => 0.85m,
            _                               => 0.9m,
        };

        if (p <= SqueezePct) return cat switch
        {
            StrategyCategory.Breakout => 1.4m,
            StrategyCategory.Trend    => 1.1m,
            _                         => 1m,
        };

        return 1m;
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
        Strategy = "character_ensemble",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
