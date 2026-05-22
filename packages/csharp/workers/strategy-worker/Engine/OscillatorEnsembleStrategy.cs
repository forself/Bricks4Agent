using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 專注震盪集成(osc_ensemble)—— 只把「驗證過有 OOS edge」的均值回歸震盪策略合在一起:
///   rsi_stoch / rsi_oversold / mfi / cci，動態 Sharpe 加權投票(複用 WeightedEnsembleStrategy)。
///
/// **跟失敗的 character_ensemble 的差別**:character 摻了一堆沒 edge 的成員被稀釋;這裡只放四條
/// 都有 edge 的、同一家(均值回歸)。目的:多個獨立確認 → 平滑掉單一 rsi_stoch 的 lumpy 虧損 streak。
/// 也是 regime router 的「盤整引擎」(資料顯示震盪一家在 ranging regime 稱霸、sharpe 0.5-0.67)。
///
/// 用 WeightedEnsembleStrategy 當內核(不帶 LLM arbitrator)、只覆寫 Name/分類。
/// </summary>
public class OscillatorEnsembleStrategy : IStrategy
{
    private readonly WeightedEnsembleStrategy _inner;

    public OscillatorEnsembleStrategy(List<IStrategy> members)
    {
        if (members == null || members.Count == 0)
            throw new ArgumentException("OscillatorEnsembleStrategy 需要至少 1 個成員");
        _inner = new WeightedEnsembleStrategy(members);   // 無 arbitrator = 純技術、無 LLM
    }

    public string Name => "osc_ensemble";
    public string Description => "Oscillator Ensemble — rsi_stoch/rsi_oversold/mfi/cci 動態 Sharpe 加權(專注震盪、盤整引擎)";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 200m;

    /// <summary>從註冊表挑四條驗證過的震盪策略;缺的跳過、整組空了退回只有 rsi_stoch。</summary>
    public static OscillatorEnsembleStrategy DefaultFrom(IReadOnlyDictionary<string, IStrategy> reg)
    {
        var names = new[] { "rsi_stoch", "rsi_oversold", "mfi", "cci" };
        var members = names.Where(reg.ContainsKey).Select(n => reg[n]).ToList();
        if (members.Count == 0 && reg.TryGetValue("rsi_stoch", out var fallback))
            members.Add(fallback);
        return new OscillatorEnsembleStrategy(members);
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var sig = _inner.Evaluate(bars, config);
        sig.Strategy = Name;   // 內核回 "ensemble"、蓋成 osc_ensemble(結果存對名字)
        return sig;
    }
}
