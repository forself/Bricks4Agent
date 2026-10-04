using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/admin/* — 需 role_admin 授權</summary>
public static class AdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");

        // Kill Switch：epoch 遞增 → 所有舊 token 即時失效
        admin.MapPost("/kill-switch", (HttpContext ctx, IRevocationService revocationService) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            var reason = body.TryGetProperty("reason", out var r)
                ? r.GetString() ?? "" : "Kill switch activated";

            var newEpoch = revocationService.IncrementEpoch(principalId, reason);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                new_epoch = newEpoch,
                message = "All tokens issued before this epoch are now invalid."
            }));
        });

        // 撤權
        admin.MapPost("/revoke", (HttpContext ctx,
            IRevocationService revocationService,
            ISessionService sessionService,
            ISessionKeyStore keyStore) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            var targetType = body.GetProperty("target_type").GetString() ?? "";
            var targetId = body.GetProperty("target_id").GetString() ?? "";
            var reason = body.TryGetProperty("reason", out var r)
                ? r.GetString() ?? "" : "Revoked by admin";

            var revType = targetType.ToLowerInvariant() switch
            {
                "session" => RevocationTargetType.Session,
                "grant" => RevocationTargetType.Grant,
                "token" => RevocationTargetType.Token,
                _ => throw new ArgumentException($"Unknown target_type: {targetType}")
            };

            var revocation = revocationService.Revoke(revType, targetId, reason, principalId);

            // 若撤銷 session，同時清除金鑰
            if (revType == RevocationTargetType.Session)
            {
                keyStore.Remove(targetId);
                sessionService.RevokeSession(targetId, reason, principalId);
            }

            return Results.Ok(ApiResponseHelper.Success(revocation));
        });

        // ── 註冊憑證（session register 的前提）──
        // 種子與 agents/spawn 會自行簽發；以 tasks/create 建立的任務由管理員在這裡簽發。
        // 明文密鑰只在 issue 的加密回應中出現一次；broker 只保存雜湊，list 不回傳雜湊。
        admin.MapPost("/registration-credentials/issue", (HttpContext ctx,
            IRegistrationCredentialService credentials,
            RegistrationCredentialOptions options,
            BrokerDb db) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "principal_id", out var principalId, out var err))
                return err!;
            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out err))
                return err!;

            var lifetimeHours = options.AdminIssuedLifetimeHours;
            if (body.TryGetProperty("lifetime_hours", out var lifetimeEl))
            {
                if (lifetimeEl.ValueKind != JsonValueKind.Number ||
                    !lifetimeEl.TryGetInt32(out lifetimeHours) ||
                    !RegistrationCredentialOptions.IsValidLifetime(lifetimeHours))
                {
                    return Results.BadRequest(ApiResponseHelper.Error(
                        $"lifetime_hours must be an integer between 1 and {RegistrationCredentialService.MaxLifetimeHours}."));
                }
            }

            var principal = db.Get<Principal>(principalId);
            if (principal == null || principal.Status != EntityStatus.Active)
                return Results.BadRequest(ApiResponseHelper.Error("Principal is missing or not active."));

            var task = db.Get<BrokerTask>(taskId);
            if (task == null || task.State is TaskState.Cancelled or TaskState.Completed)
                return Results.BadRequest(ApiResponseHelper.Error("Task is missing or not active."));
            if (!string.Equals(task.AssignedPrincipalId, principalId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(task.AssignedRoleId))
                return Results.BadRequest(ApiResponseHelper.Error("Task must be assigned to this principal with a role."));

            var issuedBy = RequestBodyHelper.GetPrincipalId(ctx);
            var issued = credentials.Issue(
                principalId,
                taskId,
                RegistrationCredentialSources.AdminIssue,
                string.IsNullOrWhiteSpace(issuedBy) ? "admin" : issuedBy,
                DateTime.UtcNow.AddHours(lifetimeHours));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                credential_id = issued.CredentialId,
                principal_id = principalId,
                task_id = taskId,
                registration_secret = issued.Secret,
                expires_at = issued.ExpiresAt
            }, "Registration credential issued. The secret is shown only in this response."));
        });

        // 撤銷憑證時，一併撤銷以它註冊、仍有效的 session（並移除通道金鑰），代理不能再以既有 session 續期。
        // 以 principal_id＋task_id 撤銷時，這組主體與任務的所有 session 都一併結束。
        admin.MapPost("/registration-credentials/revoke", (HttpContext ctx,
            IRegistrationCredentialService credentials,
            ISessionService sessionService,
            ISessionKeyStore keyStore) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var revokedBy = RequestBodyHelper.GetPrincipalId(ctx);
            if (string.IsNullOrWhiteSpace(revokedBy)) revokedBy = "admin";
            var reason = body.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString() ?? "Revoked by admin"
                : "Revoked by admin";

            if (body.TryGetProperty("credential_id", out var idEl) && idEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(idEl.GetString()))
            {
                var credentialId = idEl.GetString()!;
                var revoked = credentials.Revoke(credentialId, reason, revokedBy);
                var endedById = sessionService.RevokeSessionsByCredential(credentialId, reason, revokedBy);
                foreach (var sessionId in endedById)
                    keyStore.Remove(sessionId);
                return Results.Ok(ApiResponseHelper.Success(new { revoked = revoked ? 1 : 0, sessions_revoked = endedById.Count }));
            }

            if (!RequestBodyHelper.TryGetRequired(body, "principal_id", out var principalId, out var err))
                return err!;
            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out err))
                return err!;

            var count = credentials.RevokeFor(principalId, taskId, reason, revokedBy);
            var ended = sessionService.RevokeSessionsBySubject(principalId, taskId, reason, revokedBy);
            foreach (var sessionId in ended)
                keyStore.Remove(sessionId);
            return Results.Ok(ApiResponseHelper.Success(new { revoked = count, sessions_revoked = ended.Count }));
        });

        admin.MapPost("/registration-credentials/list", (HttpContext ctx, IRegistrationCredentialService credentials) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            string? Optional(string name) => body.ValueKind == JsonValueKind.Object &&
                                             body.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
            var includeInactive = body.ValueKind == JsonValueKind.Object &&
                                  body.TryGetProperty("include_inactive", out var inactiveEl) &&
                                  inactiveEl.ValueKind == JsonValueKind.True;

            var items = credentials.List(Optional("principal_id"), Optional("task_id"), includeInactive)
                .Select(credential => new
                {
                    credential_id = credential.CredentialId,
                    principal_id = credential.PrincipalId,
                    task_id = credential.TaskId,
                    source = credential.Source,
                    created_at = credential.CreatedAt,
                    created_by = credential.CreatedBy,
                    expires_at = credential.ExpiresAt,
                    revoked_at = credential.RevokedAt,
                    revoke_reason = credential.RevokeReason,
                    last_used_at = credential.LastUsedAt,
                    use_count = credential.UseCount
                })
                .ToList();

            return Results.Ok(ApiResponseHelper.Success(new { credentials = items, total = items.Count }));
        });

        // 註冊主體
        admin.MapPost("/principals/create", (HttpContext ctx, BrokerDb db) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var actorType = body.GetProperty("actor_type").GetString() ?? "AI";
            var displayName = body.GetProperty("display_name").GetString() ?? "";
            var publicKey = body.TryGetProperty("public_key", out var pk) ? pk.GetString() : null;

            var principal = new Principal
            {
                PrincipalId = BrokerCore.IdGen.New("prn"),
                ActorType = Enum.Parse<ActorType>(actorType, true),
                DisplayName = displayName,
                PublicKey = publicKey,
                Status = EntityStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            db.Insert(principal);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                principal_id = principal.PrincipalId,
                actor_type = principal.ActorType.ToString(),
                display_name = principal.DisplayName
            }));
        });

        // 定義角色
        admin.MapPost("/roles/create", (HttpContext ctx, BrokerDb db) =>
        {
            if (!BrokerAuthorization.TryRequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var displayName = body.GetProperty("display_name").GetString() ?? "";
            var allowedTaskTypes = body.TryGetProperty("allowed_task_types", out var att)
                ? att.GetRawText() : "[]";
            var defaultCapIds = body.TryGetProperty("default_capability_ids", out var dci)
                ? dci.GetRawText() : "[]";

            var role = new Role
            {
                RoleId = BrokerCore.IdGen.New("role"),
                DisplayName = displayName,
                AllowedTaskTypes = allowedTaskTypes,
                DefaultCapabilityIds = defaultCapIds,
                Version = 1,
                Status = EntityStatus.Active
            };

            db.Insert(role);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                role_id = role.RoleId,
                display_name = role.DisplayName
            }));
        });

        // 查詢 epoch（允許所有已認證角色查詢，但 kill-switch/revoke/create 需 admin）
        admin.MapPost("/epoch/query", (HttpContext ctx, IRevocationService revocationService) =>
        {
            var epoch = revocationService.GetCurrentEpoch();
            return Results.Ok(ApiResponseHelper.Success(new { current_epoch = epoch }));
        });
    }
}
