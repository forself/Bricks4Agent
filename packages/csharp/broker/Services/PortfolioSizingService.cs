using Microsoft.Extensions.Logging;

namespace Broker.Services;

// Inline math helpers(避免 broker 引 strategy-worker、循環依賴)
// 跟 packages/csharp/workers/strategy-worker/Engine/{Kelly,VolTarget,DrawdownAware}Sizer.cs 對齊、
// 邏輯一致、各自獨立小、不抽 shared lib 過度設計
internal static class _Math
{
    public static decimal KellyCompute(decimal winRate, decimal avgWin, decimal avgLoss)
    {
        if (winRate <= 0m || winRate >= 1m) return 0m;
        if (avgWin <= 0m || avgLoss <= 0m) return 0m;
        decimal b = avgWin / avgLoss;
        decimal kelly = (b * winRate - (1m - winRate)) / b;
        return Math.Max(0m, kelly);
    }

    public static decimal KellySafe(decimal winRate, decimal avgWin, decimal avgLoss, decimal fraction = 0.25m, decimal maxPct = 0.20m)
    {
        var raw = KellyCompute(winRate, avgWin, avgLoss);
        return Math.Clamp(raw * fraction, 0m, maxPct);
    }

    public static decimal AnnualizedRealizedVol(List<decimal> closes, int lookback = 30)
    {
        if (closes == null || closes.Count < lookback + 1) return 0m;
        var slice = closes.TakeLast(lookback + 1).ToList();
        var returns = new List<double>();
        for (int i = 1; i < slice.Count; i++)
        {
            if (slice[i - 1] <= 0m) continue;
            returns.Add(Math.Log((double)(slice[i] / slice[i - 1])));
        }
        if (returns.Count < 5) return 0m;
        double mean = returns.Average();
        double var_ = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        return (decimal)(Math.Sqrt(var_) * Math.Sqrt(365));
    }

    public static decimal VolScalar(decimal realized, decimal target, decimal min = 0.3m, decimal max = 2.0m)
    {
        if (realized <= 0m || target <= 0m) return 1m;
        return Math.Clamp(target / realized, min, max);
    }

    public static decimal DdAwareScalar(decimal currentDd, decimal maxDd, decimal power = 2m)
    {
        if (maxDd <= 0m || currentDd <= 0m) return 1m;
        decimal frac = 1m - currentDd / maxDd;
        if (frac <= 0m) return 0m;
        return (decimal)Math.Pow((double)frac, (double)power);
    }
}

/// <summary>
/// Portfolio Sizing 推薦服務(2026-05-27 Q1.6、Roadmap Q1.6)。
///
/// 組織 4 個 helper 給出 runtime sizing 推薦:
///   - KellyPositionSizer:per-strategy 該配多少 %
///   - VolTargetSizer:整體時間維度縮放
///   - RiskParityOptimizer (ERC):多策略空間維度分配
///   - DrawdownAwareSizer:當下 DD 決定路徑縮放
///
/// **MVP 階段策略**(可演進):
/// - 用 hardcoded backtest stats(從 strat-validate --apply-funding 跑出的)
/// - 等 shadow 累積夠多 closed legs(>=20)再切到 live stats
/// - 只 recommend、不自動 apply(避免誤動真錢)、user/admin 看完手動 SQL UPDATE
///
/// 完整 sizing 公式:
///   final_pct = ERC_weight × VolTarget_scalar × DDAware_scalar
///   final_usdt = final_pct × total_equity
///
/// 為什麼 Kelly 不直接相乘?ERC 已隱含 Kelly intuition(風險均攤、vol 高的策略 weight 自然小)。
/// Kelly 是 single-strategy 視角、ERC 是 portfolio 視角、不重複計算。
/// </summary>
public class PortfolioSizingService
{
    private readonly ILogger<PortfolioSizingService> _logger;

