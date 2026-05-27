namespace StrategyWorker.Engine;

/// <summary>
/// Risk Parity / Equal Risk Contribution (ERC) optimizer(2026-05-27 Q1.4、Roadmap Q1.4)。
///
/// 跟 min-variance 的差別:
///   - **min-variance**:總風險最小(常選 1-2 支低 vol 策略、其他丟掉、過度集中)
///   - **risk parity (ERC)**:每支策略對總風險貢獻一樣(forced diversification、所有策略都用)
///   - **inverse-vol(naive RP)**:weight ∝ 1/vol(忽略 correlation、不夠精確)
///
/// 業界用 ERC 原因(Bridgewater All-Weather、AQR、Roncalli《Introduction to Risk Parity》):
///   1. **不需估報酬**(跟 min-var 一樣、避 μ-sensitive bias)
///   2. **strategy 不被丟棄**(diversification 強制)
///   3. **單一策略爆掉影響受限**(risk contribution 上限自然就低)
///   4. **動態自動 rebalance**(高 vol 期該策略 contribution 變大、weight 自動降)
///
/// 算法:Maillard, Roncalli, Teïletche (2010) iterative cyclical coordinate descent。
/// 簡化版:每 iteration 計算每支 MRC(marginal risk contribution)、調整 w 讓 RC 拉平。
///
/// 公式:
///   RC_i = w_i × (Σw)_i / sqrt(wᵀΣw)   (策略 i 的風險貢獻)
///   ERC 目標:RC_i = total_risk / N(每支貢獻一樣)
///   每 iter:w_i_new = w_i × (target_RC / RC_i)^η,η = 0.5(緩慢調整、避震盪)
/// </summary>
public static class RiskParityOptimizer
{
    /// <summary>
    /// Equal Risk Contribution weights — iterative algorithm。
    /// 初始 = inverse-vol weights、每 iter 調整、最多 100 iter 或 收斂 tol 1e-6 停。
    /// 加 max cap 防單腿過大(預設 35%)。
    /// </summary>
    public static decimal[] ErcWeights(double[,] cov, decimal maxWeight = 0.35m, int maxIter = 100, double tol = 1e-6)
    {
        int n = cov.GetLength(0);
        if (n == 0) return Array.Empty<decimal>();
        if (n == 1) return new[] { 1m };

        // 初始用 inverse-vol(快收斂的起點、本身已是 naive RP)
        var w = new double[n];
        double wSum = 0;
        for (int i = 0; i < n; i++)
        {
            double vol = Math.Sqrt(Math.Max(cov[i, i], 1e-12));
            w[i] = 1.0 / vol;
            wSum += w[i];
        }
        for (int i = 0; i < n; i++) w[i] /= wSum;

        // Iterative ERC
        for (int iter = 0; iter < maxIter; iter++)
        {
            // Σw vector
            var sigmaW = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    sigmaW[i] += cov[i, j] * w[j];

            // portfolio variance & total risk
            double pv = 0;
            for (int i = 0; i < n; i++) pv += w[i] * sigmaW[i];
            double sigma_p = Math.Sqrt(Math.Max(pv, 1e-12));

            // Risk contribution per strategy:RC_i = w_i × sigmaW_i / sigma_p
            var rc = new double[n];
            double sumRc = 0;
            for (int i = 0; i < n; i++)
            {
                rc[i] = w[i] * sigmaW[i] / sigma_p;
                sumRc += rc[i];
            }
            double targetRc = sumRc / n;

            // 檢查 convergence:max | RC - target_RC | / target_RC < tol
            double maxDev = 0;
            for (int i = 0; i < n; i++)
                maxDev = Math.Max(maxDev, Math.Abs(rc[i] - targetRc) / Math.Max(targetRc, 1e-12));
            if (maxDev < tol) break;

            // 調整 w:w_new = w × (target / current)^0.5(緩慢)
            double newSum = 0;
            for (int i = 0; i < n; i++)
            {
                if (rc[i] <= 0) continue;
                w[i] *= Math.Pow(targetRc / rc[i], 0.5);
                newSum += w[i];
            }
            if (newSum > 0)
                for (int i = 0; i < n; i++) w[i] /= newSum;
        }

        // 套 max cap(iterative water-filling、同 min-variance)
        var weights = new decimal[n];
        for (int i = 0; i < n; i++) weights[i] = (decimal)w[i];
        for (int iter = 0; iter < 10; iter++)
        {
            bool changed = false;
            decimal excess = 0m;
            for (int i = 0; i < n; i++)
            {
                if (weights[i] > maxWeight)
                {
                    excess += weights[i] - maxWeight;
                    weights[i] = maxWeight;
                    changed = true;
                }
            }
            if (!changed || excess <= 0m) break;
            decimal freeSum = 0m;
            for (int i = 0; i < n; i++) if (weights[i] < maxWeight && weights[i] > 0m) freeSum += weights[i];
            if (freeSum <= 0m) break;
            for (int i = 0; i < n; i++)
                if (weights[i] < maxWeight && weights[i] > 0m)
                    weights[i] += excess * weights[i] / freeSum;
        }
        return weights;
    }

    /// <summary>
    /// Risk contribution diagnostic — 算每支策略對 portfolio 總風險的實際貢獻 %
    /// 用於驗證 ERC 是否 work(理想:所有 RC% 接近 1/N)
    /// </summary>
    public static decimal[] RiskContributions(decimal[] weights, double[,] cov)
    {
        int n = weights.Length;
        if (n == 0 || cov.GetLength(0) != n) return Array.Empty<decimal>();

        var w = weights.Select(x => (double)x).ToArray();
        var sigmaW = new double[n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                sigmaW[i] += cov[i, j] * w[j];

        double pv = 0;
        for (int i = 0; i < n; i++) pv += w[i] * sigmaW[i];
        double sigma_p = Math.Sqrt(Math.Max(pv, 1e-12));

        var rcPct = new decimal[n];
        for (int i = 0; i < n; i++)
        {
            double rc = w[i] * sigmaW[i] / sigma_p;
            rcPct[i] = (decimal)(rc / sigma_p);   // 標準化成 % of total risk
        }
        return rcPct;
    }
}
