using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// Scanner Hybrid API(Phase 1 Step C、2026-05-27)
///
/// GET    /api/v1/scanner/legs                          — 列出所有 scanner 定義
/// GET    /api/v1/scanner/active?include_closed=false   — 列出已開 leg(含/不含已 close 的歷史)
/// POST   /api/v1/scanner/legs/{id}/enable              — body { "enabled": true/false }
/// POST   /api/v1/scanner/legs/{id}/shadow              — body { "shadow": true/false }
///   ⚠ shadow=false = 武裝真錢、目前 real dispatch 仍未實作(B.3b)、即使切了也只是 log warning、不下單
///
/// 設計來源:docs/designs/portfolio-scanner-hybrid.md
/// </summary>
public static class ScannerEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var sc = group.MapGroup("/scanner");

        sc.MapGet("/legs", (BrokerDb db) =>
        {
            try
            {
                var legs = db.Query<ScannerLegEntry>("SELECT * FROM scanner_legs ORDER BY id");
                var view = legs.Select(l => new
                {
                    id = l.Id,
                    name = l.Name,
                    strategy = l.Strategy,
                    universe = ParseUniverse(l.Universe),
                    budget_total = l.BudgetTotal,
                    max_concurrent = l.MaxConcurrent,
                    per_leg_cap = l.PerLegCap,
                    mode = l.Mode,
                    interval = l.Interval,
                    leverage = l.Leverage,
                    shadow = l.Shadow,
                    enabled = l.Enabled,
                });
                return Results.Ok(ApiResponseHelper.Success(new { legs = view }));
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error(ex.Message)); }
        });

        sc.MapGet("/active", (BrokerDb db, HttpRequest req) =>
        {
            try
            {
                var includeClosed = string.Equals(req.Query["include_closed"], "true", StringComparison.OrdinalIgnoreCase);
                var sql = includeClosed
                    ? "SELECT * FROM scanner_active_legs ORDER BY opened_at DESC LIMIT 200"
                    : "SELECT * FROM scanner_active_legs WHERE closed_at IS NULL ORDER BY opened_at DESC";
                var legs = db.Query<ScannerActiveLegEntry>(sql);
                var view = legs.Select(a => new
                {
                    id = a.Id,
                    scanner_id = a.ScannerId,
                    symbol = a.Symbol,
                    exchange = a.Exchange,
                    side = a.Side,
                    entry_price = a.EntryPrice,
                    peak_mark = a.PeakMark,
                    entry_signal = a.EntrySignal,
                    entry_confidence = a.EntryConfidence,
                    shadow = a.Shadow,
                    opened_at = a.OpenedAt,
                    closed_at = a.ClosedAt,
                    exit_price = a.ExitPrice,
                    realized_pnl_pct = a.RealizedPnlPct,
                    close_reason = a.CloseReason,
                });
                return Results.Ok(ApiResponseHelper.Success(new { active_legs = view }));
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error(ex.Message)); }
        });

        sc.MapPost("/legs/{id}/enable", async (BrokerDb db, string id, HttpRequest req, HttpContext ctx) =>
        {
            if (!RequestBodyHelper.IsAdmin(ctx))   // [2026-06-10 安全] 真錢腿開關:要 admin(原無檢查)
                return Results.Json(ApiResponseHelper.Error("Forbidden: admin required", 403), statusCode: 403);
            try
            {
                using var doc = await JsonDocument.ParseAsync(req.Body);
                if (!doc.RootElement.TryGetProperty("enabled", out var en) || en.ValueKind != JsonValueKind.True && en.ValueKind != JsonValueKind.False)
                    return Results.Ok(ApiResponseHelper.Error("body 需 { enabled: bool }"));
                var enabled = en.GetBoolean() ? 1 : 0;
                var rows = db.Execute("UPDATE scanner_legs SET enabled = @E, updated_at = @T WHERE id = @Id",
                    new { E = enabled, T = DateTime.UtcNow, Id = id });
                if (rows == 0) return Results.Ok(ApiResponseHelper.Error($"scanner {id} not found"));
                return Results.Ok(ApiResponseHelper.Success(new { id, enabled = enabled == 1 }));
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error(ex.Message)); }
        });

        sc.MapPost("/legs/{id}/shadow", async (BrokerDb db, string id, HttpRequest req, HttpContext ctx) =>
        {
            if (!RequestBodyHelper.IsAdmin(ctx))   // [2026-06-10 安全] 真錢武裝:要 admin(原無檢查)
                return Results.Json(ApiResponseHelper.Error("Forbidden: admin required", 403), statusCode: 403);
            try
            {
                using var doc = await JsonDocument.ParseAsync(req.Body);
                if (!doc.RootElement.TryGetProperty("shadow", out var sh) || sh.ValueKind != JsonValueKind.True && sh.ValueKind != JsonValueKind.False)
                    return Results.Ok(ApiResponseHelper.Error("body 需 { shadow: bool }"));
                var shadow = sh.GetBoolean() ? 1 : 0;
                var rows = db.Execute("UPDATE scanner_legs SET shadow = @S, updated_at = @T WHERE id = @Id",
                    new { S = shadow, T = DateTime.UtcNow, Id = id });
                if (rows == 0) return Results.Ok(ApiResponseHelper.Error($"scanner {id} not found"));
                return Results.Ok(ApiResponseHelper.Success(new { id, shadow = shadow == 1, warning = shadow == 0 ? "real dispatch path B.3b not implemented yet — non-shadow only logs warning, no real orders" : null }));
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error(ex.Message)); }
        });
    }

    private static List<string> ParseUniverse(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }
}
