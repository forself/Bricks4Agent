using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Crypto;
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
            ICapabilityCatalog capabilityCatalog) =>
        {
            var body = GetBody(ctx);
            var taskId = body.GetProperty("task_id").GetString() ?? "";
            var principalId = body.GetProperty("principal_id").GetString() ?? "";
            var roleId = body.GetProperty("role_id").GetString() ?? "";

            // 取得 client 的 ECDH 臨時公鑰
            var clientPub = ctx.Items[EncryptionMiddleware.ClientEphemeralPubKey] as string;
            if (string.IsNullOrEmpty(clientPub))
                return Results.BadRequest(ApiResponseHelper.Error("Missing client ephemeral public key."));

            // 取得當前 epoch
            var currentEpoch = revocationService.GetCurrentEpoch();

            // 生成 session_id 以做 HKDF salt
            var tempSessionId = BrokerCore.IdGen.New("ses");

            // ECDH → session_key
            var sessionKey = crypto.DeriveSessionKey(clientPub, tempSessionId);

            // 發行 Scoped Token
            var jti = BrokerCore.IdGen.New("jti");

            // 為 session 自動授予角色預設能力
            var defaultCapabilityIds = GetDefaultCapabilities(roleId, capabilityCatalog);

            var tokenClaims = new ScopedTokenClaims
            {
                PrincipalId = principalId,
                Jti = jti,
                TaskId = taskId,
                SessionId = tempSessionId,
                RoleId = roleId,
                CapabilityIds = defaultCapabilityIds,
                Scope = "{}",
                Epoch = currentEpoch
            };

            var scopedToken = tokenService.GenerateToken(tokenClaims);

            // 儲存 session（encrypted_session_key 由 keyStore 處理）
            var session = sessionService.RegisterSession(
                taskId, principalId, roleId, jti, currentEpoch, "");
            // 覆蓋 session_id 為 HKDF 使用的 ID
            // 注意：RegisterSession 內部生成了 session_id，但我們需要用 tempSessionId
            // 因為 HKDF 已用 tempSessionId 作為 salt
            // 解法：直接更新 DB
            // 但更好的做法是讓 RegisterSession 接受預設 ID
            // Phase 1 簡化：直接使用 RegisterSession 產生的 ID 重新 derive

            // 修正：使用 session.SessionId 重新 derive session_key
            sessionKey = crypto.DeriveSessionKey(clientPub, session.SessionId);
            keyStore.Store(session.SessionId, sessionKey);

            // 更新 token claims 使用正確的 session_id
            tokenClaims.SessionId = session.SessionId;
            scopedToken = tokenService.GenerateToken(tokenClaims);

            // 自動授予預設能力
            foreach (var capId in defaultCapabilityIds)
            {
                capabilityCatalog.CreateGrant(
                    taskId, session.SessionId, principalId,
                    capId, "{}", -1, // -1 = 無限配額
                    DateTime.UtcNow.AddHours(1));
            }

            // 回應（由 EncryptionMiddleware 處理加密 — 但交握回應需特殊處理）
            // 因為此時 EncryptionMiddleware 的回應加密需要 session_key，
            // 我們透過 HttpContext.Items 傳遞
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
            var body = GetBody(ctx);
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

    private static string[] GetDefaultCapabilities(string roleId, ICapabilityCatalog catalog)
    {
        // Phase 1: 依角色分配預設能力
        return roleId switch
        {
            "role_reader" => new[] { "file.read", "file.list", "file.search_name", "file.search_content" },
            "role_sa" => new[] { "file.read", "file.list", "file.search_name", "file.search_content" },
            "role_executor" => new[] { "file.read", "file.list", "file.search_name", "file.search_content" },
            "role_admin" => new[] { "file.read", "file.list", "file.search_name", "file.search_content", "file.write", "command.execute" },
            _ => Array.Empty<string>()
        };
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