    public PortfolioSizingService(ILogger<PortfolioSizingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 8 個 scanner 對應策略的 backtest stats(2026-05-27 strat-validate --apply-funding 跑出)
    /// 之後可改成動態查 scanner_active_legs 統計、或從 t-stat re-run 自動更新
    /// </summary>
    private static readonly Dictionary<string, StratStats> BacktestStats = new()
    {
        // (key = strategy name; values from strat-validate --apply-funding output, 2026-05-27)
        ["harm_prz_scan10"]               = new(WinRate: 0.42m, AvgWinPct: 26.8m, AvgLossPct: 4.1m,  MeanRet: 10.6m, Vol: 30.0m),
        ["harm_prz_scan10_widepz"]        = new(WinRate: 0.50m, AvgWinPct: 35.2m, AvgLossPct: 4.9m,  MeanRet: 17.0m, Vol: 30.0m),
        ["harm_prz_top2_scan10_widepz"]   = new(WinRate: 0.48m, AvgWinPct: 28.0m, AvgLossPct: 7.0m,  MeanRet: 12.4m, Vol: 28.0m),
        ["ts_momentum"]                   = new(WinRate: 0.58m, AvgWinPct: 37.4m, AvgLossPct: 22.7m, MeanRet: 12.4m, Vol: 35.0m),
        ["tsmom_widepz"]                  = new(WinRate: 0.54m, AvgWinPct: 38.0m, AvgLossPct: 18.0m, MeanRet: 14.4m, Vol: 32.0m),
        ["decorr5_scan10"]                = new(WinRate: 0.49m, AvgWinPct: 28.5m, AvgLossPct: 13.0m, MeanRet: 9.8m,  Vol: 28.0m),
        ["funding_momentum_ls"]           = new(WinRate: 0.52m, AvgWinPct: 42.5m, AvgLossPct: 23.8m, MeanRet: 10.7m, Vol: 33.0m),
        ["fundmom_ls_xtight"]             = new(WinRate: 0.62m, AvgWinPct: 39.8m, AvgLossPct: 18.7m, MeanRet: 17.4m, Vol: 33.0m),
        ["tsmom_btc_not_up"]              = new(WinRate: 0.55m, AvgWinPct: 35.0m, AvgLossPct: 22.0m, MeanRet: 12.0m, Vol: 34.0m),
    };

    public sealed record StratStats(decimal WinRate, decimal AvgWinPct, decimal AvgLossPct, decimal MeanRet, decimal Vol);

    public sealed record ScannerRecommendation(
        string ScannerId, string Strategy,
        decimal KellyRawPct, decimal KellySafePct,
        decimal VolScalar,
        decimal ErcWeight,
        decimal DdScalar,
        decimal FinalPct, decimal FinalUsdt,
        string Rationale);

    public sealed record SizingResponse(
        decimal TotalEquity, decimal BtcRealizedVol, decimal CurrentDd,
        decimal VolScalar, decimal DdScalar,
        decimal TotalFinalPct, decimal TotalFinalUsdt, decimal CashBuffer,
        List<ScannerRecommendation> Scanners,
        List<string> Warnings);

    /// <summary>
    /// 主推薦函式 — 給 scanner list + 當下市況 → 推薦 sizing
    /// </summary>
    public SizingResponse Recommend(
        List<(string ScannerId, string Strategy, decimal CurrentBudget)> scanners,
        decimal totalEquityUsdt,
        List<decimal>? btcDailyCloses = null,
        decimal currentDdPct = 0m,
        decimal targetVolPct = 0.60m,
        decimal maxAcceptableDdPct = 0.20m,
        decimal maxCapPerStrategy = 0.20m)
    {
        var warnings = new List<string>();

        // 1. Vol scalar(用 BTC 30 天 realized vol vs target)
        decimal btcVol = btcDailyCloses?.Count >= 31
            ? _Math.AnnualizedRealizedVol(btcDailyCloses, 30)
            : 0m;
        decimal volScalar = btcVol > 0m
            ? _Math.VolScalar(btcVol, targetVolPct, min: 0.3m, max: 2.0m)
            : 1m;
        if (btcVol <= 0m) warnings.Add("BTC bars 不足、vol_scalar = 1.0(無 vol-target 調整)");

        // 2. DD scalar(從當前 DD%)
        decimal ddScalar = _Math.DdAwareScalar(currentDdPct, maxAcceptableDdPct, power: 2m);
        if (currentDdPct >= maxAcceptableDdPct)
            warnings.Add($"當前 DD {currentDdPct:P0} 已達 max {maxAcceptableDdPct:P0}、所有 scanner final size = 0(全停)");

        // 3. ERC weights(用 hardcoded backtest vol/cov、簡化版用 inverse-vol)
        var statsList = new List<(string id, string strat, StratStats? stats)>();
        foreach (var (id, strat, _) in scanners)
            statsList.Add((id, strat, BacktestStats.TryGetValue(strat, out var s) ? s : null));
        var validScanners = statsList.Where(x => x.stats != null).ToList();
        if (validScanners.Count == 0)
        {
            warnings.Add("無 scanner 有 backtest stats、無法計算 ERC");
            return new SizingResponse(totalEquityUsdt, btcVol, currentDdPct, volScalar, ddScalar,
                0m, 0m, totalEquityUsdt, new(), warnings);
        }

        // 用 inverse-vol 當 ERC 近似(scanner 間 cov 沒存、用 stand-alone vol 近似)
        // 嚴格 ERC 需 cov matrix、留 future enhancement
        decimal totalInvVol = 0m;
        foreach (var v in validScanners) totalInvVol += 1m / v.stats!.Vol;
        var ercRaw = new Dictionary<string, decimal>();
        foreach (var v in validScanners) ercRaw[v.id] = (1m / v.stats!.Vol) / totalInvVol;
        // Apply max cap iteratively
        for (int iter = 0; iter < 10; iter++)
        {
            bool changed = false;
            decimal excess = 0m;
            foreach (var id in ercRaw.Keys.ToList())
            {
                if (ercRaw[id] > maxCapPerStrategy)
                {
                    excess += ercRaw[id] - maxCapPerStrategy;
                    ercRaw[id] = maxCapPerStrategy;
                    changed = true;
                }
            }
            if (!changed || excess <= 0m) break;
            decimal freeSum = ercRaw.Values.Where(w => w < maxCapPerStrategy && w > 0m).Sum();
            if (freeSum <= 0m) break;
            foreach (var id in ercRaw.Keys.ToList())
                if (ercRaw[id] < maxCapPerStrategy && ercRaw[id] > 0m)
                    ercRaw[id] += excess * ercRaw[id] / freeSum;
        }

        // 4. Per-scanner Kelly + final compute
        var recommendations = new List<ScannerRecommendation>();
        decimal totalFinalPct = 0m;
        foreach (var (id, strat, currentBudget) in scanners)
        {
            var sTuple = statsList.FirstOrDefault(x => x.id == id);
            if (sTuple.stats == null)
            {
                recommendations.Add(new ScannerRecommendation(id, strat, 0, 0, volScalar, 0, ddScalar, 0, 0,
                    $"❌ no backtest stats for {strat}"));
                continue;
            }
            var stats = sTuple.stats;
            decimal kellyRaw = _Math.KellyCompute(stats.WinRate, stats.AvgWinPct, stats.AvgLossPct);
            decimal kellySafe = _Math.KellySafe(stats.WinRate, stats.AvgWinPct, stats.AvgLossPct,
                fraction: 0.25m, maxPct: maxCapPerStrategy);
            decimal ercWeight = ercRaw.GetValueOrDefault(id, 0m);
            decimal finalPct = ercWeight * volScalar * ddScalar;
            decimal finalUsdt = finalPct * totalEquityUsdt;
            totalFinalPct += finalPct;

            string rationale = $"Kelly raw {kellyRaw:P1} / safe {kellySafe:P1} / ERC {ercWeight:P1} × vol {volScalar:F2} × DD {ddScalar:F2} = {finalPct:P1}";
            recommendations.Add(new ScannerRecommendation(id, strat,
                Math.Round(kellyRaw * 100m, 1), Math.Round(kellySafe * 100m, 1),
                Math.Round(volScalar, 2),
                Math.Round(ercWeight * 100m, 1),
                Math.Round(ddScalar, 2),
                Math.Round(finalPct * 100m, 1),
                Math.Round(finalUsdt, 2),
                rationale));
        }

        decimal totalUsdt = totalFinalPct * totalEquityUsdt;
        decimal cashBuffer = totalEquityUsdt - totalUsdt;
        if (totalFinalPct > 1m)
            warnings.Add($"⚠ 總配重 {totalFinalPct:P0} > 100% — 須 scale down 或減 max cap");
        if (cashBuffer < totalEquityUsdt * 0.10m)
            warnings.Add($"⚠ 現金緩衝 {cashBuffer:F2} USDT 過低(< 10% equity)、考慮降 max cap");

        return new SizingResponse(
            TotalEquity: totalEquityUsdt,
            BtcRealizedVol: Math.Round(btcVol, 3),
            CurrentDd: Math.Round(currentDdPct, 3),
            VolScalar: Math.Round(volScalar, 2),
            DdScalar: Math.Round(ddScalar, 2),
            TotalFinalPct: Math.Round(totalFinalPct * 100m, 1),
            TotalFinalUsdt: Math.Round(totalUsdt, 2),
            CashBuffer: Math.Round(cashBuffer, 2),
            Scanners: recommendations,
            Warnings: warnings);
    }
}
