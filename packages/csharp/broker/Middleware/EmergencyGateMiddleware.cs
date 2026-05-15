using Broker.Services;

namespace Broker.Middleware;

/// <summary>
/// W14 P5 — ReadOnlyMode 寫入閘道
///
/// IEmergencyState.ReadOnlyMode = true 時、所有 POST/PUT/DELETE 回 503，
/// 例外清單（admin 還能解鎖、bot 還能 health check、user 還能登入）：
/// - /api/v1/emergency/*  → admin 解鎖唯一通道
/// - /api/v1/auth/*       → 不能鎖在門外
/// - /api/v1/health       → LB / probe
/// - /metrics             → Prometheus scrape
///
/// 也順便把 P1 KillSwitch 的 trading-write 阻擋寫在這裡（trading.* endpoint），
/// 免得分散到 InProcessDispatcher / TradingEndpoints / AutoTraderService 三處。
/// （AutoTrader 主迴圈本身已被 Disable()、不會主動下單；這裡是防 manual POST）
/// </summary>
public class EmergencyGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IEmergencyState _state;
    private readonly ILogger<EmergencyGateMiddleware> _logger;

    private static readonly string[] ReadOnlyExemptPrefixes = new[]
    {
        "/api/v1/emergency/",
        "/api/v1/auth/",
        "/api/v1/health",
        "/metrics",
        "/dev/",                  // dev/health endpoints
    };

    private static readonly string[] KillSwitchBlockedPrefixes = new[]
    {
        "/api/v1/trading/order",
        "/api/v1/auto-trader/enable",   // 防 admin 解 killswitch 同時 reenable autotrader
        "/api/v1/auto-trader/watch",    // 新加 watch 也擋
    };

    public EmergencyGateMiddleware(RequestDelegate next, IEmergencyState state,
        ILogger<EmergencyGateMiddleware> logger)
    {
        _next = next;
        _state = state;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var path = ctx.Request.Path.Value ?? "";
        var isWrite = method != "GET" && method != "HEAD" && method != "OPTIONS";

        // P5 — ReadOnly 阻擋
        if (isWrite && _state.ReadOnlyMode && !MatchesAny(path, ReadOnlyExemptPrefixes))
        {
            _logger.LogWarning("Emergency: read-only mode blocked {Method} {Path}", method, path);
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(
                "{\"success\":false,\"error\":\"Broker is in read-only lockdown mode. Writes are blocked. Use POST /api/v1/emergency/lockdown {\\\"enable\\\":false} to disable.\"}");
            return;
        }

        // P1 — KillSwitch 阻擋 trading writes（即使 admin 一時忘記也擋）
        if (isWrite && _state.KillSwitchActive && MatchesAny(path, KillSwitchBlockedPrefixes))
        {
            _logger.LogWarning("Emergency: kill-switch blocked {Method} {Path}", method, path);
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(
                "{\"success\":false,\"error\":\"KILL_SWITCH active. Trading and AutoTrader writes blocked. Use POST /api/v1/emergency/clear first.\"}");
            return;
        }

        await _next(ctx);
    }

    private static bool MatchesAny(string path, string[] prefixes)
    {
        foreach (var p in prefixes)
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

public static class EmergencyGateExtensions
{
    public static IApplicationBuilder UseEmergencyGate(this IApplicationBuilder app)
        => app.UseMiddleware<EmergencyGateMiddleware>();
}
