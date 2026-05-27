using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// BTC macro regime filter wrapper(2026-05-27 C 路線發現)。
///
/// 跟 [[HtfConfirmationStrategy]] 不同:
///   HTF wrapper:濾「訊號方向 vs trend 方向」(同 trend 才開)— B 路線實證全策略負向、失敗
///   Regime wrapper:濾「策略 vs 當下 regime」(該策略最強 regime 才開)— C 路線實證正向
///
/// 假設:策略各有最強的 BTC macro regime(--validate-cross-asset 數據):
///   - scan10:    best in BTC up(Sharpe 1.31 vs avg 0.78)
///   - widepz:    best in BTC sideways(Sharpe 1.96 vs avg 1.22)
///   - tsmom:     best in BTC sideways(Sharpe 1.01 vs avg 0.66)
///
/// 邏輯:
///   1. caller 指定 allowedRegimes(e.g., ["up", "sideways"])
///   2. base.Evaluate 取得 baseSig
///   3. baseSig.Action == "hold" → pass(不做事)
///   4. 算當前 BTC regime(EMA20 vs EMA50、gap > ±2% 才算 up/down、否則 sideways)
///   5. 若 currentRegime 不在 allowedRegimes → 改 hold
///   6. 否則 pass through 原訊號
///
/// 限制:
///   - 需要 BTC bars 注入(透過 static)。production 部署需要從 quote-worker 抓
///   - regime 用同 LTF bars 計算、EMA 不是「真 4h/1w」regime;但實證足夠
///   - regime 切換有 lag(EMA cross 本質)、可能進入後立刻被 false signal 帶離
/// </summary>
public sealed class BtcRegimeFilterStrategy : IStrategy
{
    /// <summary>
    /// 由 backtest harness 在跑前注入 BTC 1d bars。Production 部署改成從 broker query 抓。
    /// 用 static 簡化、不擴 IStrategy interface。
    /// </summary>
    public static List<BarData>? BtcBarsRef { get; set; }

    private readonly IStrategy _base;
    private readonly HashSet<string> _allowedRegimes;
    private readonly int _fast;
    private readonly int _slow;
    private readonly decimal _thresholdPct;
    private readonly string _name;

    public BtcRegimeFilterStrategy(
        IStrategy baseStrategy,
        IEnumerable<string> allowedRegimes,
        int emaFast = 20, int emaSlow = 50,
        decimal regimeThresholdPct = 0.02m,
        string? name = null)
    {
        _base = baseStrategy ?? throw new ArgumentNullException(nameof(baseStrategy));
        _allowedRegimes = new HashSet<string>(allowedRegimes ?? throw new ArgumentNullException(nameof(allowedRegimes)),
            StringComparer.OrdinalIgnoreCase);
        if (_allowedRegimes.Count == 0) throw new ArgumentException("至少一個 allowed regime", nameof(allowedRegimes));
        _fast = emaFast;
        _slow = emaSlow;
        _thresholdPct = regimeThresholdPct;
        _name = name ?? $"btcreg_{baseStrategy.Name}_{string.Join("+", _allowedRegimes.OrderBy(x => x))}";
    }

    public string Name => _name;
    public string Description => $"BTC regime ({string.Join("/", _allowedRegimes)}) filter wrapping {_base.Name}";
    public StrategyCategory Category => _base.Category;
    public int MinBars => _base.MinBars;
    public decimal MinCapitalUsdt => _base.MinCapitalUsdt;

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        var baseSig = _base.Evaluate(bars, config);
        if (baseSig.Action == "hold") return baseSig;

        var btc = BtcBarsRef;
        if (btc == null || btc.Count < _slow + 2)
        {
            // 沒注入 BTC 或資料不足 → 保守:pass through 不過濾(避免無聲沉默)
            return baseSig;
        }

        // 找對應日期的 BTC bar
        var currentTs = bars[^1].OpenTime;
        int btcIdx = -1;
        for (int i = btc.Count - 1; i >= 0; i--)
            if (btc[i].OpenTime <= currentTs) { btcIdx = i; break; }
        if (btcIdx < _slow) return baseSig;

        // 算 BTC EMA(fast) / EMA(slow) at btcIdx
        var emaFast = ComputeEmaAt(btc, _fast, btcIdx);
        var emaSlow = ComputeEmaAt(btc, _slow, btcIdx);
        var gapPct = (emaFast - emaSlow) / emaSlow;
        var regime = gapPct > _thresholdPct ? "up"
                   : (gapPct < -_thresholdPct ? "down" : "sideways");

        if (!_allowedRegimes.Contains(regime))
        {
            return new Signal
            {
                Action = "hold",
                Confidence = 0,
                Reason = $"[BTCreg:{regime}] blocked {baseSig.Action} (not in [{string.Join(",", _allowedRegimes)}])",
            };
        }

        // 通過 — pass through、reason 標記
        return new Signal
        {
            Action = baseSig.Action,
            Confidence = baseSig.Confidence,
            Reason = $"[BTCreg:{regime}] {baseSig.Reason}",
            TargetPrice = baseSig.TargetPrice,
            StopPrice = baseSig.StopPrice,
        };
    }

    // EMA at specific index(不是只算最後一根、給 backtest 在歷史 bar 上用)
    private static decimal ComputeEmaAt(List<BarData> bars, int period, int idx)
    {
        if (idx < period - 1) return bars[idx].Close;
        decimal k = 2m / (period + 1m);
        decimal ema = 0m;
        for (int i = 0; i < period; i++) ema += bars[i].Close;
        ema /= period;
        for (int i = period; i <= idx; i++)
        {
            ema = bars[i].Close * k + ema * (1m - k);
        }
        return ema;
    }
}
