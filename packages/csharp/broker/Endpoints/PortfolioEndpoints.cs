using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// Portfolio analytics API — 從 trading-worker 的成交紀錄計算 P&L、Sharpe、MaxDD、Win Rate 等指標。
///
///   GET /api/v1/portfolio/metrics?exchange=alpaca&limit=500
///       → 完整指標 + 權益曲線 + per-symbol 明細（一次回所有東西）
///
///   GET /api/v1/portfolio/equity-curve?exchange=alpaca&limit=500
///       → 只回權益曲線（方便前端只想畫圖時省流量）
///
///   GET /api/v1/portfolio/by-symbol?exchange=alpaca&limit=500
///       → 只回 per-symbol stats
/// </summary>
public static class PortfolioEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var p = group.MapGroup("/portfolio");

        p.MapGet("/metrics", async (PortfolioAnalyticsService svc, HttpRequest req) =>
        {
            var (exchange, limit) = ReadQuery(req);
            var metrics = await svc.GetMetricsAsync(exchange, limit);
            return Results.Ok(ApiResponseHelper.Success(metrics));
        });

        p.MapGet("/equity-curve", async (PortfolioAnalyticsService svc, HttpRequest req) =>
        {
            var (exchange, limit) = ReadQuery(req);
            var metrics = await svc.GetMetricsAsync(exchange, limit);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                exchange = metrics.Exchange,
                points = metrics.EquityCurve,
                total_pnl = metrics.TotalPnl,
            }));
        });

        p.MapGet("/by-symbol", async (PortfolioAnalyticsService svc, HttpRequest req) =>
        {
            var (exchange, limit) = ReadQuery(req);
            var metrics = await svc.GetMetricsAsync(exchange, limit);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                exchange = metrics.Exchange,
                symbols = metrics.PerSymbol,
            }));
        });
    }

    private static (string exchange, int limit) ReadQuery(HttpRequest req)
    {
        var exchange = req.Query.TryGetValue("exchange", out var ex) && !string.IsNullOrWhiteSpace(ex)
            ? ex.ToString() : "alpaca";
        var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) && n > 0
            ? Math.Min(n, 5000) : 500;
        return (exchange, limit);
    }
}
