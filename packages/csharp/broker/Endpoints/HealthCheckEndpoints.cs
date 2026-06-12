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

        // GET /api/v1/health/score/history?since_minutes=360 — Health Score 時序
        hc.MapGet("/score/history", (
            HttpContext ctx, Broker.Services.HealthScoreSnapshotService snapSvc) =>
        {
            var sinceMin = int.TryParse(ctx.Request.Query["since_minutes"].ToString(), out var s)
                ? Math.Clamp(s, 5, 10080) : 360;  // default 6h, max 7d
            var history = snapSvc.GetHistory(sinceMin);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                since_minutes = sinceMin,
                count         = history.Count,
                snapshots = history.Select(h => new
                {
                    captured_at    = h.CapturedAt,
                    overall_score  = h.OverallScore,
                    status         = h.OverallStatus,
                    worker_count   = h.WorkerCount,
                    healthy        = h.HealthyCount,
                    degraded       = h.DegradedCount,
                    critical       = h.CriticalCount,
                }),
            }));
        });

        // GET /api/v1/health/score — Worker 健康綜合分數（0-100、含三個子分量）
        hc.MapGet("/score", async (
            Broker.Services.HealthScoreService svc, CancellationToken ct) =>
        {
            var report = await svc.ComputeAsync(ct);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                generated_at   = report.GeneratedAt,
                overall_score  = report.OverallScore,
                overall_status = report.OverallStatus,
                worker_count   = report.WorkerCount,
                healthy_count  = report.HealthyCount,
                degraded_count = report.DegradedCount,
                critical_count = report.CriticalCount,
                workers = report.Workers.Select(w => new
                {
                    worker_id    = w.WorkerId,
                    capabilities = w.Capabilities,
                    state        = w.State,
                    score        = w.Score,
                    status       = w.Status,
                    heartbeat = w.Heartbeat == null ? null : new
                    {
                        score = w.Heartbeat.Score,
                        label = w.Heartbeat.Label,
                        age_seconds = w.Heartbeat.AgeSeconds,
                    },
                    dispatch = w.Dispatch == null ? null : (object)new
                    {
                        score = w.Dispatch.Score,
                        label = w.Dispatch.Label,
                        succeeded = w.Dispatch.Succeeded,
                        failed = w.Dispatch.Failed,
                        success_rate_pct = w.Dispatch.SuccessRatePct,
                    },
                    resource = w.Resource == null ? null : (object)new
                    {
                        score = w.Resource.Score,
                        label = w.Resource.Label,
                        cpu_pct = w.Resource.CpuPct,
                        mem_pct = w.Resource.MemPct,
                    },
                }),
            }));
        });
    }
}
