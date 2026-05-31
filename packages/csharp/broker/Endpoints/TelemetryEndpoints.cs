using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// GET /api/v1/telemetry/history — 唯讀 broker 健康時間序列。
///
/// dashboard(metrics.html)拉這支畫「健康隨時間」折線。資料在 telemetry-worker 的本地
/// telemetry.db,broker 不直接讀 → 派發 telemetry.history(read / auto-approve)給 worker 取回。
/// telemetry-worker 不在線 → 503(dashboard 顯示「未連線」、不崩)。
///
/// query:
///   minutes (預設 180、上限 7d)、limit (預設 1000、上限 5000)
/// </summary>
public static class TelemetryEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/telemetry/history", async (HttpContext ctx, IExecutionDispatcher dispatcher) =>
        {
            var minutes = int.TryParse(ctx.Request.Query["minutes"], out var m) ? Math.Clamp(m, 1, 60 * 24 * 7) : 180;
            var limit   = int.TryParse(ctx.Request.Query["limit"],   out var l) ? Math.Clamp(l, 1, 5000) : 1000;

            var req = new ApprovedRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                CapabilityId = "telemetry.history",
                Route = "query",
                Payload = JsonSerializer.Serialize(new { minutes, limit }),
                Scope = "{}",
                PrincipalId = "system",
                TaskId = "telemetry-dashboard",
                SessionId = "telemetry-dashboard",
            };

            ExecutionResult result;
            try { result = await dispatcher.DispatchAsync(req); }
            catch (Exception ex)
            {
                return Results.Json(ApiResponseHelper.Error("dispatch failed: " + ex.Message, 503), statusCode: 503);
            }

            if (!result.Success)
                return Results.Json(
                    ApiResponseHelper.Error(result.ErrorMessage ?? "telemetry-worker unavailable", 503),
                    statusCode: 503);

            JsonElement data;
            try { data = JsonDocument.Parse(result.ResultPayload ?? "{}").RootElement.Clone(); }
            catch { return Results.Json(ApiResponseHelper.Error("bad telemetry payload", 502), statusCode: 502); }

            return Results.Ok(ApiResponseHelper.Success(data));
        });
    }
}
