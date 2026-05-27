namespace StrategyWorker.Engine;

/// <summary>
/// Mean-variance portfolio optimization(2026-05-27 Q1.3、Roadmap Q1.3)。
///
/// 提供 3 種配重法:
///   1. **Min-variance**:w* = Σ⁻¹·1 / (1ᵀΣ⁻¹·1)、不需報酬預期、最 robust
///   2. **Max-Sharpe**:w* = Σ⁻¹·μ / (1ᵀΣ⁻¹·μ)、需報酬預期、易被 μ 雜訊主導
///   3. **Risk-parity (ERC)**:每支策略對 portfolio 風險貢獻一樣、進階版反波動率
///
/// 業界教訓(Carver Ch 11、López de Prado):
///   - Max-Sharpe 對 expected return 估計超敏感、實務常給極端配重(95% 一支)
///   - Min-variance 更穩、不需估報酬、雖然「保守」但 robust
///   - **建議用 min-variance + 加 max-weight clamp(20-30%)避免單腿過大**
///
/// 公式核心:cov matrix + Gauss-Jordan inversion + analytical solver
/// 對 N ≤ 20 支策略、Gauss-Jordan 夠快(O(N³)、20³ = 8000 次操作)
///
/// 跟 KellyPositionSizer + VolTargetSizer 互補:
///   - Kelly:per-strategy 該配多少(獨立看)
///   - Vol-target:整體該縮放多少(時間維度)
///   - **Mean-variance:strategy 之間關係怎麼配**(空間維度)← 這支
/// </summary>
public static class MinVarianceOptimizer
{
    /// <summary>
    /// 從 K 支策略 × N 個 fold returns 算 covariance matrix。
    /// 各序列必須等長(若不等、取最短 + 對齊到尾部、模擬「最近 N 期共同」)。
    /// </summary>
    public static double[,] Covariance(List<List<decimal>> returns)
    {
        int K = returns.Count;
        if (K == 0) return new double[0, 0];
        int N = returns.Min(r => r.Count);
        if (N < 2) return new double[K, K];

        // 對齊到尾部 N 期(若某些序列較長、取最後 N 個)
        var aligned = returns.Select(r => r.TakeLast(N).Select(d => (double)d).ToArray()).ToList();

        // 各序列均值
        var means = aligned.Select(a => a.Average()).ToArray();

        // 共變異數矩陣
        var cov = new double[K, K];
        for (int i = 0; i < K; i++)
        {
            for (int j = i; j < K; j++)
            {
                double sum = 0;
                for (int t = 0; t < N; t++)
                    sum += (aligned[i][t] - means[i]) * (aligned[j][t] - means[j]);
                double c = sum / (N - 1);
                cov[i, j] = c;
                cov[j, i] = c;
            }
        }
        return cov;
    }

    /// <summary>
    /// Gauss-Jordan 矩陣求反。N ≤ 20 夠快。
    /// 失敗(奇異矩陣)→ 回 null(caller 用 fallback 例如 equal-weight)
    /// </summary>
    public static double[,]? Inverse(double[,] m)
    {
        int n = m.GetLength(0);
        if (n == 0 || n != m.GetLength(1)) return null;

        // 增廣矩陣 [M | I]
        var a = new double[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) a[i, j] = m[i, j];
            a[i, n + i] = 1.0;
        }

        // 加 ridge regularization 避免奇異(尤其高度共線情況)
        // λ = max(diagonal) × 1e-8、業界 Ledoit-Wolf 風格小 shrinkage
        double maxDiag = 0;
        for (int i = 0; i < n; i++) maxDiag = Math.Max(maxDiag, Math.Abs(a[i, i]));
        double ridge = maxDiag * 1e-8;
        for (int i = 0; i < n; i++) a[i, i] += ridge;

        // Gauss-Jordan elimination
        for (int i = 0; i < n; i++)
        {
            // 部分 pivoting:找絕對值最大的 row 換上來
            int maxRow = i;
            for (int k = i + 1; k < n; k++)
                if (Math.Abs(a[k, i]) > Math.Abs(a[maxRow, i])) maxRow = k;
            if (maxRow != i)
                for (int j = 0; j < 2 * n; j++)
                    (a[i, j], a[maxRow, j]) = (a[maxRow, j], a[i, j]);

            double pivot = a[i, i];
            if (Math.Abs(pivot) < 1e-15) return null;   // 奇異

            // 規整 pivot row
            for (int j = 0; j < 2 * n; j++) a[i, j] /= pivot;

            // 消去其他 rows
            for (int k = 0; k < n; k++)
            {
                if (k == i) continue;
                double factor = a[k, i];
                for (int j = 0; j < 2 * n; j++) a[k, j] -= factor * a[i, j];
            }
        }

