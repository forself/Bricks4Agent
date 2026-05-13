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

            // since 客戶端再過濾一次（worker 端也有但保險）+ 抽 realized_pnl 給 aggregator
            var pnls = new List<decimal>();
            foreach (var t in tradesEl.EnumerateArray())
            {
                if (since.HasValue && t.TryGetProperty("executed_at", out var ea) &&
                    DateTime.TryParse(ea.GetString() ?? "", out var execDt) && execDt < since.Value)
                    continue;
                if (!t.TryGetProperty("realized_pnl", out var p) || p.ValueKind != JsonValueKind.Number) continue;
                pnls.Add(p.GetDecimal());
            }

            var stats = BrokerCore.Trading.PnlAggregator.Aggregate(pnls);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                exchange, symbol,
                since = since?.ToString("o"),
                trade_count = stats.TradeCount,
                win_count = stats.WinCount,
                lose_count = stats.LoseCount,
                realized_pnl_sum = stats.RealizedPnlSum,
                win_rate_pct = stats.WinRatePct,
                avg_win = stats.AvgWin,
                avg_loss = stats.AvgLoss,
                profit_factor = stats.ProfitFactor,
            }));
        });

        // 手動觸發每日彙整（測試用 / 需要立刻看 X 小時績效時）
        trading.MapPost("/push-daily-summary", async (
            Broker.Services.DailyReportService daily, HttpRequest req, CancellationToken ct) =>
        {
            var hours = req.Query.TryGetValue("hours", out var h) && int.TryParse(h.ToString(), out var n) ? n : 24;
            var (ok, summary) = await daily.BuildAndPushAsync(hours, ct);
            return Results.Ok(ApiResponseHelper.Success(new { pushed = ok, summary }));
        });

        // 手動 refresh contract specs cache（trading-worker 連回後可立即灌、不用等 12h 排程）
        trading.MapPost("/symbol-specs/refresh", async (
            Broker.Services.SymbolSpecsService svc, CancellationToken ct) =>
        {
            var (ok, count, error) = await svc.RefreshNowAsync(ct);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                ok, count, error,
                cache_count = BrokerCore.Trading.SymbolSpecs.CacheCount,
                cache_updated_at = BrokerCore.Trading.SymbolSpecs.CacheUpdatedAt,
            }));
        });

        // 查 cache 狀態（不觸發 refresh）
        trading.MapGet("/symbol-specs/status", () =>
        {
            return Results.Ok(ApiResponseHelper.Success(new
            {
                cache_count = BrokerCore.Trading.SymbolSpecs.CacheCount,
                cache_updated_at = BrokerCore.Trading.SymbolSpecs.CacheUpdatedAt,
            }));
        });

        // ── Risk Anchor 狀態 + manual override ──
        // GET：dashboard 顯示當前 anchor + 最近變動原因
        // POST：admin 手動指定 anchor（bypass deposit/withdraw 偵測）
        trading.MapGet("/risk-anchor/{exchange}", (
            string exchange, Broker.Services.BalanceAnchorService svc, Broker.Services.AutoTraderService at) =>
        {
            var state = svc.GetState(exchange);
            var inMemory = at.DeclaredCapital.TryGetValue(exchange.ToLowerInvariant(), out var v) ? v : 0m;
            return Results.Ok(ApiResponseHelper.Success(new
            {
                exchange = exchange.ToLowerInvariant(),
                in_memory_anchor = inMemory,
                persisted = state == null ? null : new
                {
                    current_anchor = state.CurrentAnchor,
                    last_seen_balance = state.LastSeenBalance,
                    last_check_at = state.LastCheckAt,
                    last_change_reason = state.LastChangeReason,
                    last_change_at = state.LastChangeAt,
                },
            }));
        });

        trading.MapPost("/risk-anchor/{exchange}", async (
            string exchange, HttpRequest req, Broker.Services.BalanceAnchorService svc, CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            decimal newAnchor;
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                if (!doc.TryGetProperty("new_anchor", out var na) || na.ValueKind != JsonValueKind.Number)
                    return Results.Ok(ApiResponseHelper.Error("missing or non-numeric: new_anchor"));
                newAnchor = na.GetDecimal();
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error("invalid JSON: " + ex.Message)); }

            if (newAnchor < 0m) return Results.Ok(ApiResponseHelper.Error("new_anchor must be >= 0"));

            var (oldVal, newVal) = await svc.SetAnchorManualAsync(exchange, newAnchor, ct);
            return Results.Ok(ApiResponseHelper.Success(new { exchange, old_anchor = oldVal, new_anchor = newVal }));
        });

        // ── Multi-Timeframe Signal Matrix ──
        // 對 (exchange, symbol, strategy) 平行跑 1h / 4h / 1d 三個 evaluate、回成矩陣。
        // 給 dashboard MTF tab 用、避免前端要打 3 次來回。
        // intervals 可用 query 覆寫（comma 分隔）；預設 "1h,4h,1d"。
        trading.MapGet("/mtf-matrix", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("strategy.signal") || !registry.HasAvailableWorker("quote.ohlcv"))
                return Results.Ok(ApiResponseHelper.Error("strategy-worker or quote-worker not connected"));

            var symbol = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : "";
            var strategy = req.Query.TryGetValue("strategy", out var st) ? st.ToString() : "harmonic_pattern";
            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "bingx";
            var intervalsRaw = req.Query.TryGetValue("intervals", out var iv) ? iv.ToString() : "1h,4h,1d";
            if (string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("missing: symbol"));

            var intervals = intervalsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(5).ToArray();

            async Task<object> EvalAtIntervalAsync(string interval)
            {
                var barsPayload = JsonSerializer.Serialize(new { symbol, interval, limit = 200 });
                var barsRes = await dispatcher.DispatchAsync(BuildRequest(ctx, "quote.ohlcv", "get_bars", barsPayload));
                if (!barsRes.Success) return new { interval, ok = false, error = "bars fetch failed: " + barsRes.ErrorMessage };

                var barsDoc = JsonDocument.Parse(barsRes.ResultPayload ?? "{}").RootElement;
                if (!barsDoc.TryGetProperty("bars", out var barsArr) || barsArr.GetArrayLength() < 50)
                    return new { interval, ok = false, error = $"bars too few ({barsArr.GetArrayLength()})" };

                var sigPayload = JsonSerializer.Serialize(new
                {
                    strategy, symbol, exchange, interval,
                    bars = barsArr,
                });
                var sigRes = await dispatcher.DispatchAsync(BuildRequest(ctx, "strategy.signal", "evaluate", sigPayload));
                if (!sigRes.Success) return new { interval, ok = false, error = "evaluate failed: " + sigRes.ErrorMessage };

                var sig = JsonDocument.Parse(sigRes.ResultPayload ?? "{}").RootElement;
                string? regimeType = null;
                decimal? slope = null, atrPct = null;
                if (sig.TryGetProperty("regime", out var reg) && reg.ValueKind == JsonValueKind.Object)
                {
                    if (reg.TryGetProperty("type", out var rtEl)) regimeType = rtEl.GetString();
                    if (reg.TryGetProperty("sma50_slope", out var sl) && sl.ValueKind == JsonValueKind.Number) slope = sl.GetDecimal();
                    if (reg.TryGetProperty("atr_pct", out var ap) && ap.ValueKind == JsonValueKind.Number) atrPct = ap.GetDecimal();
                }
                return new
                {
                    interval,
                    ok = true,
                    action      = sig.TryGetProperty("action", out var a) ? a.GetString() : "hold",
                    confidence  = sig.TryGetProperty("confidence", out var c) ? c.GetDecimal() : 0m,
                    reason      = sig.TryGetProperty("reason", out var rr) ? rr.GetString() : "",
                    regime      = regimeType,
                    sma50_slope = slope,
                    atr_pct     = atrPct,
                };
            }

            // 平行跑、最慢的 interval 決定總延遲
            var tasks = intervals.Select(EvalAtIntervalAsync).ToArray();
            var rows = await Task.WhenAll(tasks);

            // 算出 "bullish/mixed/bearish/unclear" overall verdict（dashboard 直接顯示）
            int up = 0, dn = 0, ok = 0;
            foreach (var rObj in rows)
            {
                // 用 reflection 避免再次反序列化
                var okProp = rObj.GetType().GetProperty("ok")?.GetValue(rObj) as bool?;
                if (okProp != true) continue;
                ok++;
                var actObj = rObj.GetType().GetProperty("action")?.GetValue(rObj) as string;
                if (actObj == "buy") up++;
                else if (actObj == "sell") dn++;
            }
            var verdict = ok == 0 ? "no_data"
                : (up == ok ? "all_bullish"
                    : dn == ok ? "all_bearish"
                    : (up > dn ? "mixed_bullish" : (dn > up ? "mixed_bearish" : "neutral")));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                symbol, strategy, exchange,
                intervals = intervals,
                verdict,
                rows,
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
