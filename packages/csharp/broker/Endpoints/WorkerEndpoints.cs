using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        // worker 池的列舉與生命週期控制都是管理操作，只限管理員。
        var workers = group.MapGroup("/workers").RequireBrokerAdmin();

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
        // Agents are spawned only through /agents/spawn (which checks the agent exists and is
        // active). The container environment is composed by the broker; a request cannot add to it.
        workers.MapPost("/spawn", async (
            HttpContext ctx,
            IContainerManager containerMgr,
            CancellationToken ct) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var (request, error) = ParseSpawnRequest(body);
            if (request == null)
                return Results.BadRequest(ApiResponseHelper.Error(error!, 400));

            try
            {
                var containerId = await containerMgr.SpawnWorkerAsync(request, ct);

                return Results.Ok(ApiResponseHelper.Success(new
                {
                    container_id = containerId,
                    worker_id = request.WorkerId,
                    worker_type = request.WorkerType,
                    status = "spawned"
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
            catch (Win32Exception)
            {
                return Results.Json(ApiResponseHelper.Error(
                    "Container runtime CLI not found. Install docker or podman, or set FunctionPool:ContainerManager:Runtime.", 503),
                    statusCode: 503);
            }
            catch (TimeoutException ex)
            {
                return Results.Json(ApiResponseHelper.Error(ex.Message, 504), statusCode: 504);
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

    private static readonly Regex WorkerIdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9_.\-]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates a /workers/spawn body: worker_type is required and must not be "agent";
    /// worker_id is optional and limited to a safe character set; "environment" is no longer accepted.
    /// </summary>
    internal static (ContainerSpawnRequest? Request, string? Error) ParseSpawnRequest(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return (null, "Request body must be a JSON object.");

        if (!body.TryGetProperty("worker_type", out var typeEl) ||
            typeEl.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(typeEl.GetString()))
        {
            return (null, "worker_type is required.");
        }

        var workerType = typeEl.GetString()!.Trim();
        if (ContainerManager.IsAgentWorkerType(workerType))
            return (null, "Agents are spawned through POST /api/v1/agents/spawn.");

        if (body.TryGetProperty("environment", out _))
            return (null, "environment is not accepted; worker environment is configured on the broker.");

        string workerId;
        if (body.TryGetProperty("worker_id", out var idEl))
        {
            if (idEl.ValueKind != JsonValueKind.String || !WorkerIdPattern.IsMatch(idEl.GetString() ?? string.Empty))
                return (null, "worker_id must be 1-64 characters of letters, digits, '.', '_' or '-'.");
            workerId = idEl.GetString()!;
        }
        else
        {
            // Random part first: the container name keeps only the first 12 characters of the id.
            workerId = $"{Guid.NewGuid():N}"[..12] + "-" + workerType;
            if (workerId.Length > 64)
                workerId = workerId[..64];
            if (!WorkerIdPattern.IsMatch(workerId))
                return (null, "worker_type contains characters that cannot form a worker id.");
        }

        return (new ContainerSpawnRequest { WorkerType = workerType, WorkerId = workerId }, null);
    }
}
