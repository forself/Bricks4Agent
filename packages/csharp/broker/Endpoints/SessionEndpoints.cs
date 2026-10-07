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
        // （先驗註冊憑證；任務須已指派主體與角色、角色以任務為準、管理員等級角色只接受本機註冊）。
        sessions.MapPost("/register", (HttpContext ctx,
            ISessionService sessionService,
            IScopedTokenService tokenService,
            IRevocationService revocationService,
            IRegistrationCredentialService registrationCredentials,
            IAuditService auditService,
            ILoggerFactory loggerFactory,
            IEnvelopeCrypto crypto,
            ISessionKeyStore keyStore,
            ICapabilityCatalog capabilityCatalog,
            BrokerDb db) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var taskId = ReadString(body, "task_id");
            var principalId = ReadString(body, "principal_id");
            var requestedRoleId = ReadString(body, "role_id");
            var registrationSecret = ReadString(body, "registration_secret");

            // 註冊憑證最先驗證。缺少、錯誤、到期、撤銷，以及主體或任務不存在、任務沒有指派給這個主體，
            // 一律回同一個 401，不透露哪一項不符；實際原因只寫進伺服器端 log 與稽核（不含密鑰）。
            var registration = new RegistrationAudit(ctx, auditService, loggerFactory, principalId, taskId);
            var credentialCheck = registrationCredentials.Verify(principalId, taskId, registrationSecret);
            if (!credentialCheck.Succeeded)
            {
                return registration.Reject(
                    "credential_" + credentialCheck.Failure.ToString().ToLowerInvariant(),
                    credentialCheck.Credential);
            }

            var credential = credentialCheck.Credential!;
            var principal = db.Get<Principal>(principalId);
            if (principal == null || principal.Status != EntityStatus.Active)
            {
                return registration.Reject("principal_inactive", credential);
            }

            var task = db.Get<BrokerTask>(taskId);
            if (task == null)
            {
                return registration.Reject("task_unknown", credential);
            }

            if (TaskStates.IsTerminal(task.State))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Task is not active."));
            }

            // 只接受已指派主體與角色的任務；未指派者一律拒絕（不再採用請求自帶的角色）。
            if (string.IsNullOrWhiteSpace(task.AssignedPrincipalId) ||
                string.IsNullOrWhiteSpace(task.AssignedRoleId))
            {
                return Results.BadRequest(ApiResponseHelper.Error("Task has no assigned principal/role."));
            }

            // 憑證綁定的主體必須仍是任務指派的主體：任務改派後，舊憑證隨之失效。
            if (!string.Equals(task.AssignedPrincipalId, principalId, StringComparison.Ordinal) ||
                !string.Equals(credential.PrincipalId, task.AssignedPrincipalId, StringComparison.Ordinal))
            {
                return registration.Reject("task_assigned_elsewhere", credential);
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
                taskId, principalId, roleId, jti, currentEpoch, string.Empty, credential.CredentialId);

            var sessionKey = crypto.DeriveSessionKey(clientPub, session.SessionId);
            keyStore.Store(session.SessionId, sessionKey);

            // 憑證的撤銷先標記憑證、再結束以它註冊的 session。session 寫入之後再讀一次憑證：
            // 撤銷若落在上面的驗證與寫入之間，這裡會看到，結束剛建立的 session 並回同一個 401；
            // 撤銷若在這次讀取之後，它結束 session 的那一步會看到這個 session。
            if (registrationCredentials.IsRevoked(credential.CredentialId))
            {
                keyStore.Remove(session.SessionId);
                sessionService.RevokeSession(session.SessionId, "Registration credential revoked during registration.", "session-register");
                return registration.Reject("credential_revoked", credential);
            }

            var plannedGrants = BuildGrantPlan(task, role, capabilityCatalog, db);
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

            registrationCredentials.RecordUse(credential.CredentialId);
            registration.RecordRegistered(session.SessionId, credential);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                session_id = session.SessionId,
                scoped_token = scopedToken,
                broker_public_key = crypto.GetBrokerPublicKey(),
                expires_at = session.ExpiresAt,
                token_expires_at = DateTime.UtcNow + tokenService.TokenLifetime
            }));
        }).WithBrokerAuthPolicy(BrokerAuthPolicy.SessionBootstrap);

        // 心跳：延長 session 與它的授予，並換發同一 session 的新 token（新 jti）。
        // 舊 token 不撤銷、到期自然失效（排隊或並行中的請求仍可完成）；關閉或撤銷 session 則由 BrokerAuth 的 session 檢查立即生效。
        // 只接受 session 自己的加密通道（BrokerAuth 已確認通道與 token 屬於同一 session）。
        // 新 token 沿用請求 token 的 epoch（BrokerAuth 已驗證過的值），不取簽發當下的 epoch：
        // kill switch 即使落在 BrokerAuth 的檢查之後，換發的 token 也會在下一個請求被 epoch 閘道拒絕。
        sessions.MapPost("/heartbeat", (HttpContext ctx,
            ISessionService sessionService,
            IScopedTokenService tokenService,
            IRevocationService revocationService,
            IRegistrationCredentialService registrationCredentials,
            ICapabilityCatalog capabilityCatalog,
            ISessionKeyStore keyStore,
            BrokerDb db) =>
        {
            var claims = ctx.Items[BrokerAuthMiddleware.ClaimsKey] as ScopedTokenClaims;
            var session = ctx.Items[BrokerAuthMiddleware.SessionRecordKey] as ContainerSession;
            var channelSessionId = ctx.Items[EncryptionMiddleware.SessionIdKey] as string;
            if (claims == null || session == null ||
                string.IsNullOrEmpty(channelSessionId) ||
                !string.Equals(channelSessionId, session.SessionId, StringComparison.Ordinal))
            {
                return Unauthorized("Token renewal requires the session's encrypted channel.");
            }

            // kill switch 在 BrokerAuth 檢查之後才發生：回與 BrokerAuth 相同的 epoch 401，代理即刻停止。
            if (revocationService.GetCurrentEpoch() > claims.Epoch)
            {
                return Unauthorized(BrokerAuthMiddleware.EpochAdvancedMessage);
            }

            // 主體已停用、任務已結束，或註冊這個 session 的憑證已撤銷時，不再續期，並結束這個 session。
            // （只看撤銷不看到期：憑證到期只影響之後的註冊，不結束既有的 session。）
            var principal = db.Get<Principal>(session.PrincipalId);
            var task = db.Get<BrokerTask>(session.TaskId);
            var credentialRevoked = !string.IsNullOrEmpty(session.RegistrationCredentialId) &&
                                    registrationCredentials.IsRevoked(session.RegistrationCredentialId);
            if (principal == null || principal.Status != EntityStatus.Active ||
                task == null || TaskStates.IsTerminal(task.State) ||
                credentialRevoked)
            {
                keyStore.Remove(session.SessionId);
                sessionService.RevokeSession(
                    session.SessionId,
                    credentialRevoked ? "Registration credential revoked." : "Principal or task is no longer active.",
                    "session-heartbeat");
                return Unauthorized("Session can no longer be renewed.");
            }

            var jti = BrokerCore.IdGen.New("jti");
            var sessionExpiresAt = sessionService.Heartbeat(session.SessionId, jti);
            if (sessionExpiresAt == null)
            {
                return Unauthorized("Session is not active.");
            }

            capabilityCatalog.ExtendSessionGrants(session.SessionId, sessionExpiresAt.Value);

            var scopedToken = tokenService.GenerateToken(new ScopedTokenClaims
            {
                PrincipalId = session.PrincipalId,
                Jti = jti,
                TaskId = session.TaskId,
                SessionId = session.SessionId,
                RoleId = session.RoleId,
                CapabilityIds = claims.CapabilityIds,
                Scope = claims.Scope,
                Epoch = claims.Epoch
            });

            return Results.Ok(ApiResponseHelper.Success(new
            {
                session_id = session.SessionId,
                scoped_token = scopedToken,
                token_expires_at = DateTime.UtcNow + tokenService.TokenLifetime,
                session_expires_at = sessionExpiresAt.Value
            }, "Heartbeat acknowledged."));
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

    private static IResult Unauthorized(string message)
        => Results.Json(
            ApiResponseHelper.Error(message, StatusCodes.Status401Unauthorized),
            statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>register 對所有憑證、主體與任務綁定失敗回覆的同一則訊息。</summary>
    public const string RegistrationRejectedMessage = "Registration rejected.";

    /// <summary>讀取字串欄位；body 不是物件、欄位不存在或不是字串時回傳空字串。</summary>
    private static string ReadString(JsonElement body, string name)
        => body.ValueKind == JsonValueKind.Object &&
           body.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// register 的拒絕與成功紀錄：拒絕一律回 <see cref="RegistrationRejectedMessage"/>（401），
    /// 原因與憑證 id 只寫進伺服器端 log 與稽核；密鑰本身不出現在任何紀錄中。
    /// </summary>
    private sealed class RegistrationAudit
    {
        private const int MaxLoggedIdLength = 128;

        private readonly HttpContext _ctx;
        private readonly IAuditService _audit;
        private readonly ILogger _logger;
        private readonly string _principalId;
        private readonly string _taskId;

        public RegistrationAudit(HttpContext ctx, IAuditService audit, ILoggerFactory loggerFactory, string principalId, string taskId)
        {
            _ctx = ctx;
            _audit = audit;
            _logger = loggerFactory.CreateLogger("SessionRegistration");
            _principalId = Truncate(principalId);
            _taskId = Truncate(taskId);
        }

        public IResult Reject(string reason, RegistrationCredential? credential)
        {
            _logger.LogWarning(
                "Session registration rejected: reason={Reason} principal={PrincipalId} task={TaskId} credential={CredentialId}",
                reason, _principalId, _taskId, credential?.CredentialId ?? "-");
            Record("SESSION_REGISTER_REJECTED", null, new { reason, credential_id = credential?.CredentialId });
            return Unauthorized(RegistrationRejectedMessage);
        }

        public void RecordRegistered(string sessionId, RegistrationCredential credential)
            => Record("SESSION_REGISTERED", sessionId, new { credential_id = credential.CredentialId, source = credential.Source });

        private void Record(string eventType, string? sessionId, object details)
        {
            try
            {
                var traceId = _ctx.Items.TryGetValue("audit_trace_id", out var trace) && trace is string value && value.Length > 0
                    ? value
                    : Guid.NewGuid().ToString("N");
                _audit.RecordEvent(
                    traceId,
                    eventType,
                    principalId: _principalId,
                    taskId: _taskId,
                    sessionId: sessionId,
                    resourceRef: "/api/v1/sessions/register",
                    details: JsonSerializer.Serialize(details));
            }
            catch (Exception ex)
            {
                // 稽核失敗不阻斷註冊流程（與 AuditMiddleware 相同）
                _logger.LogError(ex, "Failed to record the {EventType} audit event", eventType);
            }
        }

        private static string Truncate(string value)
            => value.Length <= MaxLoggedIdLength ? value : value[..MaxLoggedIdLength];
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

    private static GrantPlanEntry[] BuildGrantPlan(BrokerTask task, Role role, ICapabilityCatalog capabilityCatalog, BrokerDb db)
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
                    ResolvePlannedQuota(template, task.TaskId, db)))
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

    /// <summary>
    /// 新 session 的配額。以任務累計（<c>quota_scope: task</c>）時，扣掉這個任務先前各 session 已經用掉的次數：
    /// 已消耗配額的請求（分派過的 Dispatched、Succeeded、Failed）都算，所以代理容器重啟或 session 過期後重新註冊，
    /// 不會拿到新的一份配額。其他情況維持每個 session 一份完整配額。
    /// </summary>
    internal static int ResolvePlannedQuota(TaskCapabilityGrantTemplate template, string taskId, BrokerDb db)
    {
        var quota = template.ResolveQuota();
        if (quota < 0 || !template.IsTaskScopedQuota)
            return quota;

        var used = db.Scalar<long>(
            @"SELECT COUNT(*) FROM execution_requests
              WHERE task_id = @taskId AND capability_id = @capabilityId
                AND execution_state IN (@dispatched, @succeeded, @failed)",
            new
            {
                taskId,
                capabilityId = template.CapabilityId,
                dispatched = (int)ExecutionState.Dispatched,
                succeeded = (int)ExecutionState.Succeeded,
                failed = (int)ExecutionState.Failed
            });
        return (int)Math.Max(0, quota - used);
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
