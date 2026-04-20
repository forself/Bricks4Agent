using Broker.Helpers;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Registry;
using System.Diagnostics;
using System.Text.Json;

namespace Broker.Endpoints;

/// <summary>
/// 健康檢查 API — 所有 Worker 的連線狀態、延遲、能力清單。
/// GET /api/v1/health/workers
/// </summary>
public static class HealthCheckEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var hc = group.MapGroup("/health");

        hc.MapGet("/workers", async (
            IWorkerRegistry registry,
            IExecutionDispatcher dispatcher,
            CancellationToken ct) =>
        {
            var capabilities = new[]
            {
                ("quote-worker",    "quote.prices",     "get_prices"),
                ("strategy-worker", "strategy.signal",  "list"),
                ("risk-worker",     "risk.check",       "get_rules"),
                ("trading-worker",  "trading.account",  "list_exchanges"),
            };

            var workers = new List<object>();
            foreach (var (name, cap, route) in capabilities)
            {
                var available = registry.HasAvailableWorker(cap);
                long latencyMs = -1;
                string status = "disconnected";
                string? error = null;

                if (available)
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var result = await dispatcher.DispatchAsync(new ApprovedRequest
                        {
                            RequestId = Guid.NewGuid().ToString("N"),
                            CapabilityId = cap, Route = route, Payload = "{}",
                            Scope = "{}", PrincipalId = "system", TaskId = "health", SessionId = "health"
                        });
                        sw.Stop();
                        latencyMs = sw.ElapsedMilliseconds;
                        status = result.Success ? "healthy" : "error";
                        if (!result.Success) error = result.ErrorMessage;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        latencyMs = sw.ElapsedMilliseconds;
                        status = "error";
                        error = ex.Message;
                    }
                }

                workers.Add(new
                {
                    worker = name,
                    capability = cap,
                    status,
                    connected = available,
                    latency_ms = latencyMs,
                    error,
                });
            }

            var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                broker_status = "running",
                uptime_seconds = (int)uptime.TotalSeconds,
                uptime_display = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s",
                worker_count = workers.Count(w => ((dynamic)w).connected),
                workers,
            }));
        });
    }
}
