using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 量能確認突破 —— Donchian(20) 通道突破 + 成交量爆量(> 20日均量 × 1.5)才進,過濾假突破。
/// 我們所有突破策略(don_trend / dual_thrust / donchian_fade)都沒看量,這支補上量價確認。
///   buy:  收盤 > 前 20 高 且 量 > 1.5×均量 且 收盤 > SMA50(順大勢)
///   sell: 收盤 < 前 20 低(向下突破 / 出場)
/// 2026-05-25 新增、先 paper 驗證。
/// </summary>
public class VolumeBreakoutStrategy : IStrategy
{
    public string Name => "volume_breakout";
    public string Description => "量能確認突破 — Donchian(20) 突破 + 量爆(>1.5×均量)+ SMA50 上方才買";
    public StrategyCategory Category => StrategyCategory.Breakout;
    public int MinBars => 55;
    public decimal MinCapitalUsdt => 100m;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"need ≥{MinBars} bars");
        int n = bars.Count;
        decimal close = bars[^1].Close;

        decimal hh = decimal.MinValue, ll = decimal.MaxValue, avgVol = 0;
        for (int k = n - 21; k < n - 1; k++)   // 前 20 bar(不含當前)
        {
            if (bars[k].High > hh) hh = bars[k].High;
            if (bars[k].Low < ll) ll = bars[k].Low;
            avgVol += bars[k].Volume;
        }
        avgVol /= 20m;
        decimal vol = bars[^1].Volume;
        decimal sma50 = 0; for (int k = n - 50; k < n; k++) sma50 += bars[k].Close; sma50 /= 50m;
        bool volSurge = avgVol > 0m && vol > avgVol * 1.5m;

        string action = "hold"; decimal conf = 0m; string reason;
        if (close > hh && volSurge && close > sma50)
        {
            action = "buy"; conf = 0.75m;
            reason = $"突破前20高 {hh:F4}(收{close:F4})+ 量 {vol:F0}>1.5×均量 {avgVol:F0} + SMA50 上方 — 量增突破";
        }
        else if (close < ll)
        {
            action = "sell"; conf = 0.7m;
            reason = $"跌破前20低 {ll:F4}(收{close:F4})— 向下突破/出場";
        }
        else reason = volSurge ? $"量增但未破高({ll:F4}<{close:F4}<{hh:F4})" : $"無突破或量不足(量{vol:F0} vs 均{avgVol:F0})";

        return Sig(config, action, conf, reason, new()
        {
            ["price"] = Math.Round(close, 4), ["don_high"] = Math.Round(hh, 4), ["don_low"] = Math.Round(ll, 4),
            ["vol"] = Math.Round(vol, 2), ["avg_vol"] = Math.Round(avgVol, 2), ["sma50"] = Math.Round(sma50, 4),
        });
    }

    private Signal Hold(StrategyConfig c, string r) => Sig(c, "hold", 0m, r, new());
    private Signal Sig(StrategyConfig c, string a, decimal conf, string r, Dictionary<string, decimal> ind) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16], Strategy = Name, Symbol = c.Symbol, Exchange = c.Exchange,
        Action = a, Confidence = conf, Reason = r, Interval = c.Interval, Indicators = ind,
    };
}
