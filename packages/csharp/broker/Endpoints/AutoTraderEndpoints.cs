using Broker.Helpers;
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

        at.MapGet("/status", (AutoTraderService svc) =>
        {
            var watchList = svc.WatchList.Select(kv => new
            {
                symbol = kv.Value.Symbol,
                exchange = kv.Value.Exchange,
                strategy = kv.Value.Strategy,
                quantity = kv.Value.Quantity,
                active = kv.Value.Active,
                mode = kv.Value.Mode,
                leverage = kv.Value.Leverage,
                last_signal = kv.Value.LastSignal,
                last_confidence = kv.Value.LastConfidence,
                last_check = kv.Value.LastCheck,
            });

            return Results.Ok(ApiResponseHelper.Success(new
            {
                enabled = svc.IsEnabled,
                interval_seconds = svc.IntervalSeconds,
                watch_count = svc.WatchList.Count,
                watch_list = watchList,
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

        at.MapPost("/watch", async (AutoTraderService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body).RootElement;

            var symbol   = doc.TryGetProperty("symbol",   out var s) ? s.GetString() ?? "" : "";
            var exchange = doc.TryGetProperty("exchange",  out var e) ? e.GetString() ?? "alpaca" : "alpaca";
            var strategy = doc.TryGetProperty("strategy",  out var st) ? st.GetString() ?? "composite" : "composite";
            var quantity = doc.TryGetProperty("quantity",   out var q) ? q.GetDecimal() : 1m;
            // Phase 3：perpetual 模式 + leverage（向後相容、不傳就走 spot）
            var mode     = doc.TryGetProperty("mode",      out var m)  ? m.GetString() ?? "spot" : "spot";
            var leverage = doc.TryGetProperty("leverage",  out var lv) && lv.TryGetInt32(out var lvI) ? lvI : 5;

            if (string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("Missing symbol"));

            svc.AddWatch(symbol, exchange, strategy, quantity, mode, leverage);
            return Results.Ok(ApiResponseHelper.Success(new { symbol, exchange, strategy, quantity, mode, leverage }));
        });

        at.MapDelete("/watch", async (AutoTraderService svc, HttpRequest req) =>
        {
            var symbol   = req.Query.TryGetValue("symbol",   out var s) ? s.ToString() : "";
            var exchange = req.Query.TryGetValue("exchange",  out var e) ? e.ToString() : "alpaca";

            if (string.IsNullOrEmpty(symbol))
                return Results.Ok(ApiResponseHelper.Error("Missing symbol"));

            var removed = svc.RemoveWatch(symbol, exchange);
            return Results.Ok(ApiResponseHelper.Success(new { removed, symbol, exchange }));
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
