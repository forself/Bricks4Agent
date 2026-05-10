using Broker.Helpers;

namespace Broker.Middleware;

/// <summary>
/// 內部 bot 認證中介軟體——把 `X-Internal-Bot-Token` header 比對 env var
/// `BOT_INTERNAL_TOKEN`、相符就把 (`prn_dc_bot`, `role_user`) 注入 HttpContext.Items、
/// 讓後續 endpoint 跟既有 ACL / Approval / Audit 機制把 bot 當一個正常 principal 處理。
///
/// 為什麼不走 cookie：bot 容器不能跟 broker 跑 ECDH session 交握、也不適合管 cookie 過期。
/// 用 shared-secret header 跟 broker 直連、最務實。
///
/// 安全性：
/// - token 走容器 env vars、沒寫進 image、broker 跟 bot 兩邊都從 host env / .env 拿
/// - 容器網路只 b4a-trading-net、外面打不到 broker 內部 port
/// - bot 拿到的 role 是 `role_user`、CapabilityAclService 預設只讓它呼叫 strategy.signal
///   / quote.* / trading.account / trading.perpetual，**trading.order 直接 ACL deny**
/// - 即使 bot 被 prompt injection 也過不了 PoolDispatcher 的 gate
///
/// 設計選擇：同時設 BrokerAuth + CurrentUser 兩套 key，不論 endpoint 從哪邊讀都看到 bot identity。
/// </summary>
public class InternalBotAuthMiddleware
{
    public const string HeaderName      = "X-Internal-Bot-Token";        // role_user calls
    public const string AdminHeaderName = "X-Internal-Bot-Admin-Token";  // role_admin calls（手機 Discord 按鈕審核）
    public const string BotPrincipalId  = "prn_dc_bot";
    public const string BotRoleId       = "role_user";
    public const string BotAdminRoleId  = "role_admin";

    private readonly RequestDelegate _next;
    private readonly string _expectedToken;
    private readonly string _expectedAdminToken;
    private readonly ILogger<InternalBotAuthMiddleware> _logger;

    public InternalBotAuthMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<InternalBotAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        // user-role token：BOT_INTERNAL_TOKEN env 優先、退而 appsettings
        var fromEnv = Environment.GetEnvironmentVariable("BOT_INTERNAL_TOKEN");
        _expectedToken = !string.IsNullOrWhiteSpace(fromEnv)
            ? fromEnv
            : (config["Bot:InternalToken"] ?? "");

        // admin-role token：bot 手機 approval button 用、給 bot 暫時拿到 role_admin
        // 跟 user token 嚴格分開、不能用同一 token、避免 prompt-injected LLM 拿 user token
        // 偷敲 /admin/* 端點
        var adminFromEnv = Environment.GetEnvironmentVariable("BOT_INTERNAL_ADMIN_TOKEN");
        _expectedAdminToken = !string.IsNullOrWhiteSpace(adminFromEnv)
            ? adminFromEnv
            : (config["Bot:InternalAdminToken"] ?? "");

        if (string.IsNullOrEmpty(_expectedToken))
            logger.LogInformation("InternalBotAuth disabled (no BOT_INTERNAL_TOKEN configured)");
        else
            logger.LogInformation("InternalBotAuth enabled (user token len={Len}, admin token configured={Adm})",
                _expectedToken.Length, !string.IsNullOrEmpty(_expectedAdminToken));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // ── admin token：嚴格符合才放行為 role_admin（bot 點按鈕審核時用）──
        if (!string.IsNullOrEmpty(_expectedAdminToken)
            && context.Request.Headers.TryGetValue(AdminHeaderName, out var providedAdmin))
        {
            var providedStr = providedAdmin.ToString();
            if (!string.IsNullOrEmpty(providedStr)
                && CryptographicEquals(providedStr, _expectedAdminToken))
            {
                InjectIdentity(context, BotAdminRoleId, currentUserRole: "admin");
                _logger.LogDebug("Internal bot ADMIN authenticated for {Path}", context.Request.Path);
                await _next(context);
                return;
            }
            // admin header 帶但對不上 → 直接 401、不要 fallthrough 到 user token
            _logger.LogWarning("InternalBotAuth: invalid admin token from {Ip} on {Path}",
                context.Connection.RemoteIpAddress, context.Request.Path);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("invalid bot admin token");
            return;
        }

        // ── user token（既有行為）──
        if (!string.IsNullOrEmpty(_expectedToken)
            && context.Request.Headers.TryGetValue(HeaderName, out var provided))
        {
            var providedStr = provided.ToString();
            if (!string.IsNullOrEmpty(providedStr)
                && CryptographicEquals(providedStr, _expectedToken))
            {
                InjectIdentity(context, BotRoleId, currentUserRole: "user");
                _logger.LogDebug("Internal bot authenticated for {Path}", context.Request.Path);
            }
            else
            {
                _logger.LogWarning("InternalBotAuth: invalid token from {Ip} on {Path}",
                    context.Connection.RemoteIpAddress, context.Request.Path);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("invalid bot token");
                return;
            }
        }
        // 沒帶 header → 一般流程繼續（cookie auth 等等）
        await _next(context);
    }

    private static void InjectIdentity(HttpContext context, string roleId, string currentUserRole)
    {
        // 兩套 identity key 都設、不論 endpoint 從哪讀
        context.Items[BrokerAuthMiddleware.PrincipalIdKey] = BotPrincipalId;
        context.Items[BrokerAuthMiddleware.RoleIdKey]      = roleId;
        context.Items[BrokerAuthMiddleware.SessionIdKey]   = "dc-bot-session";
        context.Items[BrokerAuthMiddleware.TaskIdKey]      = "dc-bot";
        context.Items[CurrentUserMiddleware.PrincipalKey]  = BotPrincipalId;
        context.Items[CurrentUserMiddleware.RoleKey]       = currentUserRole;
    }

    /// <summary>常數時間比對、防 timing attack。</summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

public static class InternalBotAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseInternalBotAuth(this IApplicationBuilder app)
        => app.UseMiddleware<InternalBotAuthMiddleware>();
}
