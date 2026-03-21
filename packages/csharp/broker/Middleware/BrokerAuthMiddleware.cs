using BrokerCore.Services;
using Broker.Helpers;

namespace Broker.Middleware;

/// <summary>
/// 驗證中介軟體 —— 管線第二層（在 Encryption 之後，Audit 之前）
///
/// 職責：
/// 1. 從解密後的明文 body 讀取 Scoped Token（或 admin JWT）
/// 2. 驗證 Token 簽章 + 時效
/// 3. Epoch 閘道：token.epoch &lt; current_epoch → 401
/// 4. Session 狀態檢查（active、未過期）
/// 5. 將已驗證的 claims 注入 HttpContext.Items
///
/// 排除路徑：
/// - /api/v1/health（無需驗證）
/// - /api/v1/sessions/register（初始交握，用 admin JWT 驗證）
/// </summary>
public class BrokerAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IScopedTokenService _tokenService;
    private readonly IRevocationService _revocationService;
    private readonly ILogger<BrokerAuthMiddleware> _logger;

    // HttpContext.Items 鍵名（供後續 endpoint 讀取）
    public const string ClaimsKey = "broker_claims";
    public const string PrincipalIdKey = "broker_principal_id";
    public const string TaskIdKey = "broker_task_id";
    public const string SessionIdKey = "broker_session_id";
    public const string RoleIdKey = "broker_role_id";
    public const string EpochKey = "broker_epoch";

    // 排除驗證的路徑
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/health",
        "/api/v1/sessions/register" // 初始交握不帶 Scoped Token
    };

    private static bool IsTrustedInternalPlainJsonPath(string path)
    {
        return path.StartsWith("/api/v1/high-level/line/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/tool-specs/", StringComparison.OrdinalIgnoreCase);
    }

    public BrokerAuthMiddleware(
        RequestDelegate next,
        IScopedTokenService tokenService,
        IRevocationService revocationService,
        ILogger<BrokerAuthMiddleware> logger)
    {
        _next = next;
        _tokenService = tokenService;
        _revocationService = revocationService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // 排除不需驗證的端點
        if (ExcludedPaths.Contains(path)
            || IsTrustedInternalPlainJsonPath(path)
            || path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase)
            || context.Request.Method != "POST")
        {
            await _next(context);
            return;
        }

        // ── 1. 從解密後的 body 提取 Token ──
        // EncryptionMiddleware 已將明文注入 HttpContext.Items
        string? decryptedBody = null;
        if (context.Items.TryGetValue(EncryptionMiddleware.DecryptedBodyKey, out var bodyObj))
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
                if (doc.RootElement.TryGetProperty("scoped_token", out var tokenProp))
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
        var claims = _tokenService.ValidateToken(scopedToken);
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

        // ── 5. 注入已驗證 claims ──
        context.Items[ClaimsKey] = claims;
        context.Items[PrincipalIdKey] = claims.PrincipalId;
        context.Items[TaskIdKey] = claims.TaskId;
        context.Items[SessionIdKey] = claims.SessionId;
        context.Items[RoleIdKey] = claims.RoleId;
        context.Items[EpochKey] = claims.Epoch;

        await _next(context);
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
