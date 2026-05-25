using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// AutoTrader API — 自動交易迴圈控制
///
/// POST /api/v1/auto-trader/enable          — 啟用
/// POST /api/v1/auto-trader/disable         — 停用
/// GET  /api/v1/auto-trader/status          — 狀態
/// POST /api/v1/auto-trader/watch           — 新增監控 symbol
/// DELETE /api/v1/auto-trader/watch          — 移除監控
/// POST /api/v1/auto-trader/watch/shadow     — 切換既有 watch 的 shadow 旗標（👻 影子 ↔ 🔴 真錢）
/// POST /api/v1/auto-trader/interval        — 設定間隔
/// GET  /api/v1/auto-trader/logs            — 交易日誌
/// </summary>
public static class AutoTraderEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var at = group.MapGroup("/auto-trader");

        at.MapPost("/enable", (AutoTraderService svc) =>
        {
            svc.Enable();
            return Results.Ok(ApiResponseHelper.Success(new { enabled = true }));
        });

        at.MapPost("/disable", (AutoTraderService svc) =>
        {
            svc.Disable();
            return Results.Ok(ApiResponseHelper.Success(new { enabled = false }));
        });

        at.MapGet("/status", (AutoTraderService svc, HttpContext ctx) =>
        {
            // Phase A2：admin 看全部、user 只看自己 owner 的 watches。
            // 未登入（legacy / 內部 health check）視為 admin，不做過濾——避免破舊呼叫。
            var (pid, role) = ctx.GetCurrentUser();
            var isAdminOrLegacy = pid == null || string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
            var visibleWatches = svc.WatchList.Values
                .Where(w => isAdminOrLegacy || string.Equals(w.OwnerPrincipalId, pid, StringComparison.Ordinal))
                .ToList();
            var watchList = visibleWatches.Select(w => new
            {
                symbol = w.Symbol,
                exchange = w.Exchange,
                strategy = w.Strategy,
                quantity = w.Quantity,
                active = w.Active,
                mode = w.Mode,
                leverage = w.Leverage,
                budget_pct = w.BudgetPct,
                htf_interval = w.HtfInterval,
                shadow = w.Shadow,
                owner_principal_id = w.OwnerPrincipalId,
                last_signal = w.LastSignal,
                last_confidence = w.LastConfidence,
                last_check = w.LastCheck,
            });

            return Results.Ok(ApiResponseHelper.Success(new
            {
                enabled = svc.IsEnabled,
                interval_seconds = svc.IntervalSeconds,
                watch_count = visibleWatches.Count,
                watch_list = watchList,
                viewer = new { principal_id = pid, role, scope = isAdminOrLegacy ? "all" : "self" },
                dev_force_action = svc.DevForceAction,   // 非 null 表示 dev override 啟用中
                min_confidence = svc.MinConfidence,
                max_portfolio_dd_pct = svc.MaxPortfolioDdPct,
                circuit_breakers = svc.CircuitBreakerSnapshot,
                protection_config = new
                {
                    initial_sl_pct        = svc.PositionProtectionConfig.InitialSlPct,
                    partial_exit_pct      = svc.PositionProtectionConfig.PartialExitPct,
                    partial_exit_ratio    = svc.PositionProtectionConfig.PartialExitRatio,
                    breakeven_trigger_pct = svc.PositionProtectionConfig.BreakevenTriggerPct,
                    breakeven_buffer_pct  = svc.PositionProtectionConfig.BreakevenBufferPct,
                },
                position_states = svc.PositionStates.ToDictionary(
                    kv => kv.Key,
                    kv => (object)new
                    {
                        entry_price     = kv.Value.EntryPrice,
                        peak_price      = kv.Value.PeakPrice,
                        sl_price        = kv.Value.SlPrice,
                        partial_exited  = kv.Value.PartialExited,
                        be_moved        = kv.Value.BeMoved,
                        created_at      = kv.Value.CreatedAt,
                        updated_at      = kv.Value.UpdatedAt,
                    }),
                sl_flush = new
                {
                    threshold        = svc.SlFlushThreshold,
                    window_minutes   = svc.SlFlushWindowMinutes,
                    triggered        = svc.SlFlushTriggered,
                    triggered_at     = svc.SlFlushTriggeredAt,
                    recent_count     = svc.RecentSlHits.Count,
                    recent_hits      = svc.RecentSlHits.Select(h => new { h.Exchange, h.Symbol, h.At }).ToList(),
                },
            }));
        });

        // B3: 手動 reset SL flush 凍結狀態（連環 SL 觸發 auto-trader 自動 disable 後、
        // user 確認沒問題、手動按 reset 清掉滑動視窗 + 復原 enabled 狀態）
        at.MapPost("/sl-flush/reset", (AutoTraderService svc) =>
        {
            svc.ResetSlFlush();
            return Results.Ok(ApiResponseHelper.Success(new { reset = true, sl_flush_triggered = svc.SlFlushTriggered }));
        });

        at.MapPost("/watch", async (AutoTraderService svc, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body).RootElement;

            var symbol   = doc.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
            var exchange = doc.TryGetProperty("exchange",  out var e) ? e.GetString() ?? "alpaca" : "alpaca";
            var strategy = doc.TryGetProperty("strategy",  out var st) ? st.GetString() ?? "composite" : "composite";
            var quantity = doc.TryGetProperty("quantity",   out var q) ? q.GetDecimal() : 1m;
            var mode     = doc.TryGetProperty("mode",      out var m)  ? m.GetString() ?? "spot" : "spot";
            var leverage = doc.TryGetProperty("leverage",  out var lv) && lv.TryGetInt32(out var lvI) ? lvI : 5;
            // Batch C+++ Phase 2：HTF（大週期）確認週期、空字串/null = 不做 HTF
            var htfInterval = doc.TryGetProperty("htf_interval", out var hi) ? hi.GetString() : null;
            // Shadow（影子）模式：true = 評估訊號但絕不下真單。新策略上線前對帳用。預設 false = 真交易。
            var shadow = doc.TryGetProperty("shadow", out var sh) && sh.ValueKind == JsonValueKind.True;

            if (string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("Missing symbol"));

            // 擁有者 = 當前登入者；未登入視同 admin user prn_dashboard（向後相容、本機 dev）
            var (pid, _) = ctx.GetCurrentUser();
            var owner = pid ?? "prn_dashboard";

            svc.AddWatch(symbol, exchange, strategy, quantity, mode, leverage, owner, htfInterval, shadow);
            return Results.Ok(ApiResponseHelper.Success(new {
                symbol, exchange, strategy, quantity, mode, leverage,
                htf_interval = htfInterval,
                shadow,
                owner_principal_id = owner,
            }));
        });

        at.MapDelete("/watch", async (AutoTraderService svc, HttpContext ctx) =>
        {
            await Task.CompletedTask;
            var symbol   = ctx.Request.Query.TryGetValue("symbol",   out var s) ? s.ToString() : "";
            var exchange = ctx.Request.Query.TryGetValue("exchange",  out var e) ? e.ToString() : "alpaca";

            if (string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("Missing symbol"));

            var (pid, role) = ctx.GetCurrentUser();
            var isAdminOrLegacy = pid == null || string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
            var (removed, reason) = svc.RemoveWatch(symbol, exchange, pid, isAdminOrLegacy);
            if (!removed && reason == "forbidden")
                return Results.Json(ApiResponseHelper.Error("Forbidden: not your watch", 403), statusCode: 403);
            return Results.Ok(ApiResponseHelper.Success(new { removed, symbol, exchange, reason }));
        });

        // 切換既有 watch 的 shadow 旗標（👻 影子 ↔ 🔴 真錢）。
        // shadow=false = 轉真錢「武裝」：前端跳確認框；路徑前綴 /api/v1/auto-trader/watch
        // 已被 EmergencyGate 的 KillSwitch/ReadOnly 蓋到（StartsWith 比對）→ 緊急狀態下無法轉真錢。
        at.MapPost("/watch/shadow", async (AutoTraderService svc, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body).RootElement;

            var symbol   = doc.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
            var exchange = doc.TryGetProperty("exchange",  out var e) ? e.GetString() ?? "alpaca" : "alpaca";
            // 真錢武裝端點：shadow 必須明確帶 bool，缺值不預設（避免「漏帶 → 被當 false → 誤轉真錢」）
            if (!doc.TryGetProperty("shadow", out var sh)
                || (sh.ValueKind != JsonValueKind.True && sh.ValueKind != JsonValueKind.False))
                return Results.Ok(ApiResponseHelper.Error("Missing shadow boolean"));
            var shadow = sh.ValueKind == JsonValueKind.True;

            if (string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("Missing symbol"));

            var (pid, role) = ctx.GetCurrentUser();
            var isAdminOrLegacy = pid == null || string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
            var (ok, reason) = svc.SetShadow(symbol, exchange, shadow, pid, isAdminOrLegacy);
            if (!ok && reason == "forbidden")
                return Results.Json(ApiResponseHelper.Error("Forbidden: not your watch", 403), statusCode: 403);
            if (!ok && reason == "not_found")
                return Results.Ok(ApiResponseHelper.Error("Watch not found"));
            return Results.Ok(ApiResponseHelper.Success(new { symbol, exchange, shadow }));
        });

        at.MapPost("/interval", async (AutoTraderService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body).RootElement;
            var seconds = doc.TryGetProperty("seconds", out var s) ? s.GetInt32() : 300;

            svc.SetInterval(seconds);
            return Results.Ok(ApiResponseHelper.Success(new { interval_seconds = svc.IntervalSeconds }));
        });

        at.MapGet("/logs", (AutoTraderService svc, HttpRequest req) =>
        {
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? n : 50;
            var logs = svc.RecentLogs.Take(limit).Select(log => new
            {
                symbol = log.Symbol, exchange = log.Exchange,
                action = log.Action, message = log.Message,
                time = log.Time,
            });
            return Results.Ok(ApiResponseHelper.Success(new { count = logs.Count(), logs }));
        });

        // 監控儀錶板彙整(trading v2)：account + 每腿健康(持倉/訊號/原因/保護)+ forward + xsmom。
        // server 端把多來源 join 好、頁面一個 fetch 渲染。
        at.MapGet("/dashboard", async (AutoTraderService svc, BrokerCore.Data.BrokerDb db,
            IExecutionDispatcher dispatcher, IWorkerRegistry registry, HttpRequest req, CancellationToken ct) =>
        {
            var exchange = req.Query.TryGetValue("exchange", out var ex) ? ex.ToString() : "bingx";
            ApprovedRequest Rq(string cap, string route, object payload) => new()
            {
                RequestId = Guid.NewGuid().ToString("N"), CapabilityId = cap, Route = route,
                Payload = JsonSerializer.Serialize(payload), Scope = "{}",
                PrincipalId = "system", TaskId = "dashboard", SessionId = "dashboard",
            };
            static decimal D(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
            static string Sv(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

            // 1. 持倉(trading.perpetual get_positions — 最完整:qty/entry/mark/pnl%/liq)
            var posBySym = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (registry.HasAvailableWorker("trading.perpetual"))
            {
                try
                {
                    var pr = await dispatcher.DispatchAsync(Rq("trading.perpetual", "get_positions", new { exchange }));
                    if (pr.Success)
                    {
                        var pd = JsonDocument.Parse(pr.ResultPayload ?? "{}").RootElement;
                        if (pd.TryGetProperty("positions", out var pa) && pa.ValueKind == JsonValueKind.Array)
                            foreach (var p in pa.EnumerateArray())
                            {
                                var s = Sv(p, "symbol");
                                if (!string.IsNullOrEmpty(s) && Math.Abs(D(p, "quantity")) > 0m) posBySym[s] = p.Clone();
                            }
                    }
                }
                catch { }
            }
            // sl_price 從 perp_position_state 補(get_positions 沒有 SL)
            var slBySym = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var r in db.Query<BrokerCore.Models.PerpetualPositionStateEntry>("SELECT * FROM perp_position_state WHERE exchange = @ex", new { ex = exchange })) slBySym[r.Symbol] = r.SlPrice; }
            catch { }

            // 2. trades(forward + 今日 P&L)
            JsonElement tradesRoot = JsonDocument.Parse("{}").RootElement;
            if (registry.HasAvailableWorker("trading.account"))
            {
                try
                {
                    var tr = await dispatcher.DispatchAsync(Rq("trading.account", "get_trade_history",
                        new { exchange = (string?)null, since = DateTime.UtcNow.AddDays(-30).ToString("o"), limit = 100000 }));
                    if (tr.Success) tradesRoot = JsonDocument.Parse(tr.ResultPayload ?? "{}").RootElement;
                }
                catch { }
            }
            decimal dayPnl = 0m; int dayTrades = 0; var midnight = DateTime.UtcNow.Date;
            if (tradesRoot.TryGetProperty("trades", out var tarr) && tarr.ValueKind == JsonValueKind.Array)
                foreach (var t in tarr.EnumerateArray())
                {
                    if (!string.Equals(Sv(t, "exchange"), exchange, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!t.TryGetProperty("realized_pnl", out var rp) || rp.ValueKind != JsonValueKind.Number) continue;
                    if (t.TryGetProperty("executed_at", out var ea) && DateTime.TryParse(ea.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) && dt >= midnight)
                    { dayPnl += rp.GetDecimal(); dayTrades++; }
                }
            var forward = TradingEndpoints.AggregateStrategyPnl(tradesRoot, null);

            // 3. 每腿
            var logBySym = svc.RecentLogs.GroupBy(l => l.Symbol).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Time).First());
            decimal totalNotional = 0m;
            var legs = new List<object>();
            foreach (var w in svc.WatchList.Values.Where(w => w.Active && string.Equals(w.Exchange, exchange, StringComparison.OrdinalIgnoreCase)).OrderByDescending(w => w.BudgetPct))
            {
                bool hasPos = posBySym.TryGetValue(w.Symbol, out var p);
                decimal qty = hasPos ? Math.Abs(D(p, "quantity")) : 0m, mark = hasPos ? D(p, "mark_price") : 0m;
                if (hasPos) totalNotional += qty * mark;
                logBySym.TryGetValue(w.Symbol, out var lg);
                legs.Add(new
                {
                    symbol = w.Symbol, strategy = w.Strategy, mode = w.Mode, budget_pct = w.BudgetPct,
                    signal = w.LastSignal ?? "?", confidence = w.LastConfidence,
                    has_position = hasPos, side = hasPos ? Sv(p, "side") : "",
                    entry = hasPos ? D(p, "avg_entry_price") : 0m, mark,
                    pnl_pct = hasPos ? D(p, "unrealized_pnl_pct") : 0m,
                    liq = hasPos ? D(p, "liquidation_price") : 0m, liq_dist = hasPos ? D(p, "liquidation_distance_pct") : 0m,
                    sl = slBySym.TryGetValue(w.Symbol, out var slv) ? slv : 0m,
                    last_action = lg?.Action ?? "", last_reason = lg?.Message ?? "",
                });
            }
            decimal balance = svc.DeclaredCapital.TryGetValue(exchange.ToLowerInvariant(), out var bal) ? bal : 0m;

            // 4. xsmom shadow
            object? xsmom = null;
            try { if (File.Exists("/data/xsmom-shadow.json")) xsmom = JsonDocument.Parse(File.ReadAllText("/data/xsmom-shadow.json")).RootElement.Clone(); }
            catch { }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                exchange,
                account = new
                {
                    balance = Math.Round(balance, 2),
                    exposure_x = balance > 0 ? Math.Round(totalNotional / balance, 2) : 0m,
                    day_pnl = Math.Round(dayPnl, 2), day_trades = dayTrades,
                },
                legs, forward, xsmom,
            }));
        });
    }
}
