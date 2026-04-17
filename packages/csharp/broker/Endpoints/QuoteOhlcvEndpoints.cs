using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// OHLCV K 線 & 技術指標 API — 透過 IExecutionDispatcher 轉發至 quote-worker
///
/// GET /api/v1/workers/quote/ohlcv?symbol=AAPL&interval=1d&limit=365
/// GET /api/v1/workers/quote/ohlcv/fetch-stock?symbol=AAPL&range=2y&interval=1d
/// GET /api/v1/workers/quote/ohlcv/fetch-crypto?symbol=bitcoin&interval=1d&limit=365
/// GET /api/v1/workers/quote/indicator/sma?symbol=AAPL&period=20&interval=1d
/// GET /api/v1/workers/quote/indicator/ema?symbol=AAPL&period=20&interval=1d
/// GET /api/v1/workers/quote/indicator/rsi?symbol=AAPL&period=14&interval=1d
/// GET /api/v1/workers/quote/indicator/macd?symbol=AAPL&fast=12&slow=26&signal=9
/// </summary>
public static class QuoteOhlcvEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var ohlcv     = group.MapGroup("/workers/quote/ohlcv");
        var indicator = group.MapGroup("/workers/quote/indicator");

        // ── OHLCV 查詢 ──────────────────────────────────────────────────

        ohlcv.MapGet("/", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.ohlcv"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                symbol   = req.Query["symbol"].ToString(),
                interval = req.Query.TryGetValue("interval", out var iv) ? iv.ToString() : "1d",
                limit    = req.Query.TryGetValue("limit", out var lm) && int.TryParse(lm, out var n) ? n : 365,
            });

            var result = await dispatcher.DispatchAsync(BuildRequest("quote.ohlcv", "get_bars", payload));
            return ToResponse(result);
        });

        // ── 抓取美股歷史 ────────────────────────────────────────────────

        ohlcv.MapGet("/fetch-stock", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.ohlcv"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                symbol   = req.Query["symbol"].ToString(),
                range    = req.Query.TryGetValue("range", out var rg) ? rg.ToString() : "2y",
                interval = req.Query.TryGetValue("interval", out var iv) ? iv.ToString() : "1d",
            });

            var result = await dispatcher.DispatchAsync(BuildRequest("quote.ohlcv", "fetch_stock", payload));
            return ToResponse(result);
        });

        // ── 抓取加密貨幣歷史 ────────────────────────────────────────────

        ohlcv.MapGet("/fetch-crypto", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.ohlcv"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                symbol   = req.Query["symbol"].ToString(),
                interval = req.Query.TryGetValue("interval", out var iv) ? iv.ToString() : "1d",
                limit    = req.Query.TryGetValue("limit", out var lm) && int.TryParse(lm, out var n) ? n : 365,
            });

            var result = await dispatcher.DispatchAsync(BuildRequest("quote.ohlcv", "fetch_crypto", payload));
            return ToResponse(result);
        });

        // ── 技術指標 ────────────────────────────────────────────────────

        MapIndicator(indicator, "sma",  new[] { "period" });
        MapIndicator(indicator, "ema",  new[] { "period" });
        MapIndicator(indicator, "rsi",  new[] { "period" });
        MapIndicator(indicator, "macd", new[] { "fast", "slow", "signal" });
    }

    private static void MapIndicator(RouteGroupBuilder group, string route, string[] extraParams)
    {
        group.MapGet($"/{route}", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("quote.indicator"))
                return Results.Ok(ApiResponseHelper.Error("quote-worker not connected"));

            var dict = new Dictionary<string, object?>
            {
                ["symbol"]   = req.Query["symbol"].ToString(),
                ["interval"] = req.Query.TryGetValue("interval", out var iv) ? iv.ToString() : "1d",
                ["limit"]    = req.Query.TryGetValue("limit", out var lm) && int.TryParse(lm, out var n) ? n : 500,
            };

            foreach (var p in extraParams)
            {
                if (req.Query.TryGetValue(p, out var v) && int.TryParse(v, out var val))
                    dict[p] = val;
            }

            var payload = JsonSerializer.Serialize(dict);
            var result = await dispatcher.DispatchAsync(BuildRequest("quote.indicator", route, payload));
            return ToResponse(result);
        });
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

    private static ApprovedRequest BuildRequest(
        string capabilityId, string route, string payload = "{}")
        => new()
        {
            RequestId    = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId,
            Route        = route,
            Payload      = payload,
            Scope        = "{}",
            PrincipalId  = "system",
            TaskId       = "dashboard",
            SessionId    = "dashboard"
        };

    private static IResult ToResponse(ExecutionResult result)
    {
        if (!result.Success)
            return Results.Ok(ApiResponseHelper.Error(result.ErrorMessage ?? "dispatch failed"));

        try
        {
            var data = JsonDocument.Parse(result.ResultPayload ?? "{}");
            return Results.Ok(ApiResponseHelper.Success(data.RootElement));
        }
        catch
        {
            return Results.Ok(ApiResponseHelper.Success(result.ResultPayload ?? "{}"));
        }
    }
}
