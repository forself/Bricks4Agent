using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 波動率管理的時間序列動量(Vol-Managed Time-Series Momentum)——
/// 底層是 Moskowitz-Ooi-Pedersen(2012)的絕對動量(資產對「自己過去 N 根報酬」做趨勢跟隨),
/// 外加 Moreira-Muir(2017)的波動率管理:**波動率在自己歷史的高分位時不開新倉**。
/// 學術上 vol-managed 版本的 Sharpe 通常優於裸動量——高波動期的趨勢追進最容易被甩。
///
/// 跟既有 sma_cross / macd 的差別:看的是中期(預設 60 根 ≈ 一季)「絕對報酬正負」,
/// 而且報酬先除以已實現波動率標準化(z),再用 ATR 百分位閘門擋掉高波動進場 →
/// 進出時點跟短週期交叉/突破型錯開、且只在「波動可控」時承擔趨勢風險(survival-first)。
///
/// 訊號語意(多空對稱):z≥+entryZ 且波動可控 buy(做多)、z≤−entryZ 做空(sell)、其餘 hold。
/// 在 long-only 引擎 sell=平倉、在 LongShortBacktestEngine sell=反手做空。
/// 部位大小(真正的 vol-targeting sizing)交給 risk 層(RISK_MAX_LOSS_PER_TRADE_PCT 等),
/// 策略只負責「方向 + 何時該不該在場」。無 lookahead:ROC / ATR 百分位皆純回看、已測。
/// </summary>
public class TsMomentumStrategy : IStrategy
{
    public string Name => "ts_momentum";
    public string Description => "Vol-Managed TS Momentum — 中期絕對動量 + 高波動百分位不進場";
    public StrategyCategory Category => StrategyCategory.Trend;
    public int MinBars => 120;
    public decimal MinCapitalUsdt => 100m;

    private const int Lookback   = 60;   // 動量回看根數(≈ 一季)
    private const int VolWindow  = 30;   // 已實現波動率估計窗口(標準化用)
    private const int AtrPeriod  = 14;   // 波動率閘門:ATR 週期
    private const int VolPctLookback = 100; // 波動率閘門:ATR 百分位回看窗
    private const decimal EntryZ     = 0.5m; // |標準化動量| ≥ 此值才開倉(多/空對稱)
    private const decimal VolCeiling = 0.9m; // ATR 百分位 > 此值 → 高波動、不開新倉

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["mom_lookback"]    = new() { Type = "int",     Default = Lookback,   Min = 30,   Max = 120,  Step = 10,  Description = "動量回看根數" },
        ["mom_vol_window"]  = new() { Type = "int",     Default = VolWindow,  Min = 14,   Max = 60,   Step = 7,   Description = "標準化用的已實現波動率窗口" },
        ["mom_entry_z"]     = new() { Type = "decimal", Default = EntryZ,     Min = 0.2m, Max = 1.5m, Step = 0.1m, Description = "進場標準化動量門檻" },
        ["mom_vol_ceiling"] = new() { Type = "decimal", Default = VolCeiling, Min = 0.6m, Max = 1.0m, Step = 0.05m, Description = "ATR 百分位上限(超過不開新倉)" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        int lookback = config.GetParam("mom_lookback", Lookback);
        int volWin   = config.GetParam("mom_vol_window", VolWindow);
        decimal entryZ    = config.GetParam("mom_entry_z", EntryZ);
        decimal volCeiling = config.GetParam("mom_vol_ceiling", VolCeiling);

        int need = Math.Max(Math.Max(lookback, volWin) + 1, AtrPeriod + VolPctLookback);
        if (bars.Count < need) return Hold(config, $"資料不足(需 {need}+ 根)");

        var roc = Roc.Compute(bars, lookback);        // 期間報酬 %(無 lookahead、已測)
        if (roc == null) return Hold(config, "ROC 無法計算");

        decimal volPerBar = RealizedVolPct(bars, volWin);  // 近 volWin 根單根報酬標準差(%)
        if (volPerBar <= 0m) return Hold(config, "波動率為 0、無法標準化");

        // 把單根波動率放大到 lookback 期(√t 法則),當「期間報酬的尺度」做標準化
        decimal horizonVol = volPerBar * (decimal)Math.Sqrt(lookback);
        decimal z = roc.Value / horizonVol;

        // 波動率閘門:ATR 在自己過去 VolPctLookback 根裡的百分位(0..1);算不出就視為低波動、不擋
        var vr = VolatilityRegime.Compute(bars, AtrPeriod, VolPctLookback);
        decimal volPct = vr?.Percentile ?? 0m;
        bool volBlocked = vr != null && volPct > volCeiling;

        // 對稱多空:動量 ≥ +entryZ 做多、≤ −entryZ 做空、中性帶維持。高波動則兩邊都不開新倉。
        bool strongLong = z >= entryZ;
        bool strongShort = z <= -entryZ;

        string action; decimal confidence; string reason;
        if ((strongLong || strongShort) && volBlocked)
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"動量 z={z:F2} 達標但 ATR 百分位 {volPct:P0} > {volCeiling:P0} — 高波動不開新倉";
        }
        else if (strongLong)
        {
            action = "buy";
            confidence = Math.Clamp(0.6m + (z - entryZ) * 0.3m, 0.6m, 0.95m);
            reason = $"標準化中期動量 z={z:F2} ≥ {entryZ}、ATR 百分位 {volPct:P0} 可控 — 做多";
        }
        else if (strongShort)
        {
            action = "sell";
            confidence = Math.Clamp(0.6m + (-z - entryZ) * 0.3m, 0.6m, 0.95m);
            reason = $"標準化中期動量 z={z:F2} ≤ −{entryZ}、ATR 百分位 {volPct:P0} 可控 — 做空";
        }
        else
        {
            action = "hold";
            confidence = 0.5m;
            reason = $"標準化動量 z={z:F2} 在 (−{entryZ}, {entryZ}) 中性帶 — 維持";
        }

        return new Signal
        {
            SignalId = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy = Name, Symbol = config.Symbol, Exchange = config.Exchange,
            Action = action, Confidence = Math.Round(confidence, 2), Reason = reason,
            Interval = config.Interval,
            Indicators = new()
            {
                ["roc"]              = roc.Value,
                ["realized_vol_pct"] = Math.Round(volPerBar, 4),
                ["mom_z"]            = Math.Round(z, 4),
                ["atr_percentile"]   = Math.Round(volPct, 4),
                ["price"]            = Math.Round(bars[^1].Close, 4),
            },
        };
    }

    /// <summary>近 window 根「單根簡單報酬」的標準差(百分比);只用回看資料、無 lookahead。</summary>
    private static decimal RealizedVolPct(List<BarData> bars, int window)
    {
        if (bars.Count < window + 1) return 0m;
        var rets = new List<decimal>(window);
        for (int i = bars.Count - window; i < bars.Count; i++)
        {
            var prev = bars[i - 1].Close;
            if (prev > 0m) rets.Add((bars[i].Close - prev) / prev * 100m);
        }
        if (rets.Count < 2) return 0m;
        var avg = rets.Average();
        var variance = rets.Select(r => (r - avg) * (r - avg)).Average();
        return (decimal)Math.Sqrt((double)variance);
    }

    private static Signal Hold(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "ts_momentum", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
