namespace BrokerCore.Trading;

/// <summary>
/// 30-day daily returns Pearson correlation 計算。
///
/// 動機：AutoTrader 設 max_open_positions=3、但「BTC / ETH / SOL 同向開三筆」
/// 實質是同一個 beta 賭注（crypto major 30-day correlation 通常 &gt; 0.85）。
/// 名義 3 倉 = 實質 1 倉、3 × 2% portfolio risk = 6% 但 effective risk &gt; 5.1%。
///
/// 對學術 narrative：thesis Ch 4.6 寫「max 3 positions」是 user requirement；
/// 但 finance 上「N 倉不等於 N 倍分散」是 well-known fact、加 correlation cap
/// 是對 portfolio theory 的正向 alignment。
///
/// 設計：純函式、不持狀態；caller（broker AutoTrader）負責 fetch K 線、拼 closes
/// 序列、call ComputeMaxCorrelation。回 0..1 之間實數、0=完全不相關、1=完全同向。
/// </summary>
public static class CorrelationGuard
{
    /// <summary>
    /// 算兩個 close 序列的 Pearson correlation（用 daily log returns、不是 raw close）。
    /// 兩序列需同長度、至少 10 個 sample。
    /// 短於 10 或全 0 報酬 → return 0（保守、不擋）。
    /// </summary>
    public static decimal PearsonOfReturns(IReadOnlyList<decimal> closesA, IReadOnlyList<decimal> closesB)
    {
        if (closesA == null || closesB == null) return 0m;
        var n = Math.Min(closesA.Count, closesB.Count);
        if (n < 11) return 0m;   // 至少要 10 個 returns（11 closes）

        // daily log returns
        var ra = new decimal[n - 1];
        var rb = new decimal[n - 1];
        for (int i = 1; i < n; i++)
        {
            if (closesA[i - 1] <= 0m || closesB[i - 1] <= 0m) return 0m;
            // 用 percentage change（簡化、不算 log；對短期 daily 差異微小）
            ra[i - 1] = (closesA[i] - closesA[i - 1]) / closesA[i - 1];
            rb[i - 1] = (closesB[i] - closesB[i - 1]) / closesB[i - 1];
        }

        // Pearson r = cov / (σa × σb)
        var meanA = ra.Average();
        var meanB = rb.Average();
        decimal cov = 0m, varA = 0m, varB = 0m;
        for (int i = 0; i < ra.Length; i++)
        {
            var da = ra[i] - meanA;
            var db = rb[i] - meanB;
            cov += da * db;
            varA += da * da;
            varB += db * db;
        }
        if (varA <= 0m || varB <= 0m) return 0m;
        // 用 double sqrt（decimal 沒 Sqrt）、轉回 decimal
        var sigA = (decimal)Math.Sqrt((double)varA);
        var sigB = (decimal)Math.Sqrt((double)varB);
        var r = cov / (sigA * sigB);
        return Math.Round(r, 4);
    }

    /// <summary>
    /// 對「新 symbol 跟一組已開倉 symbol」每個算 correlation、回最大值。
    /// caller 用 max 跟 threshold 比、決定要不要擋。
    ///
    /// 若 existingClosesMap 為空、回 0（沒已開倉、自然不衝突）。
    /// </summary>
    public static (decimal MaxCorr, string? MostCorrelatedSymbol) ComputeMaxCorrelation(
        IReadOnlyList<decimal> newSymbolCloses,
        IReadOnlyDictionary<string, IReadOnlyList<decimal>> existingClosesMap)
    {
        if (existingClosesMap == null || existingClosesMap.Count == 0) return (0m, null);

        decimal maxCorr = 0m;
        string? maxSymbol = null;
        foreach (var (sym, closes) in existingClosesMap)
        {
            var r = PearsonOfReturns(newSymbolCloses, closes);
            var abs = Math.Abs(r);
            if (abs > maxCorr)
            {
                maxCorr = abs;
                maxSymbol = sym;
            }
        }
        return (maxCorr, maxSymbol);
    }
}