        // 截取右半 [I | M⁻¹]
        var inv = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                inv[i, j] = a[i, n + j];
        return inv;
    }

    /// <summary>
    /// Min-variance weights:w* = (Σ⁻¹·1) / (1ᵀΣ⁻¹·1)
    /// 加 long-only constraint(weights ≥ 0、負的 clip 0)+ max cap(預設 40% / 支)+ renormalize
    /// </summary>
    public static decimal[] MinVarianceWeights(double[,] cov, decimal maxWeight = 0.40m)
    {
        int n = cov.GetLength(0);
        if (n == 0) return Array.Empty<decimal>();
        if (n == 1) return new[] { 1m };

        var inv = Inverse(cov);
        if (inv == null) return EqualWeights(n);   // fallback

        // raw w = Σ⁻¹·1
        var raw = new double[n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                raw[i] += inv[i, j];

        // long-only clip + max cap
        for (int i = 0; i < n; i++)
        {
            if (raw[i] < 0) raw[i] = 0;
        }
        double rawSum = raw.Sum();
        if (rawSum <= 0) return EqualWeights(n);

        // 先 normalize raw 到加總 1、再 iterative cap + redistribute(防 cap 後 renormalize 重新爆 cap)
        var weights = new decimal[n];
        for (int i = 0; i < n; i++) weights[i] = (decimal)(raw[i] / rawSum);

        // Iterative water-filling:任何超 cap 的截掉、超出部分按比例分給未滿 cap 的
        for (int iter = 0; iter < 10; iter++)
        {
            bool changed = false;
            decimal excess = 0m;
            int freeCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (weights[i] > maxWeight)
                {
                    excess += weights[i] - maxWeight;
                    weights[i] = maxWeight;
                    changed = true;
                }
                else if (weights[i] < maxWeight && weights[i] > 0m)
                {
                    freeCount++;
                }
            }
            if (!changed || excess <= 0m || freeCount == 0) break;
            // 按「未滿 cap 的權重比例」分配 excess
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
    /// Max-Sharpe weights(需 expected returns μ):w* = (Σ⁻¹·μ) / (1ᵀΣ⁻¹·μ)
    /// 同樣加 long-only clip + max cap、但對 μ 雜訊敏感、結果常極端、慎用
    /// </summary>
    public static decimal[] MaxSharpeWeights(double[,] cov, decimal[] expectedReturns, decimal maxWeight = 0.40m)
    {
        int n = cov.GetLength(0);
        if (n != expectedReturns.Length || n == 0) return Array.Empty<decimal>();
        if (n == 1) return new[] { 1m };

        var inv = Inverse(cov);
        if (inv == null) return EqualWeights(n);

        // raw w = Σ⁻¹·μ
        var raw = new double[n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                raw[i] += inv[i, j] * (double)expectedReturns[j];

        for (int i = 0; i < n; i++) if (raw[i] < 0) raw[i] = 0;
        double rawSum = raw.Sum();
        if (rawSum <= 0) return EqualWeights(n);

        // 同 min-var、iterative water-filling 避免 cap+renormalize bug
        var weights = new decimal[n];
        for (int i = 0; i < n; i++) weights[i] = (decimal)(raw[i] / rawSum);
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

    public static decimal[] EqualWeights(int n)
    {
        if (n == 0) return Array.Empty<decimal>();
        var w = new decimal[n];
        decimal v = 1m / n;
        for (int i = 0; i < n; i++) w[i] = v;
        return w;
    }

    /// <summary>
    /// Portfolio variance:wᵀΣw、用於 diagnostic 顯示「優化後 portfolio 風險」
    /// </summary>
    public static decimal PortfolioVariance(decimal[] weights, double[,] cov)
    {
        int n = weights.Length;
        if (n == 0 || cov.GetLength(0) != n) return 0m;
        double var_ = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                var_ += (double)weights[i] * (double)weights[j] * cov[i, j];
        return (decimal)var_;
    }

    /// <summary>Portfolio expected return:wᵀμ</summary>
    public static decimal PortfolioReturn(decimal[] weights, decimal[] expectedReturns)
    {
        if (weights.Length != expectedReturns.Length) return 0m;
        decimal r = 0m;
        for (int i = 0; i < weights.Length; i++) r += weights[i] * expectedReturns[i];
        return r;
    }
}
