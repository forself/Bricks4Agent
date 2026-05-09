using StrategyWorker.Engine.Indicators;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// Regime-aware meta-strategy——先用 RegimeDetector 判斷當下行情類型，
/// 再從 regime → constituent map 挑出**單一**策略執行。
///
/// 跟 ensemble 的差異：
///   - ensemble = 全部策略都跑、依近期 Sharpe 加權投票（backward-looking、換 regime 慢）
///   - auto_select = 依當下指標挑 1 個策略跑（forward-looking、換 regime 即時）
///
/// 預設對照（建構時可覆蓋）：
///   - TrendingUp   → vegas_tunnel  （EMA 階梯抓強趨勢）
///   - TrendingDown → sma_cross     （死叉訊號清晰）
///   - RangeBound   → rsi_oversold  （震盪市靠均值回歸）
///   - Squeeze      → bollinger_bands（收斂後抓爆破方向）
///   - HighVol      → multi_timeframe（高波動時跨時段確認、避免雜訊）
///   - Unclear      → composite     （回退到廣譜投票）
///
/// 訊號的 reason 會寫「[regime: trending_up, slope=+2.3%, atr=1.2%] → vegas_tunnel: ...」
/// 給下游（dashboard / auto-trader log）看得到挑選依據。
/// </summary>
public class AutoSelectStrategy : IStrategy
{
    public string Name => "auto_select";
    public string Description => "AutoSelect — 偵測行情類型（趨勢/震盪/收斂/高波動）→ 挑當下最適合的單一策略";
    public StrategyCategory Category => StrategyCategory.Composite;
    public int MinBars => 50;
    public decimal MinCapitalUsdt => 100m;

    private readonly Dictionary<RegimeDetector.RegimeType, IStrategy> _regimeMap;
    private readonly IStrategy _fallback;

    public AutoSelectStrategy(
        Dictionary<RegimeDetector.RegimeType, IStrategy> regimeMap,
        IStrategy fallback)
    {
        if (regimeMap == null || regimeMap.Count == 0)
            throw new ArgumentException("AutoSelectStrategy 需要至少 1 個 regime mapping");
        _regimeMap = regimeMap;
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <summary>從一組已註冊策略建出預設對照表。缺哪個就回 fallback。</summary>
    public static AutoSelectStrategy DefaultFrom(IReadOnlyDictionary<string, IStrategy> registered)
    {
        IStrategy Get(string key, IStrategy fb) => registered.TryGetValue(key, out var s) ? s : fb;
        var composite = registered.TryGetValue("composite", out var c)
            ? c
            : throw new ArgumentException("AutoSelectStrategy.DefaultFrom 至少需要 composite 作 fallback");

        return new AutoSelectStrategy(
            new Dictionary<RegimeDetector.RegimeType, IStrategy>
            {
                [RegimeDetector.RegimeType.TrendingUp]   = Get("vegas_tunnel", composite),
                [RegimeDetector.RegimeType.TrendingDown] = Get("sma_cross",     composite),
                [RegimeDetector.RegimeType.RangeBound]   = Get("rsi_oversold",  composite),
                [RegimeDetector.RegimeType.Squeeze]      = Get("bollinger_bands", composite),
                [RegimeDetector.RegimeType.HighVol]      = Get("multi_timeframe", composite),
                [RegimeDetector.RegimeType.Unclear]      = composite,
            },
            fallback: composite);
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var regime = RegimeDetector.Detect(bars);
        var picked = _regimeMap.GetValueOrDefault(regime.Type) ?? _fallback;

        var sig = picked.Evaluate(bars, config);

        // Re-brand 訊號：用 auto_select 名稱、保留實際跑的成員 reason
        var reason = $"[regime:{regime.Type} {regime.Description}] → {picked.Name}: {sig.Reason}";

        // 把 regime 指標一起塞進 indicators 給 dashboard 顯示
        var indicators = new Dictionary<string, decimal>(sig.Indicators)
        {
            ["regime.type"]          = (decimal)(int)regime.Type,
            ["regime.sma50_slope"]   = regime.Sma50Slope,
            ["regime.atr_pct"]       = regime.AtrPct,
            ["regime.bb_width"]      = regime.BbWidth,
            ["regime.above_sma50"]   = regime.AboveSma50 ? 1m : 0m,
        };

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = sig.Action,
            Confidence = sig.Confidence,
            Reason     = reason,
            Interval   = config.Interval,
            Indicators = indicators,
        };
    }
}
