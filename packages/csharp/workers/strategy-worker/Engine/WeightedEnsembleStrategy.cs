using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Ensemble 衝突仲裁器——當成員策略意見分歧（agreement_ratio 低）時，
/// 由仲裁者讀完所有成員的 reasoning 重新給判斷。實作可以是 LLM、人工規則、
/// 或更高階的 ML 模型。預設不註冊（null）→ ensemble 走原本的加權投票。
///
/// 觸發條件：agreement_ratio &lt; threshold （預設 0.6、不足三分之二同向）。
/// 一致時直接走加權投票（更快、更便宜）。
/// </summary>
public interface IEnsembleArbitrator
{
    /// <summary>觸發 agreement_ratio 閾值——例如 0.6 = 不足 60% 同向就叫仲裁。</summary>
    decimal Threshold { get; }

    /// <summary>
    /// 由 ensemble 在偵測到分歧時呼叫。回傳 null = 沒意見（fallback 加權投票）；
    /// 回傳 Signal = 用此覆蓋 ensemble 的最終答案。失敗時應該回 null 而非拋例外。
    /// </summary>
    Signal? Arbitrate(
        IReadOnlyList<Signal> constituentSignals,
        decimal agreementRatio,
        List<BarData> bars,
        StrategyConfig config);
}

/// <summary>
/// 動態加權投票策略（Weighted Ensemble / Performance-Adaptive Voting）
///
/// 與既有 CompositeStrategy 的差異：
/// - CompositeStrategy 用**固定權重**（全部 1.0 等權）
/// - WeightedEnsembleStrategy 用**動態權重**：每次評估時，先對最近 N 根 K 線
///   做「迷你回測」，用每個成員策略的 Sharpe Ratio 當權重
/// - 結果：近期表現好的策略投票權重大，近期績效差的幾乎被靜音
///
/// 為什麼這是真的進步：
/// - 單一策略在不同市場環境有不同表現（趨勢市 vs 震盪市）
/// - Ensemble 的論點就是「讓市場自己挑出當下適合的策略」
/// - 類似隨機森林 / AdaBoost 的 boosting 概念，只是用在 trading signals
///
/// 額外輸出：agreement_ratio（成員策略同向比例）
/// - 1.0 = 全員同向（高信心）
/// - 0.33 = 三分意見（低信心）
/// - 可用於讓下游（auto-trader）在意見分歧時跳過
///
/// 衝突仲裁（選用）：注入 IEnsembleArbitrator 後，當 agreement_ratio
/// &lt; arbitrator.Threshold 時，會把所有成員 signal 餵給仲裁者讓它做最終決策。
/// 一致時不呼叫仲裁者（節省 LLM cost / 加速）。
/// </summary>
public class WeightedEnsembleStrategy : IStrategy
{
    public string Name => "ensemble";
    public string Description => "Ensemble — 動態加權投票，權重 = 成員近期 Sharpe（適應市場變化）";
    public StrategyCategory Category => StrategyCategory.Composite;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 200m;

    private readonly List<IStrategy> _constituents;
    private readonly int _evaluationBars;
    private readonly decimal _maxWeight;  // 避免單一策略完全壟斷權重
    private readonly IEnsembleArbitrator? _arbitrator;

