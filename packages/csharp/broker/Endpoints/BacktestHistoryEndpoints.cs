using Broker.Helpers;
using Broker.Services;
using System.Text.Json;

namespace Broker.Endpoints;

public static class BacktestHistoryEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var bt = group.MapGroup("/backtest-history");

        bt.MapPost("/", async (BacktestHistoryService svc, HttpRequest req) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body).RootElement;
            var strategy = doc.TryGetProperty("strategy", out var s) ? s.GetString() ?? "" : "";
            var symbol   = doc.TryGetProperty("symbol",   out var sy) ? sy.GetString() ?? "" : "";
            var id       = $"bt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
            svc.Save(id, strategy, symbol, doc);
            return Results.Ok(ApiResponseHelper.Success(new { id, strategy, symbol }));
        });

        bt.MapGet("/", (BacktestHistoryService svc) =>
        {
            var records = svc.GetAll().Select(r => new
            {
                r.Id, r.Strategy, r.Symbol, r.SavedAt,
                total_return_pct = r.Data.TryGetProperty("total_return_pct", out var tr) ? tr.GetDecimal() : 0,
                win_rate         = r.Data.TryGetProperty("win_rate", out var wr) ? wr.GetDecimal() : 0,
                sharpe_ratio     = r.Data.TryGetProperty("sharpe_ratio", out var sr) ? sr.GetDecimal() : 0,
                total_trades     = r.Data.TryGetProperty("total_trades", out var tt) ? tt.GetInt32() : 0,
            });
            return Results.Ok(ApiResponseHelper.Success(new { count = records.Count(), records }));
        });

        bt.MapGet("/{id}", (string id, BacktestHistoryService svc) =>
        {
            var record = svc.Get(id);
            if (record == null) return Results.Ok(ApiResponseHelper.Error("Not found"));
            return Results.Ok(ApiResponseHelper.Success(record.Data));
        });
    }
}
