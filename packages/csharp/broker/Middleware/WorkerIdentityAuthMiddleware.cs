using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Middleware;

public sealed class WorkerIdentityAuthMiddleware
{
    public const string WorkerTypeItemKey = "worker_type";
    public const string WorkerKeyIdItemKey = "worker_key_id";

    private readonly RequestDelegate _next;
    private readonly WorkerIdentityAuthOptions _options;
    private readonly WorkerIdentityAuthService _authService;

    public WorkerIdentityAuthMiddleware(
        RequestDelegate next,
        WorkerIdentityAuthOptions options,
        WorkerIdentityAuthService authService)
    {
        _next = next;
        _options = options;
        _authService = authService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Enforce=false 時不驗證、也不標記 worker 身分；
        // WorkerSignature 端點因此會在 BrokerAuthMiddleware 被拒（不會退回 scoped token）。
        if (!_options.Enforce || !RequiresWorkerAuth(context))
        {
            await _next(context);
            return;
        }

        var workerType = context.Request.Headers[WorkerIdentityHeaders.WorkerType].FirstOrDefault() ?? string.Empty;
        var keyId = context.Request.Headers[WorkerIdentityHeaders.KeyId].FirstOrDefault() ?? string.Empty;
        var timestampRaw = context.Request.Headers[WorkerIdentityHeaders.Timestamp].FirstOrDefault() ?? string.Empty;
        var nonce = context.Request.Headers[WorkerIdentityHeaders.Nonce].FirstOrDefault() ?? string.Empty;
        var signature = context.Request.Headers[WorkerIdentityHeaders.Signature].FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workerType) ||
            string.IsNullOrWhiteSpace(keyId) ||
            string.IsNullOrWhiteSpace(timestampRaw) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(signature))
        {
            await WriteAuthError(context, 401, "Missing worker authentication headers.");
            return;
        }

        if (!DateTimeOffset.TryParse(timestampRaw, out var timestamp))
        {
            await WriteAuthError(context, 401, "Invalid worker authentication timestamp.");
            return;
        }

        // 驗簽內容維持原樣：method、不含 query 的原始 path、body；不可改用正規化後的路徑。
        var body = context.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? string.Empty;
        var decision = _authService.ValidateHttpRequest(new WorkerHttpAuthRequest
        {
            WorkerType = workerType,
            KeyId = keyId,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? string.Empty,
            Body = body,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        });

        if (!decision.IsAuthorized)
        {
            await WriteAuthError(context, decision.StatusCode, decision.Reason);
            return;
        }

        context.Items[WorkerTypeItemKey] = workerType;
        context.Items[WorkerKeyIdItemKey] = keyId;
        await _next(context);
    }

    /// <summary>
    /// 以已匹配的端點判斷是否為 worker 路由：端點標示 WorkerSignature 政策，
    /// 或端點的路由樣板列在 WorkerAuth:HttpRoutes（以路由樣板比對，而非請求路徑字串）。
    /// </summary>
    private bool RequiresWorkerAuth(HttpContext context)
    {
        if (BrokerAuthPolicyResolver.Resolve(context) == BrokerAuthPolicy.WorkerSignature)
        {
            return true;
        }

        var routePattern = BrokerAuthPolicyResolver.GetRoutePattern(context);
        if (string.IsNullOrEmpty(routePattern))
        {
            return false;
        }

        return _options.HttpRoutes
            .SelectMany(rule => rule.Paths)
            .Any(allowed => string.Equals(
                BrokerAuthPolicyResolver.NormalizeRoute(allowed),
                routePattern,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteAuthError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponseHelper.Error(message, statusCode));
    }
}

public static class WorkerIdentityAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseWorkerIdentityAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<WorkerIdentityAuthMiddleware>();
    }
}
