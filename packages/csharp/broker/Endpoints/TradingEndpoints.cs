using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;
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
            // 2026-06-03 安全:訂單查詢要登入(原本無 auth、defense-in-depth)。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var payload = JsonSerializer.Serialize(new { order_id = orderId });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.order", "get_order", payload));
            return ToResponse(result);
        });

        trading.MapGet("/orders", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            ExchangeCredentialService credSvc, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.order"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var (deny, p) = ResolveScopedPayload(req.HttpContext, credSvc, exchange);
            if (deny != null) return Results.Ok(ApiResponseHelper.Error(deny));
            p["symbol"] = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : (string?)null;
            p["status"] = req.Query.TryGetValue("status", out var st) ? st.ToString() : (string?)null;
            p["limit"]  = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50;
            var payload = JsonSerializer.Serialize(p);
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.order", "list_orders", payload));
            return ToResponse(result);
        });

        // ── Account ─────────────────────────────────────────────────

        trading.MapGet("/account", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            ExchangeCredentialService credSvc, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var (deny, p) = ResolveScopedPayload(req.HttpContext, credSvc, exchange);
            if (deny != null) return Results.Ok(ApiResponseHelper.Error(deny));
            var payload = JsonSerializer.Serialize(p);
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_account", payload));
            return ToResponse(result);
        });

        trading.MapGet("/positions", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            ExchangeCredentialService credSvc, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var (deny, p) = ResolveScopedPayload(req.HttpContext, credSvc, exchange);
            if (deny != null) return Results.Ok(ApiResponseHelper.Error(deny));
            var payload = JsonSerializer.Serialize(p);
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_positions", payload));
            return ToResponse(result);
        });

        trading.MapGet("/trades", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            ExchangeCredentialService credSvc, HttpRequest req, CancellationToken ct) =>
        {
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "alpaca";
            var (deny, p) = ResolveScopedPayload(req.HttpContext, credSvc, exchange);
            if (deny != null) return Results.Ok(ApiResponseHelper.Error(deny));
            p["symbol"] = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : (string?)null;
            p["limit"]  = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50;
            var payload = JsonSerializer.Serialize(p);
            var result = await dispatcher.DispatchAsync(BuildRequest(req.HttpContext, "trading.account", "get_trades", payload));
            return ToResponse(result);
        });

        // PnL summary：聚合一段時間的成交、計算總 realized PnL / 勝率 / profit factor。
        // 給 dashboard 顯示「今天 / 本週 / 全期」績效、給 LLM 寫日報。
        trading.MapGet("/pnl-summary", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpRequest req, CancellationToken ct) =>
        {
            // 多用戶:本地交易歷史是隱私資料,必須登入(未認證會讓 owner filter 拿不到 principal)。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(req.HttpContext)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
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
                owner_principal_id = TradeHistoryOwnerFilter(req.HttpContext),
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
            // ?dry=true → 只回 summary、不推 Discord/LINE(預覽/測試不洗版)
            var dry = req.Query.TryGetValue("dry", out var d) && (d.ToString() == "true" || d.ToString() == "1");
            var (ok, summary) = await daily.BuildAndPushAsync(hours, ct, push: !dry);
            return Results.Ok(ApiResponseHelper.Success(new { pushed = ok && !dry, dry, summary }));
        });

        // 手動觸發 shadow scanner 週報(預覽 / 隔週手動跑用;?dry=true 不推 Discord)
        trading.MapPost("/push-scanner-shadow", async (
            Broker.Services.DailyReportService daily, HttpRequest req, CancellationToken ct) =>
        {
            var hours = req.Query.TryGetValue("hours", out var h) && int.TryParse(h.ToString(), out var n) ? n : 24 * 7;
            var dry = req.Query.TryGetValue("dry", out var d) && (d.ToString() == "true" || d.ToString() == "1");
            var (ok, summary) = await daily.BuildAndPushScannerShadowAsync(hours, ct, push: !dry);
            return Results.Ok(ApiResponseHelper.Success(new { pushed = ok && !dry, dry, summary }));
        });

        // 手動觸發 真錢腿 backtest vs live 背離檢查(?dry=true 不推、只回 summary;預設窗 90d)
        trading.MapPost("/push-live-divergence", async (
            Broker.Services.DailyReportService daily, HttpRequest req, CancellationToken ct) =>
        {
            var hours = req.Query.TryGetValue("hours", out var h) && int.TryParse(h.ToString(), out var n) ? n : 24 * 90;
            var dry = req.Query.TryGetValue("dry", out var d) && (d.ToString() == "true" || d.ToString() == "1");
            var (ok, summary, diverged) = await daily.BuildAndPushLiveDivergenceAsync(hours, ct, push: !dry);
            return Results.Ok(ApiResponseHelper.Success(new { pushed = ok && !dry && diverged > 0, dry, diverged, summary }));
        });

        // 手動觸發台股資金流彙整(預覽 / 立刻看用;?dry=true 不推、只回 summary;往回找最近交易日)
        trading.MapPost("/push-tw-fundflow", async (
            Broker.Services.TwFundFlowService twff, HttpRequest req, CancellationToken ct) =>
        {
            var dry = req.Query.TryGetValue("dry", out var d) && (d.ToString() == "true" || d.ToString() == "1");
            var lookback = req.Query.TryGetValue("lookback", out var l) && int.TryParse(l.ToString(), out var n) ? n : 7;
            var (ok, summary, hadData) = await twff.BuildAndPushAsync(push: !dry, maxLookbackDays: lookback, ct);
            return Results.Ok(ApiResponseHelper.Success(new { pushed = ok && !dry && hadData, dry, had_data = hadData, summary }));
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

        // ── Trade journal CSV export ──
        // 把本地 DB 的 trades 表（含 strategy attribution）匯成 CSV、給離線分析 / 報告附錄用。
        // 不過 ApprovalGate（純讀 + audit）、但要 X-Internal-Bot-Token 或 cookie session。
        trading.MapGet("/trades/export.csv", async (
            IWorkerRegistry registry, IExecutionDispatcher dispatcher,
            HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            // 多用戶:交易匯出是隱私資料,必須登入(否則 owner filter 拿不到 principal)。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            if (!registry.HasAvailableWorker("trading.account"))
                return Results.Ok(ApiResponseHelper.Error("trading-worker not connected"));

            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "bingx";
            var symbol = req.Query.TryGetValue("symbol", out var s) ? s.ToString() : (string?)null;
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 1000;
            DateTime? since = null;
            if (req.Query.TryGetValue("since", out var sn) && DateTime.TryParse(sn.ToString(), out var snDt))
                since = snDt.ToUniversalTime();

            var payload = JsonSerializer.Serialize(new { exchange, symbol, limit, since = since?.ToString("o"),
                owner_principal_id = TradeHistoryOwnerFilter(ctx) });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.account", "get_trade_history", payload));
            if (!result.Success)
                return Results.Ok(ApiResponseHelper.Error(result.ErrorMessage ?? "get_trade_history failed"));

            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            if (!doc.TryGetProperty("trades", out var tradesEl) || tradesEl.ValueKind != JsonValueKind.Array)
                return Results.Text("trade_id,order_id,symbol,exchange,side,quantity,price,fee,realized_pnl,strategy,executed_at\n",
                    "text/csv");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("trade_id,order_id,symbol,exchange,side,quantity,price,fee,realized_pnl,strategy,executed_at");
            foreach (var t in tradesEl.EnumerateArray())
            {
                string Esc(string? v) => v == null ? "" : (v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v);
                string Get(string p) => t.TryGetProperty(p, out var x) ? (x.ValueKind == JsonValueKind.Null ? "" : x.ToString()) : "";
                sb.AppendLine(string.Join(",",
                    Esc(Get("trade_id")),
                    Esc(Get("order_id")),
                    Esc(Get("symbol")),
                    Esc(Get("exchange")),
                    Esc(Get("side")),
                    Get("quantity"),
                    Get("price"),
                    Get("fee"),
                    Get("realized_pnl"),
                    Esc(Get("strategy")),
                    Esc(Get("executed_at"))));
            }
            var filename = $"trades-{exchange}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", filename);
        });

        // ── Per-strategy 實盤 P&L 聚合（--allocate 的 forward 證據源）──
        // loopback-only（純讀聚合 stats、給 VPS 上的 --allocate / cron 用、不對外）。
        // 從本地 trades 表（含 strategy + realized_pnl）按策略聚合過去 N 天的實盤成交。
        // 用途：把「實盤 forward 表現」當回測之外的第二證據 → 回測過但實盤賠 = 過擬合/regime 破。
        trading.MapGet("/strategy-pnl", async (
            IExecutionDispatcher dispatcher, HttpContext ctx, HttpRequest req, CancellationToken ct) =>
        {
            if (!System.Net.IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? System.Net.IPAddress.None))
                return Results.StatusCode(403);

            var days = req.Query.TryGetValue("days", out var d) && int.TryParse(d, out var dv)
                ? Math.Clamp(dv, 1, 365) : 30;
            var since = DateTime.UtcNow.AddDays(-days);
            HashSet<string>? exFilter = null;
            if (req.Query.TryGetValue("exchanges", out var exq))
            {
                var list = exq.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (list.Length > 0) exFilter = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }

            var payload = JsonSerializer.Serialize(new { exchange = (string?)null, since = since.ToString("o"), limit = 100000 });
            var result = await dispatcher.DispatchAsync(BuildRequest(ctx, "trading.account", "get_trade_history", payload));
            if (!result.Success)
                return Results.Ok(ApiResponseHelper.Error(result.ErrorMessage ?? "get_trade_history failed"));

            var doc = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement;
            var outp = AggregateStrategyPnl(doc, exFilter);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                window_days = days, since, exchanges = exFilter?.OrderBy(x => x).ToList(),
                count = outp.Count, strategies = outp,
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
            string exchange, HttpContext ctx, Broker.Services.BalanceAnchorService svc, Broker.Services.AutoTraderService at) =>
        {
            // 2026-06-03 安全:風險錨/申報資金是 operator 級設定 → admin only(原本無 auth、洩露 capital)。
            if (!RequestBodyHelper.IsAdmin(ctx))
                return Results.Json(ApiResponseHelper.Error("admin required", 403), statusCode: 403);
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
            // 2026-06-03 安全:這是改真錢風險錨(declared capital → 影響 sizing)的寫入 → admin only(原本無 auth)。
            if (!RequestBodyHelper.IsAdmin(req.HttpContext))
                return Results.Json(ApiResponseHelper.Error("admin required", 403), statusCode: 403);
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
            // 2026-06-03 安全:策略訊號矩陣 → 至少要登入(defense-in-depth、原本無 auth)。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
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
    /// per-strategy 已實現 P&L 聚合。
    /// - perp(realized_pnl 已由 FillPoller 算好、非 null)→ 直接加總。
    /// - spot(realized_pnl 為 null,如 paper alpaca/binance)→ 用 FIFO 成本基礎重放
    ///   每個 (exchange,symbol) 的買賣序列算已實現:sell 時 (proceeds−lot成本)×qty、含手續費。
    ///   → 讓 paper 現貨也有 forward P&L,當 --allocate 的第二證據源。
    internal static List<object> AggregateStrategyPnl(JsonElement doc, HashSet<string>? exFilter)
    {
        var agg = new Dictionary<string, (int n, decimal pnl, int wins, HashSet<string> ex)>(StringComparer.OrdinalIgnoreCase);
        void Add(string strat, decimal pnl, string ex)
        {
            (int n, decimal pnl, int wins, HashSet<string> ex) c = agg.TryGetValue(strat, out var e)
                ? e : (0, 0m, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            c.n++; c.pnl += pnl; if (pnl > 0m) c.wins++; c.ex.Add(ex);
            agg[strat] = c;
        }

        if (!doc.TryGetProperty("trades", out var trades) || trades.ValueKind != JsonValueKind.Array)
            return new List<object>();

        // 解析成記錄
        var recs = new List<(string strat, string ex, string sym, string side, decimal qty, decimal price, decimal fee, decimal? pnl, string ts)>();
        foreach (var t in trades.EnumerateArray())
        {
            var strat = t.TryGetProperty("strategy", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
            if (string.IsNullOrEmpty(strat)) continue;
            var ex = t.TryGetProperty("exchange", out var exv) ? exv.GetString() ?? "" : "";
            if (exFilter != null && !exFilter.Contains(ex)) continue;
            decimal Num(string p) => t.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDecimal() : 0m;
            decimal? pnl = t.TryGetProperty("realized_pnl", out var rp) && rp.ValueKind == JsonValueKind.Number ? rp.GetDecimal() : (decimal?)null;
            var side = t.TryGetProperty("side", out var sd) ? (sd.GetString() ?? "").ToLowerInvariant() : "";
            var ts = t.TryGetProperty("executed_at", out var ea) ? ea.GetString() ?? "" : "";
            recs.Add((strat!, ex, t.TryGetProperty("symbol", out var sy) ? sy.GetString() ?? "" : "", side, Num("quantity"), Num("price"), Num("fee"), pnl, ts));
        }

        // perp:realized_pnl 已算好 → 直接加總
        foreach (var r in recs.Where(r => r.pnl.HasValue))
            Add(r.strat, r.pnl!.Value, r.ex);

        // spot:realized_pnl 為 null → 按 (exchange,symbol) FIFO 重放
        foreach (var grp in recs.Where(r => !r.pnl.HasValue).GroupBy(r => (r.ex, r.sym)))
        {
            var lots = new List<(decimal qty, decimal costPerUnit)>();   // FIFO 買入批
            foreach (var r in grp.OrderBy(r => r.ts, StringComparer.Ordinal))
            {
                if (r.qty <= 0m) continue;
                if (r.side == "buy")
                {
                    lots.Add((r.qty, r.price + r.fee / r.qty));   // 成本含手續費
                }
                else if (r.side == "sell")
                {
                    var proceedsPerUnit = r.price - r.fee / r.qty;
                    var remaining = r.qty;
                    decimal realized = 0m;
                    while (remaining > 0m && lots.Count > 0)
                    {
                        var lot = lots[0];
                        var m = Math.Min(remaining, lot.qty);
                        realized += (proceedsPerUnit - lot.costPerUnit) * m;
                        remaining -= m;
                        if (m >= lot.qty) lots.RemoveAt(0);
                        else lots[0] = (lot.qty - m, lot.costPerUnit);
                    }
                    // 有對應到買入批才算一筆已實現(沒庫存的裸賣不計、避免噪音)
                    if (r.qty - remaining > 0m) Add(r.strat, realized, r.ex);
                }
            }
        }

        return agg.OrderByDescending(kv => kv.Value.n).Select(kv => (object)new
        {
            strategy = kv.Key,
            trades = kv.Value.n,
            realized_pnl = Math.Round(kv.Value.pnl, 4),
            wins = kv.Value.wins,
            win_rate = kv.Value.n > 0 ? Math.Round((decimal)kv.Value.wins / kv.Value.n, 3) : 0m,
            exchanges = kv.Value.ex.OrderBy(x => x).ToList(),
        }).ToList();
    }

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

    /// <summary>
    /// 多用戶隱私隔離 + 安全閘:用 caller 自己的 principal 解析其交易所憑證、把 __credentials 拼進 payload,
    /// 讓 worker 走「用戶自己的帳戶」查 account/positions/orders/trades —— 每個朋友只看到自己的倉。
    /// 回傳 (Deny, Payload):
    ///   - 解析到自有憑證 → 注入 __credentials、Deny=null。
    ///   - 沒自有憑證 + caller 是 admin → Deny=null、不帶 __credentials → worker fallback env 預設
    ///     (單用戶=你、向後相容)。
    ///   - 沒自有憑證 + caller 非 admin → Deny=錯誤訊息。**絕不 fallback env 預設**,
    ///     否則朋友會掉進共用帳戶、看到甚至下單到別人(你)的真錢/紙上帳戶(Gap 1.5 安全洞)。
    /// </summary>
    private static (string? Deny, Dictionary<string, object?> Payload) ResolveScopedPayload(
        HttpContext ctx, ExchangeCredentialService credSvc, string exchange)
    {
        var d = new Dictionary<string, object?> { ["exchange"] = exchange };
        var principalId = RequestBodyHelper.GetPrincipalId(ctx);
        if (!string.IsNullOrEmpty(principalId))
        {
            var dec = credSvc.Resolve(principalId, exchange);
            if (dec != null)
            {
                d["__credentials"] = new { api_key = dec.ApiKey, api_secret = dec.ApiSecret, is_demo = dec.IsDemo };
                return (null, d);
            }
        }
        if (!RequestBodyHelper.IsAdmin(ctx))
            return ($"未註冊 {exchange} 交易所憑證:請先到「交易所憑證」設定你自己的 API key。" +
                    "系統不會用共用 / 預設帳戶代查,以免看到別人的倉。", d);
        return (null, d);  // admin 無自有憑證 → env 預設(你的帳戶,單用戶行為不變)
    }

    /// <summary>
    /// 多用戶本地交易歷史隱私 filter(get_trade_history 的 owner_principal_id):
    /// admin → null(不過濾、看全部);非 admin → caller principal(只看自己的成交)。
    /// **fail-closed**:非 admin 又拿不到 principal(未認證)→ 回配不到任何 owner 的 sentinel,
    /// 絕不回空字串(空會被 GetTrades 當「不過濾」→ 洩漏全部成交,2026-06-02 端到端測抓到的漏洞)。
    /// loopback 內部分析(strategy-pnl)不套此 filter、需全量。
    /// </summary>
    private static string? TradeHistoryOwnerFilter(HttpContext ctx)
    {
        if (RequestBodyHelper.IsAdmin(ctx)) return null;            // admin 看全部
        var pid = RequestBodyHelper.GetPrincipalId(ctx);
        return string.IsNullOrEmpty(pid) ? "__unauthenticated__" : pid;  // 非 admin 無 principal → 配不到任何成交
    }

    // 走 ApprovalAwareResponseHelper：approval gate 卡住的單會被重塑成 status="pending_approval"
    // 結構化回應、不是「失敗 + 字串」、讓 dashboard / bot 兩端都能分得出「真失敗 vs 卡審」。
    private static IResult ToResponse(ExecutionResult result)
        => ApprovalAwareResponseHelper.Shape(result);
}
