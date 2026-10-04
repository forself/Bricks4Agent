using BrokerCore.Models;
using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Middleware;

/// <summary>
/// 驗證中介軟體 —— 管線第二層（在 Encryption 之後，Audit 之前）
///
/// 是否需要驗證，一律由已匹配端點的 <see cref="BrokerAuthPolicy"/> metadata 決定，
/// 不比對 HTTP 方法字串或請求路徑；沒有標示政策的 /api/v1 端點視為 ScopedToken（預設拒絕）。
///
/// ScopedToken 政策的職責：
/// 1. 取得 Scoped Token：POST 取自解密後 body 的 scoped_token 或 Bearer；其他方法只取 Bearer
/// 2. 驗證 Token 簽章 + 時效
/// 3. Epoch 閘道：token.epoch &lt; current_epoch → 401
/// 4. 撤銷檢查（JTI、Session）
/// 5. Session 綁定：請求不是交握信封；走加密通道時通道的 session 必須是 token 的 session；
///    token 的 session 必須存在、為 Active、未過期，且主體、任務、角色與 session 記錄一致
///    （Bearer／明文路徑沒有通道，只檢查 session 狀態與一致性）。任一不符一律 401。
/// 6. 將已驗證的 claims 與 session 記錄注入 HttpContext.Items
///
/// 其他政策：Public / SessionBootstrap / SignedLink / LocalAdminSession / PortalSession 交給 handler 自行驗證；
/// WorkerSignature 必須已通過 WorkerIdentityAuthMiddleware，否則 401。
/// 沒有匹配端點的請求（之後會 404）與 /api/v1 以外的端點（例如 /dev，由 DevEndpointGuard 管）直接放行。
/// </summary>
public class BrokerAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IScopedTokenService _tokenService;
    private readonly IRevocationService _revocationService;
    private readonly ISessionService _sessionService;
    private readonly ILogger<BrokerAuthMiddleware> _logger;

    // HttpContext.Items 鍵名（供後續 endpoint 讀取）
    public const string ClaimsKey = "broker_claims";
    public const string PrincipalIdKey = "broker_principal_id";
    public const string TaskIdKey = "broker_task_id";
    public const string SessionIdKey = "broker_session_id";
    public const string RoleIdKey = "broker_role_id";
    public const string EpochKey = "broker_epoch";

    /// <summary>已通過綁定檢查的 <see cref="ContainerSession"/>，供 heartbeat 等端點直接使用。</summary>
    public const string SessionRecordKey = "broker_session";

    public BrokerAuthMiddleware(
        RequestDelegate next,
        IScopedTokenService tokenService,
        IRevocationService revocationService,
        ISessionService sessionService,
        ILogger<BrokerAuthMiddleware> logger)
    {
        _next = next;
        _tokenService = tokenService;
        _revocationService = revocationService;
        _sessionService = sessionService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var policy = BrokerAuthPolicyResolver.Resolve(context);

        switch (policy)
        {
            case null:
            case BrokerAuthPolicy.Public:
            case BrokerAuthPolicy.SessionBootstrap:
            case BrokerAuthPolicy.SignedLink:
            case BrokerAuthPolicy.LocalAdminSession:
            case BrokerAuthPolicy.PortalSession:
                await _next(context);
                return;

            case BrokerAuthPolicy.WorkerSignature:
                // 只有 worker 簽章端點才因 worker 驗證通過而略過 scoped token；
                // 未經驗證（含 WorkerAuth:Enforce=false 時）一律拒絕，不退回 scoped token。
                if (context.Items.ContainsKey(WorkerIdentityAuthMiddleware.WorkerTypeItemKey))
                {
                    await _next(context);
                    return;
                }

                _logger.LogWarning("Missing worker authentication for {Path}", path);
                await WriteAuthError(context, 401, "Worker authentication required.");
                return;

            case BrokerAuthPolicy.ScopedToken:
            default:
                break;
        }

        // ── 1. 從解密後的 body 提取 Token（只限 POST；其他方法只接受 Bearer） ──
        // EncryptionMiddleware 已將明文注入 HttpContext.Items
        string? decryptedBody = null;
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Items.TryGetValue(EncryptionMiddleware.DecryptedBodyKey, out var bodyObj))
        {
            decryptedBody = bodyObj as string;
        }

        // 嘗試從 body 的 JSON 中提取 scoped_token 欄位
        string? scopedToken = null;
        if (!string.IsNullOrEmpty(decryptedBody))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(decryptedBody);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("scoped_token", out var tokenProp) &&
                    tokenProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    scopedToken = tokenProp.GetString();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // body 不是 JSON 或沒有 scoped_token 欄位
            }
        }

        // 也檢查 Authorization header（admin 端點可能使用 Bearer token）
        if (string.IsNullOrEmpty(scopedToken))
        {
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                scopedToken = authHeader["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrEmpty(scopedToken))
        {
            _logger.LogWarning("Missing scoped_token in request to {Path}", path);
            await WriteAuthError(context, 401, "Missing authentication token.");
            return;
        }

        // ── 2. 驗證 Token ──
        // 格式錯誤的 token 會由 token handler 以例外回報（ScopedTokenService 的既有契約：由呼叫端處理），
        // 這裡一律視為無效 token 回 401，不讓例外穿過加密層變成 500 或中斷的回應。
        ScopedTokenClaims? claims;
        try
        {
            claims = _tokenService.ValidateToken(scopedToken);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException)
        {
            claims = null;
        }

        if (claims == null)
        {
            _logger.LogWarning("Invalid token for {Path}", path);
            await WriteAuthError(context, 401, "Invalid or expired token.");
            return;
        }

        // ── 3. Epoch 閘道 ──
        var currentEpoch = _revocationService.GetCurrentEpoch();
        if (claims.Epoch < currentEpoch)
        {
            _logger.LogWarning(
                "Epoch mismatch: token.epoch={TokenEpoch}, current={CurrentEpoch}, principal={PrincipalId}",
                claims.Epoch, currentEpoch, claims.PrincipalId);
            await WriteAuthError(context, 401, "Token invalidated by system epoch advancement.");
            return;
        }

        // ── 4. 撤銷檢查（JTI + Session） ──
        if (_revocationService.IsRevoked(claims.Jti))
        {
            _logger.LogWarning("Token JTI revoked: {Jti}", claims.Jti);
            await WriteAuthError(context, 401, "Token has been revoked.");
            return;
        }

        if (_revocationService.IsRevoked(claims.SessionId))
        {
            _logger.LogWarning("Session revoked: {SessionId}", claims.SessionId);
            await WriteAuthError(context, 401, "Session has been revoked.");
            return;
        }

        // ── 5. Session 綁定 ──
        var session = await CheckSessionBindingAsync(context, claims, path);
        if (session == null)
        {
            return;
        }

        // ── 6. 注入已驗證 claims 與 session 記錄 ──
        context.Items[SessionRecordKey] = session;
        context.Items[ClaimsKey] = claims;
        context.Items[PrincipalIdKey] = claims.PrincipalId;
        context.Items[TaskIdKey] = claims.TaskId;
        context.Items[SessionIdKey] = claims.SessionId;
        context.Items[RoleIdKey] = claims.RoleId;
        context.Items[EpochKey] = claims.Epoch;

        await _next(context);
    }

    /// <summary>
    /// token 只能在自己的 session 使用：拒絕交握信封、跨通道使用，以及 session 不存在、已關閉／撤銷、已過期或與 claims 不一致。
    /// 通過時回傳 session 記錄；失敗時已寫出 401 並回傳 null。
    /// </summary>
    private async Task<ContainerSession?> CheckSessionBindingAsync(HttpContext context, ScopedTokenClaims claims, string path)
    {
        // 交握信封只用於註冊（EncryptionMiddleware 已擋下其他端點，這裡是縱深防禦）。
        if (context.Items.TryGetValue(EncryptionMiddleware.IsHandshakeKey, out var handshake) && handshake is true)
        {
            _logger.LogWarning("Scoped token presented in a handshake envelope for {Path}", path);
            await WriteAuthError(context, 401, "Handshake envelopes are only accepted for session registration.");
            return null;
        }

        // 走加密通道時，通道的 session 必須就是 token 的 session。
        if (context.Items.TryGetValue(EncryptionMiddleware.SessionIdKey, out var channelObj) &&
            channelObj is string channelSessionId &&
            !string.IsNullOrEmpty(channelSessionId) &&
            !string.Equals(channelSessionId, claims.SessionId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Token of session {TokenSessionId} presented over channel {ChannelSessionId} for {Path}",
                claims.SessionId, channelSessionId, path);
            await WriteAuthError(context, 401, "Token does not belong to this session.");
            return null;
        }

        var session = string.IsNullOrEmpty(claims.SessionId) ? null : _sessionService.GetSession(claims.SessionId);
        if (session == null || session.Status != SessionStatus.Active)
        {
            _logger.LogWarning("Session not active: {SessionId}", claims.SessionId);
            await WriteAuthError(context, 401, "Session is not active.");
            return null;
        }

        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning("Session expired: {SessionId}", claims.SessionId);
            await WriteAuthError(context, 401, "Session expired.");
            return null;
        }

        if (!string.Equals(session.PrincipalId, claims.PrincipalId, StringComparison.Ordinal) ||
            !string.Equals(session.TaskId, claims.TaskId, StringComparison.Ordinal) ||
            !string.Equals(session.RoleId, claims.RoleId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Token claims do not match session {SessionId}", claims.SessionId);
            await WriteAuthError(context, 401, "Token does not match its session.");
            return null;
        }

        return session;
    }

    /// <summary>
    /// M-10 修復：統一使用 ApiResponseHelper 格式
    /// </summary>
    private static async Task WriteAuthError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponseHelper.Error(message, statusCode));
    }
}

/// <summary>
/// BrokerAuthMiddleware 擴展方法
/// </summary>
public static class BrokerAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseBrokerAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<BrokerAuthMiddleware>();
    }
}
