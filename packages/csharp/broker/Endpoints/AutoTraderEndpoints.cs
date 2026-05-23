using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;
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
    }
}