    public WeightedEnsembleStrategy(
        List<IStrategy> constituents,
        int evaluationBars = 100,
        decimal maxWeight = 3m,
        IEnsembleArbitrator? arbitrator = null)
    {
        if (constituents == null || constituents.Count == 0)
            throw new ArgumentException("WeightedEnsembleStrategy 需要至少 1 個 constituent");
        _constituents = constituents;
        _evaluationBars = evaluationBars;
        _maxWeight = maxWeight;
        _arbitrator = arbitrator;
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        // Step 1: 當前 bars 下每個成員的訊號
        var currentSignals = _constituents.ToDictionary(
            s => s.Name,
            s => SafeEvaluate(s, bars, config));

        // Step 2: 從最近 _evaluationBars 根 K 線的 Sharpe 算動態權重
        var recentBars = bars.Count > _evaluationBars
            ? bars.Skip(bars.Count - _evaluationBars).ToList()
            : bars;

        var rawWeights = _constituents.ToDictionary(s => s.Name, s =>
        {
            try
            {
                var bt = BacktestEngine.Run(s, recentBars, config);
                // Sharpe 負的完全靜音；正的最多 _maxWeight
                return Math.Max(0m, Math.Min(bt.SharpeRatio, _maxWeight));
            }
            catch { return 0m; }
        });

        var totalWeight = rawWeights.Values.Sum();
        // 若全員都虧錢，退回等權（不讓 ensemble 完全靜音）
        if (totalWeight <= 0m)
        {
            foreach (var k in rawWeights.Keys.ToList()) rawWeights[k] = 1m;
            totalWeight = _constituents.Count;
        }

        // Step 3: 加權投票——hold 視為棄權、不參與比較（與 CompositeStrategy 同步的 fix）。
        // 原本作法把 hold 也算進 score、結果只要任一成員中性、buy/sell 票就被稀釋。
        // 改成：buyScore/sellScore 各自用「同向票權重總和」當分母（同向票的平均信心），
        // 全員 hold → hold @ 0.3；buy vs sell 持平 → hold。
        decimal buyScore = 0m, sellScore = 0m;
        decimal buyWeight = 0m, sellWeight = 0m;
        var indicators = new Dictionary<string, decimal>();
        var reasonParts = new List<string>();

        foreach (var s in _constituents)
        {
            var sig = currentSignals[s.Name];
            var wNorm = rawWeights[s.Name] / totalWeight;

            switch (sig.Action)
            {
                case "buy":
                    buyScore  += sig.Confidence * wNorm;
                    buyWeight += wNorm;
                    break;
                case "sell":
                    sellScore  += sig.Confidence * wNorm;
                    sellWeight += wNorm;
                    break;
                // hold: 棄權、不算 score 也不算 weight
            }

            indicators[$"weight.{s.Name}"] = Math.Round(wNorm, 4);
            indicators[$"vote.{s.Name}"] = sig.Action switch
            {
                "buy" => 1m,
                "sell" => -1m,
                _ => 0m,
            };
            reasonParts.Add($"[{s.Name} w={wNorm:P0}] {sig.Action}({sig.Confidence:P0})");
        }

        // Step 4: 決定 action + confidence（同向票平均信心）
        string action;
        decimal confidence;
        if (buyScore > sellScore && buyWeight > 0m)
        {
            action = "buy"; confidence = buyScore / buyWeight;
        }
        else if (sellScore > buyScore && sellWeight > 0m)
        {
            action = "sell"; confidence = sellScore / sellWeight;
        }
        else
        {
            // 全員 hold 或 buy/sell 持平
            action = "hold"; confidence = 0.3m;
        }

        // Step 5: 同向比例（用來衡量意見分歧）
        var agreements = currentSignals.Values.Count(s => s.Action == action);
        var agreementRatio = _constituents.Count == 0
            ? 0m
            : Math.Round((decimal)agreements / _constituents.Count, 4);

        indicators["agreement_ratio"] = agreementRatio;
        indicators["buy_score"] = Math.Round(buyScore, 4);
        indicators["sell_score"] = Math.Round(sellScore, 4);

        // Step 5.5: 衝突仲裁——只有「有 arbitrator 註冊 + agreement 不足」才 fallthrough 到 LLM
        // 一致時直接走加權投票結果（省 cost / 延遲）。
        //
        // Audit gap mitigation：記錄「LLM 仲裁前的加權投票結果」+「仲裁是否改變了決策」，
        // 讓 dashboard 可量化「LLM 在多少比例的衝突中翻轉了規則 ensemble」、避免被學術質疑
        // broker 變 rubber stamp（看不到 LLM 改了什麼）。
        if (_arbitrator != null && agreementRatio < _arbitrator.Threshold)
        {
            indicators["arbitration.invoked"] = 1m;
            // capture pre-arbitration state for audit
            var preArbAction = action;
            var preArbConfidence = confidence;
            indicators["arbitration.fallback_would_be"] = preArbAction switch
            {
                "buy" => 1m, "sell" => -1m, _ => 0m,
            };
            indicators["arbitration.fallback_confidence"] = Math.Round(preArbConfidence, 4);

            try
            {
                var arbitrated = _arbitrator.Arbitrate(
                    currentSignals.Values.ToList(), agreementRatio, bars, config);
                if (arbitrated != null)
                {
                    // 用仲裁者結果覆蓋 ensemble action / confidence，但保留 weight / vote / agreement
                    indicators["arbitration.result_action"] = arbitrated.Action switch
                    {
                        "buy" => 1m, "sell" => -1m, _ => 0m,
                    };
                    indicators["arbitration.result_confidence"] = arbitrated.Confidence;
                    // 量化 LLM 改了多少：1 = 翻轉決策、0 = LLM 跟加權投票同向
                    indicators["arbitration.changed_decision"] = (arbitrated.Action != preArbAction) ? 1m : 0m;
                    action = arbitrated.Action;
                    confidence = arbitrated.Confidence;
                    reasonParts.Insert(0, $"[arbitrator] {arbitrated.Reason}");
                }
                else
                {
                    // arbitrator 回 null = 沒意見、走加權投票；標記 changed=0 方便 dashboard 算覆蓋率
                    indicators["arbitration.changed_decision"] = 0m;
                }
            }
            catch
            {
                // 仲裁失敗不影響主流程——保留原本的加權投票結果
                indicators["arbitration.failed"] = 1m;
                indicators["arbitration.changed_decision"] = 0m;
            }
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name,
            Symbol = config.Symbol,
            Exchange = config.Exchange,
            Action = action,
            Confidence = Math.Round(confidence, 2),
            Reason = $"[agreement {agreementRatio:P0}] " + string.Join(" | ", reasonParts),
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
                Strategy = s.Name,
                Symbol = config.Symbol,
                Exchange = config.Exchange,
                Action = "hold",
                Confidence = 0m,
                Reason = "error",
                Interval = config.Interval,
                Indicators = new(),
            };
        }
    }
}
