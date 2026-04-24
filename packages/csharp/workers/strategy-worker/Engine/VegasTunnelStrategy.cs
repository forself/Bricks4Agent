using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 維加斯通道策略（Vegas Tunnel Strategy）。
///
/// 這個策略展示的是「多層 EMA 趨勢過濾 + 回檔進場」的交易哲學，與布林通道（均值回歸）、
/// 斐波那契（擺動回撤）屬於完全不同流派，適合放在策略比較實驗中作為趨勢系交易法的代表。
///
/// 進場規則：
///   ─ 多頭（long-only 進場信號）─
///     條件 A：大趨勢為多（長通道位於主通道下方、即長通道在下方支撐整個市場結構）
///     條件 B：價格回檔至主通道內部（PriceZone == 0）或剛從下緣反彈（PriceZone == 0 且
///            上一根在下方）
///     條件 C：EMA12 觸發線由下往上穿越主通道中軸（TriggerCross == +1）
///     → 三者齊備 → buy，信心 0.7-0.9，反映通道寬度與長通道距離
///
///   ─ 空頭（sell 信號）─
///     條件 A：大趨勢為空（長通道位於主通道上方）
///     條件 B：價格反彈至主通道內部
///     條件 C：EMA12 觸發線由上往下穿越主通道中軸（TriggerCross == -1）
///     → buy/sell 對稱邏輯
///
/// 為何需要長通道濾波：主通道（144/169）本身在盤整市會產生大量假訊號，長通道（576/676）
/// 幫助排除那些「短期回檔看起來像進場但其實是大空頭反彈」的情況。這是維加斯通道作者強調
/// 的最重要心法：先辨識大環境再看進場。
///
/// 本策略預設使用經典參數（144/169/576/676/12），需要至少 676 根 K 線才完整計算。
/// 資料不足會回傳 hold + 明確說明。
/// </summary>
public class VegasTunnelStrategy : IStrategy
{
    public string Name => "vegas_tunnel";

    private const int MainFast  = 144;
    private const int MainSlow  = 169;
    private const int LongFast  = 576;
    private const int LongSlow  = 676;
    private const int Trigger   = 12;
    private const decimal MinTunnelWidthPct = 0.3m;   // 通道寬度 < 價格 0.3% 視為糾結，不進場

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < LongSlow)
            return Hold(config, $"Not enough data (need ≥ {LongSlow} bars, got {bars.Count})");

        var snap = VegasTunnel.Compute(bars, MainFast, MainSlow, LongFast, LongSlow, Trigger);
        if (snap == null)
            return Hold(config, "Vegas Tunnel computation returned null");

        var price = bars[^1].Close;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (snap.TunnelWidthPct < MinTunnelWidthPct)
        {
            reason = $"通道過窄（{snap.TunnelWidthPct:F2}%）— 趨勢未明，觀望";
        }
        else if (snap.MacroTrend == 0)
        {
            reason = "長短通道糾結 — 大趨勢不明顯，不進場";
        }
        else if (snap.MacroTrend == 1)
        {
            // 多頭環境
            if (snap.PriceZone == 0 && snap.TriggerCross == 1)
            {
                action = "buy";
                confidence = ScoreBull(snap, price);
                reason = $"多頭大趨勢 + 價格回檔主通道 + EMA12 上穿 — 順勢買進";
            }
            else if (snap.PriceZone == 1 && snap.TriggerCross == 1)
            {
                action = "buy";
                confidence = Math.Max(0.55m, ScoreBull(snap, price) - 0.1m);
                reason = $"多頭大趨勢 + 價格站回通道上方 + EMA12 上穿 — 延續買進（信心略低）";
            }
            else if (snap.PriceZone == -1)
            {
                reason = $"多頭環境但價格跌破主通道 — 結構受損，等待收回通道再看";
            }
            else
            {
                reason = $"多頭大趨勢（主通道在長通道上方），但尚未收到觸發訊號 — 等待 EMA12 穿越";
            }
        }
        else // MacroTrend == -1
        {
            // 空頭環境
            if (snap.PriceZone == 0 && snap.TriggerCross == -1)
            {
                action = "sell";
                confidence = ScoreBear(snap, price);
                reason = $"空頭大趨勢 + 價格反彈主通道 + EMA12 下穿 — 順勢賣出";
            }
            else if (snap.PriceZone == -1 && snap.TriggerCross == -1)
            {
                action = "sell";
                confidence = Math.Max(0.55m, ScoreBear(snap, price) - 0.1m);
                reason = $"空頭大趨勢 + 價格跌破通道下方 + EMA12 下穿 — 延續賣出";
            }
            else if (snap.PriceZone == 1)
            {
                reason = $"空頭環境但價格突破主通道 — 結構變化中，等待跌回通道再看";
            }
            else
            {
                reason = $"空頭大趨勢，但尚未收到觸發訊號 — 等待 EMA12 穿越";
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
            Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["price"]          = Math.Round(price, 4),
                ["ema_main_fast"]  = snap.MainFastEma,
                ["ema_main_slow"]  = snap.MainSlowEma,
                ["ema_long_fast"]  = snap.LongFastEma,
                ["ema_long_slow"]  = snap.LongSlowEma,
                ["ema_trigger"]    = snap.TriggerEma,
                ["tunnel_upper"]   = snap.TunnelUpper,
                ["tunnel_lower"]   = snap.TunnelLower,
                ["tunnel_width_pct"] = snap.TunnelWidthPct,
                ["macro_trend"]    = snap.MacroTrend,
                ["price_zone"]     = snap.PriceZone,
                ["trigger_cross"]  = snap.TriggerCross,
            }
        };
    }

    /// <summary>多頭信心：通道寬度越明顯 + 長通道支撐越遠 → 信心越高，最高 0.9。</summary>
    private static decimal ScoreBull(VegasTunnel.Snapshot s, decimal price)
    {
        var widthBonus = Math.Min(s.TunnelWidthPct / 10m, 0.15m);        // 寬度 3% → +0.15
        var supportGap = price == 0 ? 0m : (price - (s.LongFastEma + s.LongSlowEma) / 2m) / price;
        var supportBonus = Math.Min(Math.Max(supportGap, 0m) * 2m, 0.15m); // 價格高於長通道 7.5% → +0.15
        return Math.Clamp(0.65m + widthBonus + supportBonus, 0.5m, 0.9m);
    }

    private static decimal ScoreBear(VegasTunnel.Snapshot s, decimal price)
    {
        var widthBonus = Math.Min(s.TunnelWidthPct / 10m, 0.15m);
        var resistGap = price == 0 ? 0m : ((s.LongFastEma + s.LongSlowEma) / 2m - price) / price;
        var resistBonus = Math.Min(Math.Max(resistGap, 0m) * 2m, 0.15m);
        return Math.Clamp(0.65m + widthBonus + resistBonus, 0.5m, 0.9m);
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "vegas_tunnel",
        Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
