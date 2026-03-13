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

        // 初始交握：ECDH 金鑰交換 + 建立 session + 發行 scoped token
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
            var taskId = body.GetProperty("task_id").GetString() ?? "";
            var principalId = body.GetProperty("principal_id").GetString() ?? "";
            var roleId = body.GetProperty("role_id").GetString() ?? "";

            // ── C-2 修復：身份驗證 ──
            // 驗證 principal_id 確實存在於 DB
            var principal = db.Get<Principal>(principalId);
            if (principal == null || principal.Status != EntityStatus.Active)
                return Results.BadRequest(ApiResponseHelper.Error("Invalid or inactive principal_id."));

            // 驗證 role_id 確實存在且有效
            var role = db.Get<Role>(roleId);
            if (role == null || role.Status != EntityStatus.Active)
                return Results.BadRequest(ApiResponseHelper.Error("Invalid or inactive role_id."));

            // 取得 client 的 ECDH 臨時公鑰
            var clientPub = ctx.Items[EncryptionMiddleware.ClientEphemeralPubKey] as string;
            if (string.IsNullOrEmpty(clientPub))
                return Results.BadRequest(ApiResponseHelper.Error("Missing client ephemeral public key."));

            // 取得當前 epoch
            var currentEpoch = revocationService.GetCurrentEpoch();

            // ── H-2 修復：消除 double derivation ──
            // 先建立 session 取得 session_id，再一次性 derive key
            var jti = BrokerCore.IdGen.New("jti");
            var session = sessionService.RegisterSession(
                taskId, principalId, roleId, jti, currentEpoch, "");

            // ECDH → session_key（一次性，用 session.SessionId 作為 HKDF salt）
            var sessionKey = crypto.DeriveSessionKey(clientPub, session.SessionId);
            keyStore.Store(session.SessionId, sessionKey);

            // 為 session 自動授予角色預設能力
            var defaultCapabilityIds = GetDefaultCapabilities(roleId, role);

            var tokenClaims = new ScopedTokenClaims
            {
                PrincipalId = principalId,
                Jti = jti,
                TaskId = taskId,
                SessionId = session.SessionId,
                RoleId = roleId,
                CapabilityIds = defaultCapabilityIds,
                Scope = "{}",
                Epoch = currentEpoch
            };

            var scopedToken = tokenService.GenerateToken(tokenClaims);

            // 自動授予預設能力（H-7 修復：grant 過期時間與 session 一致）
            foreach (var capId in defaultCapabilityIds)
            {
                capabilityCatalog.CreateGrant(
                    taskId, session.SessionId, principalId,
                    capId, "{}", -1, // -1 = 無限配額
                    session.ExpiresAt); // 與 session 過期時間一致，而非硬編碼 1 小時
            }

            // 回應（由 EncryptionMiddleware 處理加密 — 交握回應透過 HttpContext.Items 傳遞 session_key）
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
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? "";

            var success = sessionService.Heartbeat(sessionId);
            if (!success)
                return Results.BadRequest(ApiResponseHelper.Error("Session not found or inactive."));

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Heartbeat acknowledged."));
        });

        sessions.MapPost("/close", (HttpContext ctx, ISessionService sessionService, ISessionKeyStore keyStore) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? "";
            var reason = body.TryGetProperty("reason", out var r)
                ? r.GetString() ?? "" : "Client requested close";

            keyStore.Remove(sessionId);
            var success = sessionService.CloseSession(sessionId, reason);

            if (!success)
                return Results.BadRequest(ApiResponseHelper.Error("Session not found or already closed."));

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Session closed."));
        });
    }

    /// <summary>
    /// 取得角色的預設能力清單
    /// 優先使用 DB Role.DefaultCapabilityIds，若無則使用硬編碼映射
    /// </summary>
    private static string[] GetDefaultCapabilities(string roleId, Role role)
    {
        // 嘗試從 DB Role 的 DefaultCapabilityIds 讀取
        if (!string.IsNullOrEmpty(role.DefaultCapabilityIds) && role.DefaultCapabilityIds != "[]")
        {
            try
            {
                var caps = System.Text.Json.JsonSerializer.Deserialize<string[]>(role.DefaultCapabilityIds);
                if (caps != null && caps.Length > 0)
                    return caps;
            }
            catch (System.Text.Json.JsonException)
            {
                // 解析失敗 → 使用硬編碼映射
            }
        }

        // 退回硬編碼映射（Phase 1 相容）
        return roleId switch
        {
            "role_reader" => new[] { "file.read", "file.list", "file.search_name", "file.search_content" },
            "role_sa" => new[] { "file.read", "file.list", "file.search_name", "file.search_content" },
            "role_executor" => new[] { "file.read", "file.list", "file.search_name", "file.search_content" },
            "role_admin" => new[] { "file.read", "file.list", "file.search_name", "file.search_content", "file.write", "command.execute" },
            _ => Array.Empty<string>()
        };
    }
}
