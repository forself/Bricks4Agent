using Broker.Helpers;
using FunctionPool.Container;
using FunctionPool.ContainerLogs;
using FunctionPool.Registry;

namespace Broker.Endpoints;

/// <summary>
/// Worker pool management endpoints — spawn/stop/list/logs
///
/// All worker lifecycle is controlled through these endpoints.
/// Workers run inside containers and connect back to the broker via TCP.
/// </summary>
public static class WorkerEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var workers = group.MapGroup("/workers");

        // ── GET /api/v1/workers — List all registered workers ──
        workers.MapGet("/", (IWorkerRegistry registry) =>
        {
            var all = registry.GetAllWorkers();
            return Results.Ok(ApiResponseHelper.Success(all.Select(w => new
            {
                worker_id = w.WorkerId,
                capabilities = w.Capabilities,
                state = w.State.ToString().ToLowerInvariant(),
                active_tasks = w.ActiveTasks,
                max_concurrent = w.MaxConcurrent,
                connected_at = w.ConnectedAt,
                last_heartbeat = w.LastHeartbeat,
                remote_endpoint = w.RemoteEndpoint
            })));
        });

        // ── GET /api/v1/workers/available-types — List worker types configured for spawn ──
        workers.MapGet("/available-types", (IServiceProvider sp) =>
        {
            var containerConfig = sp.GetService<ContainerConfig>();
            if (containerConfig == null)
            {
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    runtime = (string?)null,
                    network = (string?)null,
                    max_per_type = 0,
                    types = Array.Empty<object>(),
                    note = "ContainerManager disabled (FunctionPool:ContainerManager:Enabled=false)",
                }));
            }
            var types = containerConfig.WorkerImages
                .Select(kv => new
                {
                    worker_type = kv.Key,
                    image = kv.Value.Image,
                    memory_limit = kv.Value.MemoryLimit,
                    cpu_limit = kv.Value.CpuLimit,
                })
                .OrderBy(x => x.worker_type)
                .ToList();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                runtime = containerConfig.Runtime,
                network = containerConfig.NetworkName,
                max_per_type = containerConfig.MaxContainersPerType,
                types,
            }));
        });

        // ── POST /api/v1/workers/spawn — Spawn a new worker container ──
        workers.MapPost("/spawn", async (
            HttpContext ctx,
            IContainerManager containerMgr,
            CancellationToken ct) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var workerType = body.GetProperty("worker_type").GetString();
            var workerId = body.TryGetProperty("worker_id", out var wid)
                ? wid.GetString()
                : $"{workerType}-{Guid.NewGuid():N}"[..Math.Min(32, workerType!.Length + 33)];

            Dictionary<string, string>? envOverrides = null;
            if (body.TryGetProperty("environment", out var envProp))
            {
                envOverrides = new Dictionary<string, string>();
                foreach (var prop in envProp.EnumerateObject())
                    envOverrides[prop.Name] = prop.Value.GetString() ?? "";
            }

            try
            {
                var containerId = await containerMgr.SpawnWorkerAsync(
                    workerType!, workerId!, envOverrides, ct);

                return Results.Ok(ApiResponseHelper.Success(new
                {
                    container_id = containerId,
                    worker_id = workerId,
                    worker_type = workerType,
                    status = "spawned"
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── POST /api/v1/workers/stop — Stop a worker container ──
        workers.MapPost("/stop", async (
            HttpContext ctx,
            IContainerManager containerMgr,
            CancellationToken ct) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var containerId = body.GetProperty("container_id").GetString()!;

            try
            {
                await containerMgr.StopWorkerAsync(containerId, ct);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    container_id = containerId,
                    status = "stopped"
                }));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── POST /api/v1/workers/start — Start an existing (stopped/failed) container ──
        workers.MapPost("/start", async (
            HttpContext ctx,
            IContainerManager containerMgr,
            CancellationToken ct) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var containerId = body.GetProperty("container_id").GetString()!;

            try
            {
                await containerMgr.StartWorkerAsync(containerId, ct);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    container_id = containerId,
                    status = "running"
                }));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── GET /api/v1/workers/containers — List managed containers ──
        workers.MapGet("/containers", async (IContainerManager containerMgr, CancellationToken ct) =>
        {
            var containers = await containerMgr.ListManagedAsync(ct);
            return Results.Ok(ApiResponseHelper.Success(containers.Select(c => new
            {
                container_id = c.ContainerId,
                worker_id = c.WorkerId,
                worker_type = c.WorkerType,
                image = c.ImageName,
                state = c.State.ToString().ToLowerInvariant(),
                spawned_at = c.SpawnedAt
            })));
        });

        // ── POST /api/v1/workers/logs — Get container logs ──
        workers.MapPost("/logs", async (
            HttpContext ctx,
            IContainerManager containerMgr,
            CancellationToken ct) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var containerId = body.GetProperty("container_id").GetString()!;
            var tailLines = body.TryGetProperty("tail", out var t) ? t.GetInt32() : 50;

            var logs = await containerMgr.GetLogsAsync(containerId, tailLines, ct);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                container_id = containerId,
                logs
            }));
        });

        // ── GET /api/v1/workers/log-history — Query persisted error log entries (SQLite) ──
        workers.MapGet("/log-history", (IServiceProvider sp, HttpContext ctx) =>
        {
            var tail = sp.GetService<ContainerLogTailService>();
            if (tail == null)
            {
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    entries = Array.Empty<object>(),
                    space = (object?)null,
                    note = "ContainerLogTailService disabled (FunctionPool:ContainerLogTail:Enabled=false)",
                }));
            }
            var q = ctx.Request.Query;
            var containerId = q["container_id"].ToString();
            var level = q["level"].ToString();
            var limit = int.TryParse(q["limit"].ToString(), out var l) ? Math.Min(Math.Max(l, 1), 1000) : 200;

            var entries = tail.Query(
                string.IsNullOrEmpty(containerId) ? null : containerId,
                string.IsNullOrEmpty(level) ? null : level,
                limit);
            var space = tail.GetSpaceInfo();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                entries = entries.Select(e => new
                {
                    container_id = e.ContainerId,
                    worker_type = e.WorkerType,
                    ts = e.Ts,
                    level = e.Level,
                    stderr = e.Stderr,
                    message = e.Message,
                }),
                space = new
                {
                    db_size_kb = space.DbSizeBytes / 1024,
                    entry_count = space.EntryCount,
                    retention_days = space.RetentionDays,
                },
            }));
        });

        // ── GET /api/v1/workers/stats — Real-time container resource stats ──
        workers.MapGet("/stats", async (IContainerManager containerMgr, CancellationToken ct) =>
        {
            var stats = await containerMgr.GetStatsAsync(ct);
            return Results.Ok(ApiResponseHelper.Success(stats.Select(s => new
            {
                container_id        = s.ContainerId,
                container_name      = s.ContainerName,
                cpu_percent         = s.CpuPercent,
                memory_usage_bytes  = s.MemoryUsageBytes,
                memory_limit_bytes  = s.MemoryLimitBytes,
                memory_percent      = s.MemoryPercent,
                network_input_bytes = s.NetworkInputBytes,
                network_output_bytes = s.NetworkOutputBytes,
                block_read_bytes    = s.BlockReadBytes,
                block_write_bytes   = s.BlockWriteBytes,
                collected_at        = s.CollectedAt
            })));
        });

        // ── GET /api/v1/workers/health — Pool health summary ──
        workers.MapGet("/health", async (
            IWorkerRegistry registry,
            IContainerManager containerMgr,
            CancellationToken ct) =>
        {
            var allWorkers = registry.GetAllWorkers();
            var containers = await containerMgr.ListManagedAsync(ct);
            var runtimeAvailable = await containerMgr.IsRuntimeAvailableAsync(ct);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                container_runtime_available = runtimeAvailable,
                registered_workers = allWorkers.Count,
                ready_workers = allWorkers.Count(w => w.State == FunctionPool.Models.WorkerState.Ready),
                busy_workers = allWorkers.Count(w => w.State == FunctionPool.Models.WorkerState.Busy),
                managed_containers = containers.Count,
                running_containers = containers.Count(c => c.State == ContainerState.Running)
            }));
        });
    }
}
