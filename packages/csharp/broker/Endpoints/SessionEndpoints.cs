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

        // 取得 token 的入口：不需 scoped token，由下列條件把關
        // （任務須已指派主體與角色、角色以任務為準、管理員等級角色只接受本機註冊）。
        sessions.MapPost("/register", (HttpContext ctx,
            ISessionService sessionService,
            IScopedTokenService tokenService,
            IRevocationService revocationService,
            IEnvelopeCrypto crypto,
            ISessionKeyStore keyStore,
            ICapabilityCatalog capabilityCatalog,
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

            // 只接受已指派主體與角色的任務；未指派者一律拒絕（不再採用請求自帶的角色）。
            if (string.IsNullOrWhiteSpace(task.AssignedPrincipalId) ||
                string.IsNullOrWhiteSpace(task.AssignedRoleId))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Task has no assigned principal/role."));
            }

            if (!string.Equals(task.AssignedPrincipalId, principalId, StringComparison.Ordinal))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Task is assigned to a different principal."));
            }

            if (!string.IsNullOrWhiteSpace(requestedRoleId) &&
                !string.Equals(task.AssignedRoleId, requestedRoleId, StringComparison.Ordinal))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Requested role does not match task-assigned role."));
            }

            var roleId = task.AssignedRoleId;

            var role = db.Get<Role>(roleId);
            if (role == null || role.Status != EntityStatus.Active)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Invalid or inactive role_id."));
            }

            if (!IsRoleAllowedForTask(role, task.TaskType))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Role is not allowed for this task type."));
            }

            if (IsAdministrativeRole(role) && !IsLoopbackRequest(ctx))
            {
                return Results.Json(
                    ApiResponseHelper.Error("Administrative roles can only be registered from the local host.", StatusCodes.Status403Forbidden),
                    statusCode: StatusCodes.Status403Forbidden);
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
        }).WithBrokerAuthPolicy(BrokerAuthPolicy.SessionBootstrap);

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

        sessions.MapPost("/close", (HttpContext ctx,
            ISessionService sessionService,
            ISessionKeyStore keyStore,
            IRevocationService revocationService) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? string.Empty;
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            var reason = body.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String
                ? reasonProp.GetString() ?? string.Empty
                : "Client requested close";

            // 關閉後讓這個 session 的 token 立即失效（BrokerAuth 會查撤銷清單），
            // 不必等到 token 自然過期。jti 取自註冊時寫入 session 的紀錄，並以呼叫者 token 的 jti 補足。
            var session = sessionService.GetSession(sessionId);
            var callerJti = (ctx.Items[BrokerAuthMiddleware.ClaimsKey] as ScopedTokenClaims)?.Jti;

            keyStore.Remove(sessionId);
            var success = sessionService.CloseSession(sessionId, reason);

            foreach (var jti in new[] { session?.TokenJti, callerJti }
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.Ordinal))
            {
                revocationService.Revoke(
                    RevocationTargetType.Token,
                    jti!,
                    "Session closed by client.",
                    string.IsNullOrWhiteSpace(principalId) ? "session-close" : principalId);
            }

            if (!success)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Session not found or already closed."));
            }

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Session closed."));
        });
    }

    private static bool IsRoleAllowedForTask(Role role, string taskType)
    {
        var allowedTaskTypes = ParseStringArray(role.AllowedTaskTypes);
        return allowedTaskTypes.Contains("*", StringComparer.OrdinalIgnoreCase) ||
               allowedTaskTypes.Contains(taskType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>管理員等級的角色：role_admin，或允許所有任務類型（"*"）的角色。</summary>
    private static bool IsAdministrativeRole(Role role)
    {
        return string.Equals(role.RoleId, "role_admin", StringComparison.Ordinal) ||
               ParseStringArray(role.AllowedTaskTypes).Contains("*", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>來源為 loopback（含 IPv4-mapped）；RemoteIpAddress 為 null 時視為非本機。</summary>
    private static bool IsLoopbackRequest(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip == null)
        {
            return false;
        }

        if (System.Net.IPAddress.IsLoopback(ip))
        {
            return true;
        }

        return ip.IsIPv4MappedToIPv6 && System.Net.IPAddress.IsLoopback(ip.MapToIPv4());
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
