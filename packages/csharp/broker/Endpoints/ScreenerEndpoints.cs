using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// 標的篩選器 API。
///
///   GET /api/v1/screener/perpetuals?limit=20&refresh=false
///       回 BingX USDT-M 永續、依流動性 + 波動度評分後 top N。
///       refresh=true 強制清 cache 重拉、否則 15 分鐘 TTL。
/// </summary>
public static class ScreenerEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var s = group.MapGroup("/screener");

        s.MapGet("/perpetuals", async (SymbolScreenerService svc, HttpRequest req, CancellationToken ct) =>
        {
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? Math.Clamp(n, 1, 100) : 20;
            var refresh = req.Query.TryGetValue("refresh", out var r) && r.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

            var (results, snapshotAt, error) = await svc.ScreenAsync(limit, refresh, ct);
            if (error != null && results.Count == 0)
                return Results.Ok(ApiResponseHelper.Error(error));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = results.Count,
                snapshot_at = snapshotAt,
                cache_ttl_minutes = 15,
                results = results.Select(x => new
                {
                    symbol = x.Symbol,
                    exchange = x.Exchange,
                    last_price = x.LastPrice,
                    quote_volume_24h_usdt = x.QuoteVolume24h,
                    daily_range_pct = x.DailyRangePct,
                    price_change_pct = x.PriceChangePct,
                    liquidity_score = x.LiquidityScore,
                    volatility_score = x.VolatilityScore,
                    total_score = x.TotalScore,
                    tags = x.Tags,
                }),
            }));
        });
    }
}
