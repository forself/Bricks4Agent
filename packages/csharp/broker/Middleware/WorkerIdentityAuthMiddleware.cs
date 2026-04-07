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
        if (!_options.Enforce || !RequiresWorkerAuth(context.Request.Path.Value ?? string.Empty))
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

    private bool RequiresWorkerAuth(string path)
    {
        return _options.HttpRoutes
            .SelectMany(rule => rule.Paths)
            .Any(allowed => string.Equals(allowed, path, StringComparison.OrdinalIgnoreCase));
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
