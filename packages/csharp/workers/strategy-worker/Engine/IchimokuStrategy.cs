using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 一目均衡表（Ichimoku）策略——日本五線雲圖系統。
///
/// 訊號規則：
///   價 > 雲頂 + Tenkan > Kijun → buy strong（雲上 + 短線金叉）
///   價 > 雲頂 + Tenkan ≤ Kijun → buy weak（雲上但短線轉弱）
///   價 < 雲底 + Tenkan < Kijun → sell strong
///   價 < 雲底 + Tenkan ≥ Kijun → sell weak
///   價 在雲層內                   → hold（不明、避免）
///
/// 設計對標：朋友 ai-quant-starter2/app/services/strategy_selector.py:s_ichimoku。
/// </summary>
public class IchimokuStrategy : IStrategy
{
    public string Name => "ichimoku";
    public string Description => "Ichimoku 一目均衡表 — 雲層位置 + 轉換/基準線金死叉";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 52;
    public decimal MinCapitalUsdt => 100m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["ichimoku_tenkan"] = new() { Type = "int", Default = 9,  Min = 6,  Max = 12, Step = 3,  Description = "轉換線(Tenkan)週期" },
        ["ichimoku_kijun"]  = new() { Type = "int", Default = 26, Min = 20, Max = 34, Step = 7,  Description = "基準線(Kijun)週期" },
        ["ichimoku_ssb"]    = new() { Type = "int", Default = 52, Min = 39, Max = 65, Step = 13, Description = "先行帶 B(SSB)週期" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int tenkan = config.GetParam("ichimoku_tenkan", 9);
        int kijun  = config.GetParam("ichimoku_kijun", 26);
        int ssb    = config.GetParam("ichimoku_ssb", 52);

        var r = Ichimoku.Compute(bars, tenkan, kijun, ssb);
        if (r == null) return Hold(config, "Not enough data for Ichimoku (need ≥ 52 bars)");

        var price = bars[^1].Close;
        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (r.PricePosition == "above_cloud")
        {
            action = "buy";
            confidence = r.TkCross == "bullish" ? 0.8m : 0.6m;
            reason = $"價 {price} 在雲層之上（雲頂 {r.CloudTop}）、Tenkan={r.Tenkan} Kijun={r.Kijun} — {(r.TkCross == "bullish" ? "短線金叉強訊號" : "雲上但短線轉弱")}";
        }
        else if (r.PricePosition == "below_cloud")
        {
            action = "sell";
            confidence = r.TkCross == "bearish" ? 0.8m : 0.6m;
            reason = $"價 {price} 在雲層之下（雲底 {r.CloudBottom}）、Tenkan={r.Tenkan} Kijun={r.Kijun} — {(r.TkCross == "bearish" ? "短線死叉強訊號" : "雲下但短線轉強")}";
        }
        else
        {
            reason = $"價 {price} 在雲層內（{r.CloudBottom} - {r.CloudTop}）— 不明方向、觀望";
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                ["price"]       = Math.Round(price, 4),
                ["tenkan"]      = r.Tenkan,
                ["kijun"]       = r.Kijun,
                ["senkou_a"]    = r.SenkouSpanA,
                ["senkou_b"]    = r.SenkouSpanB,
                ["cloud_top"]   = r.CloudTop,
                ["cloud_bottom"]= r.CloudBottom,
            },
        };
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "ichimoku",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
