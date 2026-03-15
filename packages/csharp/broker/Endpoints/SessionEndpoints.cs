using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/sessions/*</summary>
public static class SessionEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var sessions = group.MapGroup("/sessions");

        sessions.MapPost("/register", (HttpContext ctx,
            ISessionService sessionService,
            IScopedTokenService tokenService,
            IRevocationService revocationService,
            IEnvelopeCrypto crypto,
            ISessionKeyStore keyStore,
            ICapabilityCatalog capabilityCatalog,
            ITaskRouter taskRouter,
            BrokerDb db) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var taskId = body.GetProperty("task_id").GetString() ?? string.Empty;
            var principalId = body.GetProperty("principal_id").GetString() ?? string.Empty;
            var requestedRoleId = body.TryGetProperty("role_id", out var roleProp)
                ? roleProp.GetString() ?? string.Empty
                : string.Empty;

            var principal = db.Get<Principal>(principalId);
            if (principal == null || principal.Status != EntityStatus.Active)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Invalid or inactive principal_id."));
            }

            var task = db.Get<BrokerTask>(taskId);
            if (task == null)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Invalid task_id."));
            }

            if (task.State is TaskState.Cancelled or TaskState.Completed)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Task is not active."));
            }

            if (!string.IsNullOrWhiteSpace(task.AssignedPrincipalId) &&
                !string.Equals(task.AssignedPrincipalId, principalId, StringComparison.Ordinal))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Task is assigned to a different principal."));
            }

            if (!string.IsNullOrWhiteSpace(task.AssignedRoleId) &&
                !string.IsNullOrWhiteSpace(requestedRoleId) &&
                !string.Equals(task.AssignedRoleId, requestedRoleId, StringComparison.Ordinal))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Requested role does not match task-assigned role."));
            }

            var roleId = ResolveRoleId(task, requestedRoleId, taskRouter);
            if (string.IsNullOrWhiteSpace(roleId))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Unable to resolve role for task."));
            }

            var role = db.Get<Role>(roleId);
            if (role == null || role.Status != EntityStatus.Active)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Invalid or inactive role_id."));
            }

            if (!IsRoleAllowedForTask(role, task.TaskType))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Role is not allowed for this task type."));
            }

            var clientPub = ctx.Items[EncryptionMiddleware.ClientEphemeralPubKey] as string;
            if (string.IsNullOrEmpty(clientPub))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Missing client ephemeral public key."));
            }

            var currentEpoch = revocationService.GetCurrentEpoch();
            var jti = BrokerCore.IdGen.New("jti");
            var session = sessionService.RegisterSession(
                taskId, principalId, roleId, jti, currentEpoch, string.Empty);

            var sessionKey = crypto.DeriveSessionKey(clientPub, session.SessionId);
            keyStore.Store(session.SessionId, sessionKey);

            var plannedGrants = BuildGrantPlan(task, role, capabilityCatalog);
            var grantedCapabilityIds = plannedGrants
                .Select(grant => grant.CapabilityId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var tokenClaims = new ScopedTokenClaims
            {
                PrincipalId = principalId,
                Jti = jti,
                TaskId = taskId,
                SessionId = session.SessionId,
                RoleId = roleId,
                CapabilityIds = grantedCapabilityIds,
                Scope = string.IsNullOrWhiteSpace(task.ScopeDescriptor) ? "{}" : task.ScopeDescriptor,
                Epoch = currentEpoch
            };

            var scopedToken = tokenService.GenerateToken(tokenClaims);

            foreach (var grant in plannedGrants)
            {
                capabilityCatalog.CreateGrant(
                    taskId,
                    session.SessionId,
                    principalId,
                    grant.CapabilityId,
                    grant.ScopeOverride,
                    grant.Quota,
                    session.ExpiresAt);
            }

            if (task.State == TaskState.Created)
            {
                db.Execute(
                    "UPDATE broker_tasks SET state = @state WHERE task_id = @taskId",
                    new { state = (int)TaskState.Active, taskId });
            }

            ctx.Items[EncryptionMiddleware.SessionKeyKey] = sessionKey;
            ctx.Items[EncryptionMiddleware.SessionIdKey] = session.SessionId;
            ctx.Items[EncryptionMiddleware.RequestSeqKey] = 0;

            return Results.Ok(ApiResponseHelper.Success(new
            {
                session_id = session.SessionId,
                scoped_token = scopedToken,
                broker_public_key = crypto.GetBrokerPublicKey(),
                expires_at = session.ExpiresAt
            }));
        });

        sessions.MapPost("/heartbeat", (HttpContext ctx, ISessionService sessionService) =>
        {
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? string.Empty;

            var success = sessionService.Heartbeat(sessionId);
            if (!success)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Session not found or inactive."));
            }

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Heartbeat acknowledged."));
        });

        sessions.MapPost("/close", (HttpContext ctx, ISessionService sessionService, ISessionKeyStore keyStore) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? string.Empty;
            var reason = body.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? string.Empty
                : "Client requested close";

            keyStore.Remove(sessionId);
            var success = sessionService.CloseSession(sessionId, reason);

            if (!success)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Session not found or already closed."));
            }

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Session closed."));
        });
    }

    private static string ResolveRoleId(BrokerTask task, string requestedRoleId, ITaskRouter taskRouter)
    {
        if (!string.IsNullOrWhiteSpace(task.AssignedRoleId))
        {
            return task.AssignedRoleId;
        }

        if (!string.IsNullOrWhiteSpace(requestedRoleId))
        {
            return requestedRoleId;
        }

        return taskRouter.RecommendRole(task.TaskType) ?? string.Empty;
    }

    private static bool IsRoleAllowedForTask(Role role, string taskType)
    {
        var allowedTaskTypes = ParseStringArray(role.AllowedTaskTypes);
        return allowedTaskTypes.Contains("*", StringComparer.OrdinalIgnoreCase) ||
               allowedTaskTypes.Contains(taskType, StringComparer.OrdinalIgnoreCase);
    }

    private static GrantPlanEntry[] BuildGrantPlan(BrokerTask task, Role role, ICapabilityCatalog capabilityCatalog)
    {
        var descriptor = TaskRuntimeDescriptor.Parse(task.RuntimeDescriptor);
        var fallbackScope = string.IsNullOrWhiteSpace(task.ScopeDescriptor) ? "{}" : task.ScopeDescriptor;

        if (descriptor.CapabilityGrants.Count > 0)
        {
            return descriptor.CapabilityGrants
                .Where(template => !string.IsNullOrWhiteSpace(template.CapabilityId))
                .Select(template => new GrantPlanEntry(
                    template.CapabilityId,
                    template.ResolveScopeOverride(fallbackScope),
                    template.ResolveQuota()))
                .ToArray();
        }

        if (descriptor.CapabilityIds.Count > 0)
        {
            return descriptor.CapabilityIds
                .Where(capabilityId => !string.IsNullOrWhiteSpace(capabilityId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(capabilityId => new GrantPlanEntry(capabilityId, fallbackScope, -1))
                .ToArray();
        }

        return GetDefaultCapabilities(role, capabilityCatalog)
            .Select(capabilityId => new GrantPlanEntry(capabilityId, fallbackScope, -1))
            .ToArray();
    }

    private static string[] GetDefaultCapabilities(Role role, ICapabilityCatalog capabilityCatalog)
    {
        var defaults = ParseStringArray(role.DefaultCapabilityIds);
        if (defaults.Length == 0)
        {
            return Array.Empty<string>();
        }

        if (defaults.Contains("*", StringComparer.OrdinalIgnoreCase))
        {
            return capabilityCatalog.ListCapabilities()
                .Select(capability => capability.CapabilityId)
                .Where(capabilityId => !string.IsNullOrWhiteSpace(capabilityId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return defaults;
    }

    private static string[] ParseStringArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]")
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private sealed record GrantPlanEntry(string CapabilityId, string ScopeOverride, int Quota);
}
