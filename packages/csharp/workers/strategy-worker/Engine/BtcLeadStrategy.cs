using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// BTC 領先-alt 滯後(2026-06-10 從零開發、crypto 跨幣資訊流機制)。
///
/// 機制:資訊/流動性先打進 BTC、alt 滯後跟隨。BTC 近 N 日報酬 = 領先訊號:
///   - BTC 領先上漲(≥moveZ σ)→ alt 滯後跟漲 → LONG
///   - BTC 領先下跌(≤−moveZ σ)→ alt 滯後跟跌 → SHORT
///
/// 關鍵(跟「alt 自身動量」的差異):用 BTC[t] 預測 alt[t+1](隔日跟隨)、非 alt 自身動量。
/// 不是趨勢/反轉類 → 不被「crypto 趨勢策略不行」預判死;是跨資產 lead-lag 微結構。
/// 需 strat-validate 注入 BarData.BtcRet(每幣每日對齊 BTC 當日報酬)。BTC 自身不適用(略過)。
/// </summary>
public class BtcLeadStrategy : IStrategy
{
    private readonly string _name;
    private readonly int _lag;        // 領先窗(累積 BTC 報酬的天數)
    private readonly decimal _moveZ;  // 進場門檻(BTC 領先訊號的 sigma)

    public BtcLeadStrategy(string name = "btc_lead", int lag = 1, decimal moveZ = 1.0m)
    {
        _name = name;
        _lag = lag;
        _moveZ = moveZ;
    }

    public string Name => _name;
    public string Description => $"BTC 領先-alt 滯後 — BTC 近{_lag}日報酬 ≥{_moveZ}σ → LONG alt(資訊流 BTC→alt 滯後跟);≤−{_moveZ}σ → SHORT";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int VolLookback = 30;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["btclead_lag"]    = new() { Type = "decimal", Default = 1m,   Choices = new object[] { 1m, 2m, 3m },             Description = "BTC 領先累積天數" },
        ["btclead_move_z"] = new() { Type = "decimal", Default = 1.0m, Choices = new object[] { 0.5m, 1.0m, 1.5m, 2.0m }, Description = "領先訊號門檻(sigma)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");
        // BTC 自身不適用(會變成 BTC 動量自我預測)
        if (config.Symbol.StartsWith("BTC", StringComparison.OrdinalIgnoreCase))
            return Hold(config, "BTC 自身不適用 BTC-lead(略過)");

        int lag = (int)config.GetParam("btclead_lag", (decimal)_lag);
        var moveZ = config.GetParam("btclead_move_z", _moveZ);
        if (lag < 1) lag = 1;

        // BtcRet 序列(strat-validate 注入)→ 算 BTC vol
        var btcRets = new List<decimal>();
        int n = Math.Min(VolLookback + lag, bars.Count);
        for (int i = bars.Count - n; i < bars.Count; i++)
            if (bars[i].BtcRet is decimal r) btcRets.Add(r);
        if (btcRets.Count < 20) return Hold(config, "無 BTC 報酬注入(非 crypto 或缺 BTCUSDT)— 自動降級");

        decimal m = btcRets.Average();
        double v2 = 0; foreach (var r in btcRets) v2 += (double)(r - m) * (double)(r - m);
        decimal btcVol = (decimal)Math.Sqrt(v2 / btcRets.Count);
        if (btcVol <= 0m) return Hold(config, "BTC vol=0");

        // 領先訊號 = 最近 lag 天 BTC 累積報酬(以 BTC[t] 預測 alt[t+1])
        decimal lead = 0; int got = 0;
        for (int i = bars.Count - lag; i < bars.Count; i++)
            if (i >= 0 && bars[i].BtcRet is decimal r) { lead += r; got++; }
        if (got < lag) return Hold(config, "BTC 報酬不足");
        decimal leadZ = lead / (btcVol * (decimal)Math.Sqrt(lag));

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (leadZ >= moveZ)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (leadZ - moveZ) * 0.1m, 0.5m, 0.9m);
            reason = $"BTC 近{lag}日 +{lead:P1}({leadZ:F1}σ)領先上漲 → alt 滯後跟漲 LONG";
        }
        else if (leadZ <= -moveZ)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (-leadZ - moveZ) * 0.1m, 0.5m, 0.9m);
            reason = $"BTC 近{lag}日 {lead:P1}({leadZ:F1}σ)領先下跌 → alt 滯後跟跌 SHORT";
        }
        else
        {
            reason = $"BTC 領先訊號 {leadZ:F1}σ 在中性區(需 |z|≥{moveZ})— 觀望";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name,
            Symbol = config.Symbol,
            Exchange = config.Exchange,
            Action = action,
            Confidence = Math.Round(confidence, 2),
            Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["btc_lead_z"] = Math.Round(leadZ, 2),
                ["btc_lead_ret"] = Math.Round(lead, 4),
            },
        };
    }

    private Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = _name,
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
