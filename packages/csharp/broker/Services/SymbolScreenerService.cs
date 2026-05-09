using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>
/// 永續合約標的篩選器——拉 24h ticker 全清單、依「流動性 + 波動度」評分、產推薦。
///
/// 為什麼存在：手動加 watch 容易選到流動性差、滑價狂吃的標的。screener 自動把「成交量 < 10M USDT」、
/// 「波動度極端（&lt; 1% 或 &gt; 8%）」過濾掉、剩下的依分數排序、user 看到的都是「值得試的」。
///
/// 為什麼不直接 auto-rotate watchlist：讀寫分離。screener 是 read-only 推薦、是否加進去由 user 決策
/// （或之後另開 auto-rotate service、用 screener 結果為輸入）。少一層自動化、多一層可控。
///
/// 評分（簡化、之後可進化）：
///   - liquidity = log10(quote_volume_usdt / 10M)，&gt;1 才能進、值越大越好
///   - volatility = 高斯式評分、(high-low)/open 在 4% 中心、最佳 1.0、偏離扣分
///   - 總分 = 0.6 * liquidity + 0.4 * volatility
/// 過濾門檻（保守）：
///   - quote_volume &gt;= 10M USDT
///   - daily range &gt;= 1% 且 &lt;= 12%
/// </summary>
public class SymbolScreenerService
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger<SymbolScreenerService> _logger;

    private List<ScreenerResult>? _cache;
    private DateTime _cacheAt = DateTime.MinValue;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();

    public SymbolScreenerService(
        IExecutionDispatcher dispatcher,
        IWorkerRegistry registry,
        ILogger<SymbolScreenerService> logger)
    {
        _dispatcher = dispatcher;
        _registry = registry;
        _logger = logger;
    }

    public class ScreenerResult
    {
        public string Symbol { get; set; } = string.Empty;
        public string Exchange { get; set; } = "bingx";
        public decimal LastPrice { get; set; }
        public decimal QuoteVolume24h { get; set; }
        public decimal DailyRangePct { get; set; }    // (high-low)/open × 100
        public decimal PriceChangePct { get; set; }
        public decimal LiquidityScore { get; set; }
        public decimal VolatilityScore { get; set; }
        public decimal TotalScore { get; set; }
        public List<string> Tags { get; set; } = new();  // 'top-volume', 'high-vol', 'low-vol', 'meme' 等供 UI 顯示
    }

    public async Task<(List<ScreenerResult> Results, DateTime SnapshotAt, string? Error)> ScreenAsync(int limit = 20, bool forceRefresh = false, CancellationToken ct = default)
    {
        // cache hit & 不強制刷新就直接回
        if (!forceRefresh && _cache != null && DateTime.UtcNow - _cacheAt < _cacheTtl)
        {
            return (_cache.Take(limit).ToList(), _cacheAt, null);
        }

        if (!_registry.HasAvailableWorker("trading.perpetual"))
            return (new List<ScreenerResult>(), DateTime.UtcNow, "trading-worker not connected");

        var req = new ApprovedRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            CapabilityId = "trading.perpetual",
            Route = "get_tickers_24h",
            Payload = JsonSerializer.Serialize(new { exchange = "bingx" }),
            Scope = "{}", PrincipalId = "system",
            TaskId = "symbol-screener", SessionId = "symbol-screener",
        };

        var result = await _dispatcher.DispatchAsync(req);
        if (!result.Success)
        {
            _logger.LogWarning("Screener: dispatch failed: {Err}", result.ErrorMessage);
            return (new List<ScreenerResult>(), DateTime.UtcNow, result.ErrorMessage);
        }

        var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
        if (!doc.TryGetProperty("tickers", out var tickers) || tickers.ValueKind != JsonValueKind.Array)
            return (new List<ScreenerResult>(), DateTime.UtcNow, "empty tickers");

        var scored = new List<ScreenerResult>();
        foreach (var t in tickers.EnumerateArray())
        {
            var symbol = t.GetProperty("symbol").GetString() ?? "";
            // 只看 USDT-M 永續、過濾掉 USDC / 反向合約等
            if (!symbol.EndsWith("-USDT", StringComparison.OrdinalIgnoreCase)) continue;

            var quoteVol = t.TryGetProperty("quote_volume", out var qv) ? qv.GetDecimal() : 0m;
            var high = t.TryGetProperty("high", out var h) ? h.GetDecimal() : 0m;
            var low = t.TryGetProperty("low", out var l) ? l.GetDecimal() : 0m;
            var open = t.TryGetProperty("open", out var o) ? o.GetDecimal() : 0m;
            var last = t.TryGetProperty("last_price", out var lp) ? lp.GetDecimal() : 0m;
            var changePct = t.TryGetProperty("price_change_pct", out var cp) ? cp.GetDecimal() : 0m;

            if (open <= 0m || high <= 0m || low <= 0m) continue;
            var rangePct = (high - low) / open * 100m;

            // 過濾門檻
            if (quoteVol < 10_000_000m) continue;       // < 10M USDT 流動性太差
            if (rangePct < 1m || rangePct > 12m) continue;  // 太死或極端波動都跳過

            // 評分
            var liquidity = (decimal)Math.Log10((double)(quoteVol / 10_000_000m));   // 1=10M, 2=100M, 3=1B
            var volScore = ScoreVolatility(rangePct);
            var total = 0.6m * liquidity + 0.4m * volScore;

            var tags = new List<string>();
            if (quoteVol >= 1_000_000_000m) tags.Add("top-volume");
            if (rangePct >= 8m) tags.Add("high-vol");
            if (rangePct <= 2m) tags.Add("low-vol");

            scored.Add(new ScreenerResult
            {
                Symbol = symbol,
                LastPrice = last,
                QuoteVolume24h = quoteVol,
                DailyRangePct = Math.Round(rangePct, 2),
                PriceChangePct = changePct,
                LiquidityScore = Math.Round(liquidity, 3),
                VolatilityScore = Math.Round(volScore, 3),
                TotalScore = Math.Round(total, 3),
                Tags = tags,
            });
        }

        scored = scored.OrderByDescending(s => s.TotalScore).ToList();

        lock (_gate)
        {
            _cache = scored;
            _cacheAt = DateTime.UtcNow;
        }

        _logger.LogInformation("Screener: scored {Total} symbols, top: {Top}",
            scored.Count, string.Join(", ", scored.Take(5).Select(s => s.Symbol)));

        return (scored.Take(limit).ToList(), _cacheAt, null);
    }

    /// <summary>
    /// 高斯式評分：rangePct 在 4% 中心給 1.0、偏離越多分數越低、12% 處接近 0。
    /// </summary>
    private static decimal ScoreVolatility(decimal rangePct)
    {
        // peak at 4, decay with σ=4
        var diff = (double)(rangePct - 4m);
        var sigma = 4.0;
        var score = Math.Exp(-(diff * diff) / (2.0 * sigma * sigma));
        return (decimal)score;
    }
}
