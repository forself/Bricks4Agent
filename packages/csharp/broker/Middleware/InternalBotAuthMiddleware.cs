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
    public const string HeaderName = "X-Internal-Bot-Token";
    public const string BotPrincipalId = "prn_dc_bot";
    public const string BotRoleId = "role_user";

    private readonly RequestDelegate _next;
    private readonly string _expectedToken;
    private readonly ILogger<InternalBotAuthMiddleware> _logger;

    public InternalBotAuthMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<InternalBotAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        // 兩個來源：appsettings `Bot:InternalToken` 或 env `BOT_INTERNAL_TOKEN`（後者優先）
        var fromEnv = Environment.GetEnvironmentVariable("BOT_INTERNAL_TOKEN");
        _expectedToken = !string.IsNullOrWhiteSpace(fromEnv)
            ? fromEnv
            : (config["Bot:InternalToken"] ?? "");

        if (string.IsNullOrEmpty(_expectedToken))
            logger.LogInformation("InternalBotAuth disabled (no BOT_INTERNAL_TOKEN configured)");
        else
            logger.LogInformation("InternalBotAuth enabled (token len={Len})", _expectedToken.Length);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!string.IsNullOrEmpty(_expectedToken)
            && context.Request.Headers.TryGetValue(HeaderName, out var provided))
        {
            var providedStr = provided.ToString();
            if (!string.IsNullOrEmpty(providedStr)
                && CryptographicEquals(providedStr, _expectedToken))
            {
                // 兩套 identity key 都設、不論 endpoint 從哪讀
                context.Items[BrokerAuthMiddleware.PrincipalIdKey] = BotPrincipalId;
                context.Items[BrokerAuthMiddleware.RoleIdKey]      = BotRoleId;
                context.Items[BrokerAuthMiddleware.SessionIdKey]   = "dc-bot-session";
                context.Items[BrokerAuthMiddleware.TaskIdKey]      = "dc-bot";
                context.Items[CurrentUserMiddleware.PrincipalKey]  = BotPrincipalId;
                // CurrentUser 用 "user" / "admin" 簡短形式
                context.Items[CurrentUserMiddleware.RoleKey]       = "user";
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
