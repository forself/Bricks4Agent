using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Strategy API — 透過 IExecutionDispatcher 轉發至 strategy-worker
///
/// POST /api/v1/strategy/signal   — 產生訊號（body: {strategy, bars, symbol, ...}）
/// GET  /api/v1/strategy/list     — 列出可用策略
/// </summary>
public static class StrategyEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var strategy = group.MapGroup("/strategy");

        strategy.MapPost("/signal", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "evaluate", body));
            return ToResponse(result);
        });

        strategy.MapPost("/backtest", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "backtest", body));
            return ToResponse(result);
        });

        strategy.MapPost("/optimize", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "optimize", body));
            return ToResponse(result);
        });

        strategy.MapPost("/walk-forward", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "walk_forward", body));
            return ToResponse(result);
        });

        // #1 通用 walk-forward backtest——對任何策略切 train/test 滑窗、給 OOS 績效 + IS-OOS gap
        strategy.MapPost("/backtest-walkforward", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "backtest_walk_forward", body));
            return ToResponse(result);
        });

        // universe 掃描 → Top N 候選（harmonic + price action + SMC 加權評分）
        strategy.MapPost("/scan", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "scan", body));
            return ToResponse(result);
        });

        // 持倉決策：對已開倉位給 ADD / HOLD / TRIM / EXIT 建議 + 信心 + 目標價
        strategy.MapPost("/position-decision", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "position_decision", body));
            return ToResponse(result);
        });

        // 多維訊號雷達卡（單一 symbol、no LLM）
        strategy.MapPost("/signal-card", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "signal_card", body));
            return ToResponse(result);
        });

        // 通用 walk-forward 參數優化：broker 抓 bars → 派 optimize_wf → 回「優化 vs 預設」OOS 對照。
        // GET /api/v1/strategy/optimize-wf?strategy=super_trend&symbol=BTC-USDT&interval=1d&limit=1000&train_bars=365&test_bars=90
        strategy.MapGet("/optimize-wf", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal") || !registry.HasAvailableWorker("quote.ohlcv"))
                return Results.Ok(ApiResponseHelper.Error("strategy.signal or quote.ohlcv worker not connected"));

            var strat    = req.Query["strategy"].ToString();
            var symbol   = req.Query["symbol"].ToString();
            var interval = req.Query.TryGetValue("interval", out var iv) ? iv.ToString() : "1d";
            var limit    = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 1000;
            var train    = req.Query.TryGetValue("train_bars", out var tb) && int.TryParse(tb, out var tbi) ? tbi : 365;
            var test     = req.Query.TryGetValue("test_bars", out var tt) && int.TryParse(tt, out var tti) ? tti : 90;
            if (string.IsNullOrEmpty(strat) || string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("strategy + symbol query params required"));

            var barsRes = await dispatcher.DispatchAsync(BuildRequest(ctx, "quote.ohlcv", "get_bars",
                JsonSerializer.Serialize(new { symbol, interval, limit })));
            if (!barsRes.Success) return Results.Ok(ApiResponseHelper.Error($"get_bars failed: {barsRes.ErrorMessage}"));
            var barsDoc = JsonDocument.Parse(barsRes.ResultPayload ?? "{}").RootElement;
            if (!barsDoc.TryGetProperty("bars", out var barsArr) || barsArr.ValueKind != JsonValueKind.Array)
                return Results.Ok(ApiResponseHelper.Error("no bars for symbol"));

            var optPayload = JsonSerializer.Serialize(new
            {
                strategy = strat, bars = barsArr, symbol, interval, train_bars = train, test_bars = test,
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "optimize_wf", optPayload));
            return ToResponse(result);
        });

        // 組合層 walk-forward:N 條策略各半資金、合併權益 → 捕捉去相關紅利。broker 抓 funding-bars 後派 portfolio_wf。
        // GET /api/v1/strategy/portfolio-wf?strategies=rsi_stoch,mfi&symbol=BTC-USDT&interval=1d&limit=1500&train_bars=365&test_bars=90
        strategy.MapGet("/portfolio-wf", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal") || !registry.HasAvailableWorker("quote.ohlcv"))
                return Results.Ok(ApiResponseHelper.Error("strategy.signal or quote.ohlcv worker not connected"));

            var strategies = req.Query["strategies"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var symbol   = req.Query["symbol"].ToString();
            var interval = req.Query.TryGetValue("interval", out var iv) ? iv.ToString() : "1d";
            var limit    = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 1500;
            var train    = req.Query.TryGetValue("train_bars", out var tb) && int.TryParse(tb, out var tbi) ? tbi : 365;
            var test     = req.Query.TryGetValue("test_bars", out var tt) && int.TryParse(tt, out var tti) ? tti : 90;
            if (strategies.Length == 0 || string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("strategies + symbol query params required"));

            // get_bars_funding：bars 帶 funding_rate,讓組合回測也扣資金費(誠實)
            var barsRes = await dispatcher.DispatchAsync(BuildRequest(ctx, "quote.ohlcv", "get_bars_funding",
                JsonSerializer.Serialize(new { symbol, interval, limit })));
            if (!barsRes.Success) return Results.Ok(ApiResponseHelper.Error($"get_bars_funding failed: {barsRes.ErrorMessage}"));
            var barsDoc = JsonDocument.Parse(barsRes.ResultPayload ?? "{}").RootElement;
            if (!barsDoc.TryGetProperty("bars", out var barsArr) || barsArr.ValueKind != JsonValueKind.Array)
                return Results.Ok(ApiResponseHelper.Error("no bars for symbol"));

            var payload = JsonSerializer.Serialize(new
            {
                strategies, bars = barsArr, symbol, interval, train_bars = train, test_bars = test,
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "portfolio_wf", payload));
            return ToResponse(result);
        });

        strategy.MapGet("/compare", async (
            Broker.Services.StrategyComparisonService svc, HttpRequest req) =>
        {
            var symbol = req.Query.TryGetValue("symbol", out var s) && !string.IsNullOrWhiteSpace(s)
                ? s.ToString() : "AAPL";
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) && n > 0
                ? Math.Min(n, 5000) : 300;
            var cash = req.Query.TryGetValue("initial_cash", out var c) && decimal.TryParse(c, out var cd) && cd > 0
                ? cd : 100_000m;
            var filter = req.Query.TryGetValue("strategies", out var fs) && !string.IsNullOrWhiteSpace(fs)
                ? fs.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : null;

            var result = await svc.CompareAsync(symbol, limit, cash, filter);
            if (result == null || !string.IsNullOrEmpty(result.Error))
                return Results.Ok(ApiResponseHelper.Error(result?.Error ?? "comparison failed"));
            return Results.Ok(ApiResponseHelper.Success(result));
        });

        strategy.MapGet("/list", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "list"));
            return ToResponse(result);
        });
    }

    private static ApprovedRequest BuildRequest(
        HttpContext ctx, string capabilityId, string route, string payload = "{}")
    {
        // 拿登入 user 的 (principal_id, role)；沒登入或內部呼叫 → fallback "system" / role 留空 = ACL fail-open
        var pid = RequestBodyHelper.GetPrincipalId(ctx);
        var role = RequestBodyHelper.GetRoleId(ctx);
        return new()
        {
            RequestId    = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId,
            Route        = route,
            Payload      = payload,
            Scope        = "{}",
            PrincipalId  = string.IsNullOrEmpty(pid) ? "system" : pid,
            TaskId       = "dashboard",
            SessionId    = "dashboard",
            Role         = role,
        };
    }

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
