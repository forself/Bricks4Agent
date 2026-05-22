namespace Broker.Services;

/// <summary>
/// 組合淨倉計算(per-symbol portfolio netting)的純邏輯 —— 把 N 條 sub-策略的「目前該不該做多」
/// 合成「一個淨倉的目標權重」(0..1 = 配額的幾成)。交易所單向只有一個淨倉,所以多條 edge 要先
/// 在這裡 net 成一個目標,AutoTrader 再把實際倉位管理到該目標。
///
/// 為什麼需要狀態:震盪策略發的是「轉換訊號」(buy=進、sell=出、hold=維持),不是「目標狀態」。
/// 要知道「rsi 那半現在是不是多」必須記住上一次。狀態由 AutoTrader 持有(它本來就 per-symbol stateful),
/// 策略維持無狀態。本函式是純的:給(上次各 sub 是否多)+(這次各 sub 訊號)→ 回(這次各 sub 是否多, 目標權重)。
///
/// 目前只做 long-only(配合 rsi_stoch/mfi 的多頭買回撤 edge);sell→該 sub 歸零、buy(達信心)→該 sub 做多、
/// hold/buy 未達信心→維持上次。目標權重 = 做多的 sub 數 / 總 sub 數。
/// </summary>
public static class PortfolioNetting
{
    public static (Dictionary<string, bool> States, decimal TargetWeight) Step(
        IReadOnlyDictionary<string, bool> prior,
        IReadOnlyDictionary<string, (string Action, decimal Confidence)> signals,
        decimal minConfidence = 0.6m)
    {
        var states = new Dictionary<string, bool>();
        foreach (var (sub, sig) in signals)
        {
            bool wasLong = prior.TryGetValue(sub, out var p) && p;
            bool nowLong = sig.Action switch
            {
                "buy"  => sig.Confidence >= minConfidence || wasLong,  // 達信心→進場;未達→維持(本來多就續抱、本來空就不進)
                "sell" => false,                                       // 出場→歸零
                _      => wasLong,                                     // hold→維持上次
            };
            states[sub] = nowLong;
        }
        int n = states.Count;
        decimal weight = n == 0 ? 0m : (decimal)states.Values.Count(v => v) / n;
        return (states, weight);
    }
}
