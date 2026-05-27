using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Volume Momentum LS(2026-05-27 結構性 alpha 第二類)— funding_momentum_ls 變奏。
///
/// 假設(跟 funding momentum 同類):
///   交易量極端時 = 機構流入 / 散戶 FOMO,趨勢方向被推動、繼續(momentum continuation)
///   而非「成交量爆量 = 頂部訊號」(經典假設、實證不可靠)
///
/// 邏輯:
///   volume 在近期分布的「極高端」(成交量大爆量)+ 漲(close > open)→ BUY follow trend
///   volume 在「極高端」+ 跌(close < open)→ SELL follow trend
///   中性區 → hold
///
/// 跟 funding momentum 差別:
///   - funding 是 positioning(誰擁擠付錢)、structural
///   - volume 是 participation(誰在交易)、半 structural(可能被人發現後抄)
///
/// 預設用 xtight 閾值(0.05/0.95、selective)— funding 已實證 xtight 是最強區段
/// </summary>
public class VolumeMomentumLsStrategy : IStrategy
{
    private readonly string _name;
    private readonly decimal _volPct;        // 成交量百分位門檻、≥ 此 = 爆量
    private readonly int _lookback;

    public VolumeMomentumLsStrategy(string name = "volume_momentum_ls", decimal volPct = 0.85m, int lookback = 100)
    {
        _name = name;
        _volPct = volPct;
        _lookback = lookback;
    }

    public string Name => _name;
    public string Description => $"Volume Momentum LS — 成交量極高(percentile ≥ {_volPct:P0})且漲 → 跟多;爆量且跌 → 跟空(momentum follow、非 contrarian)";
    public StrategyCategory Category => StrategyCategory.Volume;
    public int MinBars => Math.Max(50, _lookback + 5);
    public decimal MinCapitalUsdt => 100m;

    public IReadOnlyDictionary<string, ParamSpec> ParamSchema => new Dictionary<string, ParamSpec>
    {
        ["volume_pct"] = new() { Type = "decimal", Default = 0.85m, Choices = new object[] { 0.75m, 0.85m, 0.90m, 0.95m }, Description = "爆量門檻(percentile)" },
        ["lookback"]   = new() { Type = "int",     Default = 100,    Choices = new object[] { 50, 100, 200 }, Description = "成交量歷史窗口" },
    };

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        if (bars.Count < MinBars) return Hold(config, $"Not enough data (need {MinBars}+ bars)");

        var volPct = config.GetParam("volume_pct", _volPct);
        var lookback = (int)config.GetParam("lookback", (decimal)_lookback);

        var slice = bars.TakeLast(lookback + 1).ToList();
        var current = slice[^1];
        var hist = slice.Take(slice.Count - 1).Select(b => b.Volume).OrderBy(v => v).ToList();
        if (hist.Count < 30) return Hold(config, "Not enough volume history");

        // current bar's volume percentile in lookback distribution
        int rank = hist.Count(v => v <= current.Volume);
        decimal pct = (decimal)rank / hist.Count;

        // direction: bar close vs open
        bool isUpBar = current.Close > current.Open;
        decimal bodyPct = current.Open > 0m ? Math.Abs(current.Close - current.Open) / current.Open : 0m;

        string action = "hold";
        decimal confidence = 0.5m;
        string reason;

        if (pct >= volPct && bodyPct > 0.002m)  // 爆量 + body >0.2%(避免 doji)
        {
            if (isUpBar)
            {
                action = "buy";
                confidence = Math.Clamp(0.6m + (pct - volPct), 0.5m, 0.9m);
                reason = $"成交量百分位 {pct:P0} ≥ {volPct:P0} + 漲 body {bodyPct:P2} = 機構流入跟多";
            }
            else
            {
                action = "sell";
                confidence = Math.Clamp(0.6m + (pct - volPct), 0.5m, 0.9m);
                reason = $"成交量百分位 {pct:P0} ≥ {volPct:P0} + 跌 body {bodyPct:P2} = 機構撤出跟空";
            }
        }
        else
        {
            reason = $"成交量百分位 {pct:P0} < {volPct:P0} 或 body 太小 — 觀望";
        }

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = _name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = new()
            {
                ["volume"]      = Math.Round(current.Volume, 4),
                ["volume_pct"]  = pct,
                ["body_pct"]    = Math.Round(bodyPct, 6),
                ["is_up_bar"]   = isUpBar ? 1m : 0m,
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
