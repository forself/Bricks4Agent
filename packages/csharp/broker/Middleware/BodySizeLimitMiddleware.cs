using Broker.Helpers;

namespace Broker.Middleware;

/// <summary>
/// H-10 修復：請求 body 大小限制中間件
///
/// 防止 DoS 攻擊：拒絕超過設定上限的 request body
/// 在 EncryptionMiddleware 之前執行（管線第 0 層）
///
/// 配置：
/// - Broker:MaxRequestBodyBytes（預設 1MB）
/// - 排除 /api/v1/health（無 body）
/// </summary>
public class BodySizeLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly long _maxBodyBytes;
    private readonly ILogger<BodySizeLimitMiddleware> _logger;

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/health"
    };

    public BodySizeLimitMiddleware(RequestDelegate next, long maxBodyBytes, ILogger<BodySizeLimitMiddleware> logger)
    {
        _next = next;
        _maxBodyBytes = maxBodyBytes;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // 排除健康檢查和非 POST
        if (ExcludedPaths.Contains(path) || context.Request.Method != "POST")
        {
            await _next(context);
            return;
        }

        // 檢查 Content-Length header（快速拒絕）
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > _maxBodyBytes)
        {
            _logger.LogWarning(
                "Request body too large: Content-Length={ContentLength} > max={Max} from {Remote}",
                context.Request.ContentLength.Value, _maxBodyBytes,
                context.Connection.RemoteIpAddress);

            // M-10 修復：統一使用 ApiResponseHelper 格式
            context.Response.StatusCode = 413; // Payload Too Large
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponseHelper.Error($"Request body exceeds maximum size of {_maxBodyBytes} bytes.", 413));
            return;
        }

        // 設置 Kestrel 的 MaxRequestBodySize（即使 Content-Length 缺失，讀取時也會強制限制）
        var bodyFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyFeature != null && !bodyFeature.IsReadOnly)
        {
            bodyFeature.MaxRequestBodySize = _maxBodyBytes;
        }

        await _next(context);
    }
}

/// <summary>BodySizeLimitMiddleware 擴展方法</summary>
public static class BodySizeLimitMiddlewareExtensions
{
    /// <summary>
    /// 啟用請求 body 大小限制
    /// </summary>
    /// <param name="builder">應用程式建構器</param>
    /// <param name="maxBodyBytes">最大 body 大小（bytes），預設 1MB</param>
    public static IApplicationBuilder UseBodySizeLimit(
        this IApplicationBuilder builder, long maxBodyBytes = 1_048_576)
    {
        return builder.UseMiddleware<BodySizeLimitMiddleware>(maxBodyBytes);
    }
}
