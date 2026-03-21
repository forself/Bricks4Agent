using Broker.Helpers;
using FunctionPool.Container;
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
