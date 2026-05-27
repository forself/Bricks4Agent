using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// HTF(Higher Timeframe)趨勢確認 wrapper strategy(2026-05-27 B 路線)。
///
/// 包裝任意 base strategy、用同 LTF bars 的長週期 EMA cross 當 trend filter:
///   - emaFast(default 20)cross above emaSlow(default 50) → trend "up"
///   - emaFast cross below emaSlow → trend "down"
///   - 等於或 ambiguous → trend "neutral"
///
/// 邏輯:
///   1. base.Evaluate(bars, cfg) 取 baseSig
///   2. baseSig.Action == "buy" 且 trend != "up" → 改為 "hold"(過濾掉 counter-trend buy)
///   3. baseSig.Action == "sell" 且 trend != "down" → 改為 "hold"(過濾掉 counter-trend sell)
///   4. 同向通過、reason 加 "[HTF:up]" / "[HTF:down]" 標記
///
/// 動機:
///   - param sweep(2026-05-27)證實 harm_prz_scan10 / widepz 已到參數極點
///   - 進一步 Sharpe 提升需換 mechanism、不是調參
///   - HTF confirmation 是業界最常見的「trend-with-trend」過濾、減少 fakeout
///
/// Tradeoff:
///   - 預期 ✓ Sharpe ↑ / WR ↑ / DD ↓(過濾品質低 trade)
///   - 預期 ✗ trade frequency ↓(因為 ~50% bar 可能 trend neutral)
///   - 若 wrap fail 過多、考慮 emaSlow 短一點(e.g., 30 而非 50)讓 trend 信號更頻繁
///
/// 不在範圍:
///   - 多時框分離 fetch(用同 LTF bars 算長 EMA 等價 HTF trend、避免額外資料依賴)
///   - 不是「真的」拉 4h / 12h bars、只是長 EMA 模擬 HTF 趨勢(實證等價、Lo / Hasanhodzic 2013)
/// </summary>
public sealed class HtfConfirmationStrategy : IStrategy
{
    private readonly IStrategy _base;
    private readonly int _fast;
    private readonly int _slow;
    private readonly string _name;

    public HtfConfirmationStrategy(IStrategy baseStrategy, int fastPeriod = 20, int slowPeriod = 50, string? name = null)
    {
        if (baseStrategy == null) throw new ArgumentNullException(nameof(baseStrategy));
        if (fastPeriod < 2) throw new ArgumentException("fastPeriod 至少 2", nameof(fastPeriod));
        if (slowPeriod <= fastPeriod) throw new ArgumentException("slowPeriod 必須大於 fastPeriod", nameof(slowPeriod));
        _base = baseStrategy;
        _fast = fastPeriod;
        _slow = slowPeriod;
        _name = name ?? $"htf_{baseStrategy.Name}_{fastPeriod}_{slowPeriod}";
    }

    public string Name => _name;
    public string Description => $"HTF EMA({_fast}/{_slow}) trend filter wrapping {_base.Name}";
    public StrategyCategory Category => _base.Category;
    public int MinBars => Math.Max(_base.MinBars, _slow + 5);
    public decimal MinCapitalUsdt => _base.MinCapitalUsdt;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var baseSig = _base.Evaluate(bars, config);
        if (baseSig.Action == "hold") return baseSig;
        if (bars.Count < _slow + 2)
        {
            // 不夠 bar 算 HTF trend、保守不過濾、直接 pass(避免 cold-start 一律 hold)
            return baseSig;
        }

        var emaFast = ComputeEmaLast(bars, _fast);
        var emaSlow = ComputeEmaLast(bars, _slow);
        var trend = emaFast > emaSlow ? "up" : (emaFast < emaSlow ? "down" : "neutral");

        bool block = (baseSig.Action == "buy" && trend != "up")
                   || (baseSig.Action == "sell" && trend != "down");
        if (block)
        {
            return new Signal
            {
                Action = "hold",
                Confidence = 0,
                Reason = $"[HTF:{trend}] blocked {baseSig.Action} (base: {baseSig.Reason})",
                TargetPrice = null,
                StopPrice = null,
            };
        }

        // 同向通過、保留原訊號、reason 標記
        return new Signal
        {
            Action = baseSig.Action,
            Confidence = baseSig.Confidence,
            Reason = $"[HTF:{trend}] {baseSig.Reason}",
            TargetPrice = baseSig.TargetPrice,
            StopPrice = baseSig.StopPrice,
        };
    }

    private static decimal ComputeEmaLast(List<BarData> bars, int period)
    {
        if (bars.Count < period) return bars[^1].Close;
        decimal k = 2m / (period + 1m);
        decimal ema = 0m;
        for (int i = 0; i < period; i++) ema += bars[i].Close;
        ema /= period;
        for (int i = period; i < bars.Count; i++)
        {
            ema = bars[i].Close * k + ema * (1m - k);
        }
        return ema;
    }
}
