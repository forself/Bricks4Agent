using Broker.Helpers;
using FunctionPool.Diagnostics;

namespace Broker.Endpoints;

/// <summary>
/// System-wide diagnostic scan endpoints.
///
/// Only registered when FunctionPool:Enabled=true (requires IWorkerRegistry + IContainerManager).
/// </summary>
public static class DiagnosticsEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var diag = group.MapGroup("/diagnostics");

        // ── GET /api/v1/diagnostics/scan — Full diagnostic scan ──
        //
        // Scans: container runtime, container states, container logs (last 100 lines),
        // and worker heartbeats. Returns a structured report with all detected issues.
        //
        // This is a read-only, side-effect-free operation.
        diag.MapGet("/scan", async (HttpContext ctx, IDiagnosticsService svc, CancellationToken ct) =>
        {
            // 2026-06-03 安全:系統診斷(容器/worker 內部狀態)要登入(原本無 auth、defense-in-depth)。
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var report = await svc.ScanAsync(ct);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                scanned_at         = report.ScannedAt,
                healthy            = report.Healthy,
                runtime_available  = report.RuntimeAvailable,
                total_containers   = report.TotalContainers,
                running_containers = report.RunningContainers,
                total_workers      = report.TotalWorkers,
                ready_workers      = report.ReadyWorkers,
                critical_count     = report.CriticalCount,
                error_count        = report.ErrorCount,
                warning_count      = report.WarningCount,
                issues = report.Issues.Select(i => new
                {
                    severity    = i.Severity.ToString().ToLowerInvariant(),
                    category    = i.Category,
                    entity_id   = i.EntityId,
                    message     = i.Message,
                    detected_at = i.DetectedAt,
                })
            }));
        });

        // ── GET /api/v1/diagnostics/history — 排程掃描歷史 ──
        diag.MapGet("/history", (HttpRequest req, IServiceProvider sp) =>
        {
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(req.HttpContext)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var sched = sp.GetService<ScheduledDiagnosticsService>();
            if (sched == null)
                return Results.Ok(ApiResponseHelper.Error("Scheduled diagnostics not enabled"));

            var take = req.Query.TryGetValue("take", out var t) && int.TryParse(t, out var n) ? n : 24;
            var runs = sched.GetRecentRuns(take);
            return Results.Ok(ApiResponseHelper.Success(new { runs }));
        });

        // ── GET /api/v1/diagnostics/history/:runId — 單次掃描的 issues ──
        diag.MapGet("/history/{runId}", (string runId, HttpContext ctx, IServiceProvider sp) =>
        {
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var sched = sp.GetService<ScheduledDiagnosticsService>();
            if (sched == null)
                return Results.Ok(ApiResponseHelper.Error("Scheduled diagnostics not enabled"));

            var issues = sched.GetIssuesForRun(runId);
            return Results.Ok(ApiResponseHelper.Success(new { run_id = runId, issues }));
        });

        // ── GET /api/v1/diagnostics/space — DB 空間使用 ──
        diag.MapGet("/space", (HttpContext ctx, IServiceProvider sp) =>
        {
            if (string.IsNullOrEmpty(RequestBodyHelper.GetPrincipalId(ctx)))
                return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var sched = sp.GetService<ScheduledDiagnosticsService>();
            if (sched == null)
                return Results.Ok(ApiResponseHelper.Error("Scheduled diagnostics not enabled"));

            var info = sched.GetSpaceInfo();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                active_db_size_kb      = info.ActiveDbSizeBytes / 1024,
                archive_db_size_kb     = info.ArchiveDbSizeBytes / 1024,
                active_run_count       = info.ActiveRunCount,
                archive_run_count      = info.ArchiveRunCount,
                retention_days         = info.RetentionDays,
                archive_retention_days = info.ArchiveRetentionDays
            }));
        });
    }
}
