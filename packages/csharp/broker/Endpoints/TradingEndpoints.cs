using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Trading API — 透過 IExecutionDispatcher 轉發至 trading-worker
///
/// Orders:
///   POST /api/v1/trading/order            — 下單
///   DELETE /api/v1/trading/order/{id}      — 取消訂單
///   GET  /api/v1/trading/order/{id}        — 查詢訂單
///   GET  /api/v1/trading/orders            — 列出訂單
///
/// Account:
///   GET  /api/v1/trading/account           — 帳戶摘要
///   GET  /api/v1/trading/positions         — 持倉
///   GET  /api/v1/trading/trades            — 成交紀錄
///   GET  /api/v1/trading/exchanges         — 可用交易所
/// </summary>
public static class TradingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var trading = group.MapGroup("/trading");

        // ── Orders ──────────────────────────────────────────────────

        trading.MapPost("/order", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "place_order", body));
            return ToResponse(result);
        });

        trading.MapDelete("/order/{externalId}", async (
            string externalId,
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var payload = JsonSerializer.Serialize(new { exchange, external_id = externalId });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "cancel_order", payload));
            return ToResponse(result);
        });

        trading.MapGet("/order/{orderId}", async (
            string orderId,
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new { order_id = orderId });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.order", "get_order", payload));
            return ToResponse(result);
        });

        trading.MapGet("/orders", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                symbol = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : (string?)null,
                status = req.Query.TryGetValue("status", out var st) ? st.ToString() : (string?)null,
                limit  = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50,
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "list_orders", payload));
            return ToResponse(result);
        });

        // ── Account ─────────────────────────────────────────────────

        trading.MapGet("/account", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var payload = JsonSerializer.Serialize(new { exchange });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_account", payload));
            return ToResponse(result);
        });

        trading.MapGet("/positions", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var payload = JsonSerializer.Serialize(new { exchange });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_positions", payload));
            return ToResponse(result);
        });

        trading.MapGet("/trades", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new
            {
                exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca",
                symbol   = req.Query.TryGetValue("symbol",   out var s)  ? s.ToString()  : (string?)null,
                limit    = req.Query.TryGetValue("limit",    out var l) && int.TryParse(l, out var n) ? n : 50,
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_trades", payload));
            return ToResponse(result);
        });

        // PnL summary：聚合一段時間的成交、計算總 realized PnL / 勝率 / profit factor。
        // 給 dashboard 顯示「今天 / 本週 / 全期」績效、給 LLM 寫日報。
        trading.MapGet("/pnl-summary", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "bingx";
            var symbol   = req.Query.TryGetValue("symbol",   out var s)  ? s.ToString() : (string?)null;
            // since=YYYY-MM-DD 或 ISO datetime；空 = 全部
            DateTime? since = null;
            if (req.Query.TryGetValue("since", out var sn) && DateTime.TryParse(sn.ToString(), out var snDt))
                since = snDt.ToUniversalTime();
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 500;

            // get_trade_history 純讀本地 DB、不挑 exchange client、支援 BingX perp。
            // 跟 get_trades（live exchange API）對比：history 從上次 sync 起的歷史快取、含 realized_pnl。
            var payload = JsonSerializer.Serialize(new
            {
                exchange, symbol, limit,
                since = since?.ToString("o"),
            });
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_trade_history", payload));
            if (!result.Success)
                return Results.Ok(ApiResponseHelper.Error(result.ErrorMessage ?? "get_trade_history failed"));

            // 解析 trades + 聚合
            var tradesDoc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            if (!tradesDoc.TryGetProperty("trades", out var tradesEl) || tradesEl.ValueKind != JsonValueKind.Array)
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    exchange, symbol, since,
                    trade_count = 0, win_count = 0, lose_count = 0,
                    realized_pnl_sum = 0m, win_rate_pct = 0m,
                    avg_win = 0m, avg_loss = 0m, profit_factor = 0m,
                }));

            int total = 0, wins = 0, loses = 0;
            decimal pnlSum = 0m, winSum = 0m, lossSum = 0m;
            foreach (var t in tradesEl.EnumerateArray())
            {
                if (since.HasValue && t.TryGetProperty("executed_at", out var ea) &&
                    DateTime.TryParse(ea.GetString() ?? "", out var execDt) && execDt < since.Value)
                    continue;

                decimal? pnl = null;
                if (t.TryGetProperty("realized_pnl", out var p) && p.ValueKind == JsonValueKind.Number)
                    pnl = p.GetDecimal();
                if (pnl == null) continue;   // 跳過沒有 realized_pnl 的（如：純開倉、未對沖）

                total++;
                pnlSum += pnl.Value;
                if (pnl.Value > 0m) { wins++;  winSum  += pnl.Value; }
                else if (pnl.Value < 0m) { loses++; lossSum += pnl.Value; }
            }

            var winRate     = total > 0 ? Math.Round(100m * wins / total, 1) : 0m;
            var avgWin      = wins > 0 ? Math.Round(winSum / wins, 4) : 0m;
            var avgLoss     = loses > 0 ? Math.Round(lossSum / loses, 4) : 0m;
            // profit factor = 總獲利 / |總損失|；無損失時回 ∞ 用 99.99 代表
            var profitFactor = lossSum < 0m ? Math.Round(winSum / Math.Abs(lossSum), 3) : (winSum > 0m ? 99.99m : 0m);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                exchange, symbol,
                since = since?.ToString("o"),
                trade_count = total,
                win_count = wins,
                lose_count = loses,
                realized_pnl_sum = Math.Round(pnlSum, 4),
                win_rate_pct = winRate,
                avg_win = avgWin,
                avg_loss = avgLoss,
                profit_factor = profitFactor,
            }));
        });

        trading.MapGet("/exchanges", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.account", "list_exchanges"));
            return ToResponse(result);
        });
    }

    // ── 輔助 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 從 HttpContext 抓真實的 principal/role（由 BrokerAuthMiddleware /
    /// InternalBotAuthMiddleware 注入）。硬寫 PrincipalId="system" 會讓
    /// PoolDispatcher approval gate 直接放行（system 是內部背景任務 exemption）、
    /// 任何走 HTTP 進來的單就繞過 admin 核准了——絕對不行。
    /// </summary>
    private static ApprovedRequest BuildRequest(
        HttpContext ctx, string capabilityId, string route, string payload = "{}")
    {
        // 用 RequestBodyHelper 統一兩套 auth：scoped_token + cookie session 都認
        var principalId = RequestBodyHelper.GetPrincipalId(ctx);
        var roleId      = RequestBodyHelper.GetRoleId(ctx);
        if (string.IsNullOrEmpty(principalId)) principalId = "system";
        if (string.IsNullOrEmpty(roleId))      roleId      = "system";
        var taskId      = ctx.Items[BrokerAuthMiddleware.TaskIdKey]    as string ?? "dashboard";
        var sessionId   = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? "dashboard";
        return new ApprovedRequest
        {
            RequestId    = Guid.NewGuid().ToString("N"),
            CapabilityId = capabilityId,
            Route        = route,
            Payload      = payload,
            Scope        = "{}",
            PrincipalId  = principalId,
            Role         = roleId,
            TaskId       = taskId,
            SessionId    = sessionId,
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
