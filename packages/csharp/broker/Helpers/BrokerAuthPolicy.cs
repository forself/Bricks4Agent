namespace Broker.Helpers;

/// <summary>
/// /api/v1 端點的驗證政策。由端點 metadata 決定，不依 HTTP 方法字串或請求路徑判斷。
/// 沒有標示政策的 /api/v1 端點一律視為 <see cref="ScopedToken"/>（預設拒絕）。
/// </summary>
public enum BrokerAuthPolicy
{
    /// <summary>需要有效 scoped token（POST 取自解密後 body 的 scoped_token 或 Bearer；其他方法只取 Bearer）。</summary>
    ScopedToken = 0,

    /// <summary>不驗證（健康探測）。</summary>
    Public,

    /// <summary>不需 token，由 handler 驗證註冊條件（sessions/register）。</summary>
    SessionBootstrap,

    /// <summary>不需 token，由 handler 驗證連結簽章或連結 token。</summary>
    SignedLink,

    /// <summary>不需 scoped token，由 handler／群組 filter 以 LocalAdminAuthService 驗證 cookie。</summary>
    LocalAdminSession,

    /// <summary>不需 scoped token，由 handler 以 portal 工作階段驗證。</summary>
    PortalSession,

    /// <summary>必須已通過 WorkerIdentityAuthMiddleware 的簽章驗證，否則 401。</summary>
    WorkerSignature
}

/// <summary>端點上的驗證政策 metadata。同一端點有多筆時，最後加入者（最具體的群組或端點）為準。</summary>
public sealed class BrokerAuthPolicyMetadata
{
    public BrokerAuthPolicyMetadata(BrokerAuthPolicy policy)
    {
        Policy = policy;
    }

    public BrokerAuthPolicy Policy { get; }

    public override string ToString() => $"BrokerAuthPolicy:{Policy}";
}

public static class BrokerAuthPolicyExtensions
{
    /// <summary>為端點或路由群組標示驗證政策。</summary>
    public static TBuilder WithBrokerAuthPolicy<TBuilder>(this TBuilder builder, BrokerAuthPolicy policy)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new BrokerAuthPolicyMetadata(policy));
}

/// <summary>
/// 依已匹配的端點解析驗證政策。EncryptionMiddleware、BodySizeLimitMiddleware、
/// WorkerIdentityAuthMiddleware、BrokerAuthMiddleware 共用，確保判斷一致。
/// </summary>
public static class BrokerAuthPolicyResolver
{
    public const string ApiPrefix = "/api/v1";

    /// <summary>
    /// 回傳 null 表示這個請求不受 /api/v1 驗證政策管轄：
    /// 沒有匹配端點（之後會 404）、框架產生的非路由端點（例如 405），或不在 /api/v1 之下的端點（例如 /dev）。
    /// </summary>
    public static BrokerAuthPolicy? Resolve(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint == null)
        {
            return null;
        }

        var metadata = endpoint.Metadata.GetMetadata<BrokerAuthPolicyMetadata>();
        if (metadata != null)
        {
            return metadata.Policy;
        }

        if (endpoint is RouteEndpoint routeEndpoint && IsApiRoutePattern(routeEndpoint.RoutePattern.RawText))
        {
            return BrokerAuthPolicy.ScopedToken;
        }

        return null;
    }

    /// <summary>已匹配端點的路由樣板（不含尾斜線），沒有路由端點時為 null。</summary>
    public static string? GetRoutePattern(HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint routeEndpoint)
        {
            return null;
        }

        return NormalizeRoute(routeEndpoint.RoutePattern.RawText);
    }

    public static string NormalizeRoute(string? route)
    {
        if (string.IsNullOrEmpty(route))
        {
            return string.Empty;
        }

        var normalized = route.TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static bool IsApiRoutePattern(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return false;
        }

        var pattern = rawText.StartsWith('/') ? rawText : "/" + rawText;
        return pattern.Equals(ApiPrefix, StringComparison.OrdinalIgnoreCase)
            || pattern.StartsWith(ApiPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
