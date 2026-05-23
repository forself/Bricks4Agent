using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 淨加權曝險 ensemble(Net-Weighted Exposure)—— 比投票式 ensemble 更貼近「組合」的真相:
/// 在真實交易所,單一 symbol 只有一個淨部位,「N 支加權組合在該 symbol 的損益」
/// 恆等於「持有 N 支加權後的淨曝險」損益(同價格、個別損益可相加)。所以本 ensemble 不投票、
/// 而是算淨曝險:
///   net = Σ wᵢ · dirᵢ        (dir: buy=+1, sell=−1, hold=0;wᵢ = 固定權重、預設反波動率)
///   |net| < minNet           → hold(分歧太大、淨曝險太小)
///   action = sign(net)、confidence 隨 |net| 遞增 → 搭配 confidence-sizing 時:
///     成員一致(|net|→1)= 滿倉;分歧(|net|小)= 縮小部位 → 等效「組合在分歧時自動降曝險」。
///
/// 與 WeightedEnsembleStrategy(投票、分歧退 hold、抹掉紅利)的差別就在這:本版保留淨曝險的大小資訊,
/// 因此(配 AUTOTRADER_CONFIDENCE_SIZING_ENABLED)能真正復刻風險加權組合、而非只取多數方向。
/// </summary>
public class NetWeightedEnsembleStrategy : IStrategy
{
    public string Name { get; }
    public string Description => "Net-Weighted Ensemble — 淨加權曝險(非投票)、confidence∝|淨曝險|,復刻風險加權組合";
    public StrategyCategory Category => StrategyCategory.Composite;
    public int MinBars => _constituents.Max(c => c.s.MinBars);
    public decimal MinCapitalUsdt => 200m;

    private readonly List<(IStrategy s, decimal w)> _constituents;
    private readonly decimal _minNet;

    /// <param name="weighted">成員 + 權重(會自動正規化成和=1)。</param>
    /// <param name="name">註冊名(預設 net_ensemble)。</param>
    /// <param name="minNet">|淨曝險| 低於此值就 hold(預設 0.10)。</param>
    public NetWeightedEnsembleStrategy(
        List<(IStrategy s, decimal w)> weighted, string name = "net_ensemble", decimal minNet = 0.10m)
    {
        if (weighted == null || weighted.Count == 0)
            throw new ArgumentException("NetWeightedEnsembleStrategy 需要至少 1 個成員");
        var sum = weighted.Sum(x => x.w);
        if (sum <= 0m) throw new ArgumentException("權重總和必須 > 0");
        _constituents = weighted.Select(x => (x.s, x.w / sum)).ToList();   // 正規化
        Name = name;
        _minNet = minNet;
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        decimal net = 0m;
        var indicators = new Dictionary<string, decimal>();
        var parts = new List<string>();

        foreach (var (s, w) in _constituents)
        {
            var sig = SafeEvaluate(s, bars, config);
            // 只有信心達門檻的方向才計入淨曝險(跟回測引擎的進場門檻一致)
            int dir = (sig.Confidence >= 0.6m)
                ? sig.Action switch { "buy" => 1, "sell" => -1, _ => 0 }
                : 0;
            net += w * dir;
            indicators[$"w.{s.Name}"] = Math.Round(w, 4);
            indicators[$"dir.{s.Name}"] = dir;
            parts.Add($"{s.Name}({w:P0}){(dir > 0 ? "多" : dir < 0 ? "空" : "·")}");
        }

        indicators["net_exposure"] = Math.Round(net, 4);

        string action; decimal confidence; string reason;
        if (Math.Abs(net) < _minNet)
        {
            action = "hold"; confidence = 0.5m;
            reason = $"淨曝險 {net:F2} (<{_minNet}) 成員分歧 — 觀望 | {string.Join(" ", parts)}";
        }
        else
        {
            action = net > 0 ? "buy" : "sell";
            // confidence 隨 |net| 從 0.6 升到 1.0 → 搭配 confidence-sizing:一致滿倉、分歧縮量
            confidence = Math.Clamp(0.55m + Math.Abs(net) * 0.45m, 0.6m, 1.0m);
            reason = $"淨曝險 {net:F2} → {(action == "buy" ? "淨多" : "淨空")} | {string.Join(" ", parts)}";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval, Indicators = indicators,
        };
    }

    private static Signal SafeEvaluate(IStrategy s, List<BarData> bars, StrategyConfig config)
    {
        try { return s.Evaluate(bars, config); }
        catch
        {
            return new Signal { Action = "hold", Confidence = 0m, Strategy = s.Name, Symbol = config.Symbol, Interval = config.Interval };
        }
    }
}
