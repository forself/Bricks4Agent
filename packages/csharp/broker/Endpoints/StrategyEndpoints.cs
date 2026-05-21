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
