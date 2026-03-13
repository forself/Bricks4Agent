using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/admin/*</summary>
public static class AdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin");

        // Kill Switch：epoch 遞增 → 所有舊 token 即時失效
        admin.MapPost("/kill-switch", (HttpContext ctx, IRevocationService revocationService) =>
        {
            var body = GetBody(ctx);
            var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "system";
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
            var body = GetBody(ctx);
            var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "system";
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

        // 註冊主體
        admin.MapPost("/principals/create", (HttpContext ctx, BrokerDb db) =>
        {
            var body = GetBody(ctx);
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
            var body = GetBody(ctx);
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

        // 查詢 epoch
        admin.MapPost("/epoch/query", (HttpContext ctx, IRevocationService revocationService) =>
        {
            var epoch = revocationService.GetCurrentEpoch();
            return Results.Ok(ApiResponseHelper.Success(new { current_epoch = epoch }));
        });
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
