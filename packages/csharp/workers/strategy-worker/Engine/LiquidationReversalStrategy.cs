using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 流動性清算反轉(2026-06-10 從零開發、crypto 專屬機制 — registry 沒有的新 edge)。
///
/// 機制:過度槓桿 → 大幅價格 move 觸發強制平倉(爆倉)→ 級聯 → 價格 overshoot → 均值反轉。
///   - 大跌 + OI 驟降(多頭倉位被清)→ 向下 overshoot → BUY(吃反彈)
///   - 大漲 + OI 驟降(空頭被軋平)→ 向上 overshoot → SELL(吃回落)
///
/// 關鍵區分(為什麼用 OI 而非單看價格):
///   - 突破/動量:大 move 時 OI「上升」(新資金進場、趨勢有續航)→ 不該 fade
///   - 爆倉級聯:大 move 時 OI「下降」(被迫平倉、非新倉)→ 是強制賣壓 overshoot → 該 fade
///   OI 的方向把「真趨勢」跟「爆倉雜訊」分開 = 這條策略的核心。
///
/// 用 BarData.OpenInterest + vol-normalized return。跟 harmonic(技術形態反轉)機制不同、預期去相關。
/// </summary>
public class LiquidationReversalStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _moveZ;    // move 門檻(近期報酬 std 的倍數)
    private readonly decimal _oiDrop;   // OI 驟降門檻(正值、代表 -%change 下限)

    public LiquidationReversalStrategy(string name = "liquidation_reversal", decimal moveZ = 2.0m, decimal oiDrop = 0.03m)
    {
        _name = name;
        _moveZ = moveZ;
        _oiDrop = oiDrop;
    }

    public string Name => _name;
    public string Description => $"流動性清算反轉 — 大 move(≥{_moveZ}σ)+ OI 驟降(≥{_oiDrop:P0})= 強制平倉級聯 → 反向吃 overshoot 反轉";
    public StrategyCategory Category => StrategyCategory.MeanReversion;
    public int MinBars => 40;
    public decimal MinCapitalUsdt => 100m;

    private const int VolLookback = 20;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["liq_move_z"]  = new() { Type = "decimal", Default = 2.0m,  Choices = new object[] { 1.5m, 2.0m, 2.5m, 3.0m },    Description = "move 門檻(近期報酬 std 倍數)" },
        ["liq_oi_drop"] = new() { Type = "decimal", Default = 0.03m, Choices = new object[] { 0.02m, 0.03m, 0.05m, 0.08m }, Description = "OI 驟降門檻(-%change 下限)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var moveZ = config.GetParam("liq_move_z", _moveZ);
        var oiDropTh = config.GetParam("liq_oi_drop", _oiDrop);

        // 近期報酬 std(vol、用來把 move 標準化)
        int n = Math.Min(VolLookback, bars.Count - 1);
        var rets = new List<decimal>();
        for (int i = bars.Count - n; i < bars.Count; i++)
        {
            decimal pv = bars[i - 1].Close;
            if (pv > 0m) rets.Add((bars[i].Close - pv) / pv);
        }
        if (rets.Count < 10) return Hold(config, "報酬樣本不足");
        decimal mean = rets.Average();
        double v2 = 0; foreach (var r in rets) v2 += (double)(r - mean) * (double)(r - mean);
        decimal vol = (decimal)Math.Sqrt(v2 / rets.Count);
        if (vol <= 0m) return Hold(config, "vol=0");

        var last = bars[^1]; var prev = bars[^2];
        if (prev.Close <= 0m) return Hold(config, "prev close 0");
        decimal todayRet = (last.Close - prev.Close) / prev.Close;
        decimal moveInVol = todayRet / vol;

        if (!(last.OpenInterest is decimal oiCur && oiCur > 0m &&
              prev.OpenInterest is decimal oiPrev && oiPrev > 0m))
            return Hold(config, "無 OI 資料(非 perp 或未接 metrics)— 自動降級");
        decimal oiChange = (oiCur - oiPrev) / oiPrev;

        bool bigMove = Math.Abs(moveInVol) >= moveZ;
        bool oiDropped = oiChange <= -oiDropTh;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (bigMove && oiDropped)
        {
            if (moveInVol < 0m)
            {
                action = "buy";
                reason = $"大跌 {todayRet:P1}({moveInVol:F1}σ)+ OI 驟降 {oiChange:P1} = 多頭強制平倉級聯 → 反向 BUY 吃反彈";
            }
            else
            {
                action = "sell";
                reason = $"大漲 {todayRet:P1}({moveInVol:F1}σ)+ OI 驟降 {oiChange:P1} = 空頭軋空級聯 → 反向 SELL 吃回落";
            }
            confidence = Math.Clamp(0.6m + (Math.Abs(moveInVol) - moveZ) * 0.1m + (-oiChange - oiDropTh), 0.5m, 0.9m);
        }
        else
        {
            reason = $"無級聯(move {moveInVol:F1}σ / OI%change {oiChange:P1};需 |move|≥{moveZ}σ 且 OI≤−{oiDropTh:P0})— 觀望";
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
                ["move_in_vol"] = Math.Round(moveInVol, 2),
                ["oi_change"] = Math.Round(oiChange, 4),
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
