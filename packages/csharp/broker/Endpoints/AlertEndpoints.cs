using Broker.Helpers;
using Broker.Services;
using System.Text.Json;

namespace Broker.Endpoints;

public static class AlertEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var alert = group.MapGroup("/alerts");

        alert.MapPost("/", async (PriceAlertService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body).RootElement;
            var symbol    = doc.TryGetProperty("symbol",    out var s) ? s.GetString() ?? "" : "";
            var condition = doc.TryGetProperty("condition", out var c) ? c.GetString() ?? "above" : "above";
            var target    = doc.TryGetProperty("target",    out var t) ? t.GetDecimal() : 0;
            var note      = doc.TryGetProperty("note",      out var n) ? n.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(symbol) || target <= 0)
                return Results.Ok(ApiResponseHelper.Error("Missing symbol or target"));
            var id = svc.AddAlert(symbol, condition, target, note);
            return Results.Ok(ApiResponseHelper.Success(new { id, symbol, condition, target }));
        });

        alert.MapDelete("/{id}", (string id, PriceAlertService svc) =>
        {
            var removed = svc.RemoveAlert(id);
            return Results.Ok(ApiResponseHelper.Success(new { removed, id }));
        });

        alert.MapGet("/", (PriceAlertService svc) =>
        {
            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = svc.Alerts.Count,
                alerts = svc.Alerts.Values.Select(a => new { a.Id, a.Symbol, a.Condition, target = a.TargetPrice, a.Note, a.CreatedAt }),
                triggered = svc.History.Take(20).Select(e => new { e.Id, e.Symbol, e.Condition, target = e.TargetPrice, current = e.CurrentPrice, e.Note, e.TriggeredAt }),
            }));
        });
    }
}
