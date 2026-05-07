using System.Text.Json;
using Broker.Helpers;
using Broker.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Broker.Endpoints;

/// <summary>
/// 告警系統 endpoints（#2）—— 規則 CRUD + 事件查詢 + acknowledge。
///
/// 路由：
///   GET    /api/v1/alerts/rules                列出所有規則
///   POST   /api/v1/alerts/rules                建規則 {name, condition_type, symbol, exchange, threshold, cooldown_minutes?}
///   PUT    /api/v1/alerts/rules/{id}           更新規則（部分欄位）
///   DELETE /api/v1/alerts/rules/{id}           刪除規則
///   GET    /api/v1/alerts/events?limit=50&unacknowledged_only=true  事件清單
///   POST   /api/v1/alerts/events/{id}/acknowledge  標記已處理
/// </summary>
public static class AlertRulesEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var alerts = group.MapGroup("/alerts");

        // ── Rules CRUD ────────────────────────────────────────────
        alerts.MapGet("/rules", (AlertRulesService svc) =>
        {
            var rules = svc.Rules.Values
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    id = r.Id, name = r.Name,
                    condition_type = r.ConditionType,
                    symbol = r.Symbol, exchange = r.Exchange,
                    threshold = r.Threshold,
                    enabled = r.Enabled,
                    cooldown_minutes = r.CooldownMinutes,
                    last_triggered_at = r.LastTriggeredAt,
                    created_at = r.CreatedAt, updated_at = r.UpdatedAt,
                });
            return Results.Ok(ApiResponseHelper.Success(new { rules }));
        });

        alerts.MapPost("/rules", async (AlertRulesService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            JsonElement doc;
            try { doc = JsonDocument.Parse(body).RootElement; }
            catch { return Results.Ok(ApiResponseHelper.Error("Invalid JSON body")); }

            var name          = doc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var conditionType = doc.TryGetProperty("condition_type", out var ct) ? ct.GetString() ?? "" : "";
            var symbol        = doc.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
            var exchange      = doc.TryGetProperty("exchange", out var ex) ? ex.GetString() ?? "alpaca" : "alpaca";
            var threshold     = doc.TryGetProperty("threshold", out var t) ? t.GetDecimal() : 0m;
            var cooldown      = doc.TryGetProperty("cooldown_minutes", out var cm) ? cm.GetInt32() : 30;

            if (string.IsNullOrWhiteSpace(name))          return Results.Ok(ApiResponseHelper.Error("name required"));
            if (string.IsNullOrWhiteSpace(conditionType)) return Results.Ok(ApiResponseHelper.Error("condition_type required"));
            var validTypes = new[] { "price_above", "price_below", "position_pnl_below", "portfolio_dd_above" };
            if (!validTypes.Contains(conditionType))
                return Results.Ok(ApiResponseHelper.Error($"condition_type must be one of: {string.Join(", ", validTypes)}"));
            if (string.IsNullOrWhiteSpace(symbol) && conditionType != "portfolio_dd_above")
                return Results.Ok(ApiResponseHelper.Error("symbol required for price/pnl rules"));
            if (cooldown < 1 || cooldown > 1440)
                return Results.Ok(ApiResponseHelper.Error("cooldown_minutes must be in [1, 1440]"));

            var rule = svc.Create(name, conditionType, symbol, exchange, threshold, cooldown);
            return Results.Ok(ApiResponseHelper.Success(new { id = rule.Id }));
        });

        alerts.MapPut("/rules/{id}", async (AlertRulesService svc, string id, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            JsonElement doc;
            try { doc = JsonDocument.Parse(body).RootElement; }
            catch { return Results.Ok(ApiResponseHelper.Error("Invalid JSON body")); }

            var ok = svc.Update(id, r =>
            {
                if (doc.TryGetProperty("name", out var n)) r.Name = n.GetString() ?? r.Name;
                if (doc.TryGetProperty("threshold", out var t)) r.Threshold = t.GetDecimal();
                if (doc.TryGetProperty("enabled", out var e)) r.Enabled = e.GetBoolean();
                if (doc.TryGetProperty("cooldown_minutes", out var cm))
                {
                    var v = cm.GetInt32();
                    if (v >= 1 && v <= 1440) r.CooldownMinutes = v;
                }
            });
            return ok
                ? Results.Ok(ApiResponseHelper.Success(new { id }))
                : Results.Ok(ApiResponseHelper.Error($"Rule {id} not found"));
        });

        alerts.MapDelete("/rules/{id}", (AlertRulesService svc, string id) =>
        {
            return svc.Delete(id)
                ? Results.Ok(ApiResponseHelper.Success(new { id }))
                : Results.Ok(ApiResponseHelper.Error($"Rule {id} not found"));
        });

        // ── Events ────────────────────────────────────────────────
        alerts.MapGet("/events", (AlertRulesService svc, HttpRequest req) =>
        {
            var limit = req.Query.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? Math.Clamp(n, 1, 500) : 50;
            var unackOnly = req.Query.TryGetValue("unacknowledged_only", out var u)
                && bool.TryParse(u, out var b) && b;
            var events = svc.GetEvents(limit, unackOnly).Select(e => new
            {
                id = e.Id, rule_id = e.RuleId, rule_name = e.RuleName,
                condition_type = e.ConditionType,
                symbol = e.Symbol, exchange = e.Exchange,
                threshold = e.Threshold, observed_value = e.ObservedValue,
                message = e.Message,
                triggered_at = e.TriggeredAt, acknowledged_at = e.AcknowledgedAt,
            });
            return Results.Ok(ApiResponseHelper.Success(new { events }));
        });

        alerts.MapPost("/events/{id}/acknowledge", (AlertRulesService svc, string id) =>
        {
            return svc.Acknowledge(id)
                ? Results.Ok(ApiResponseHelper.Success(new { id, acknowledged = true }))
                : Results.Ok(ApiResponseHelper.Error($"Event {id} not found"));
        });
    }
}
