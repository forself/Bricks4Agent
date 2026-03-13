using System.Diagnostics;
using BrokerCore.Services;

namespace Broker.Middleware;

/// <summary>
/// 稽核中介軟體 —— 管線第三層（在 Auth 之後）
///
/// 職責：
/// - 每個 API 呼叫記錄一筆 AuditEvent
/// - 自動從 HttpContext.Items 讀取已驗證的 principal/task/session
/// - 記錄請求路徑、狀態碼
/// - 使用 trace_id 串聯整個請求生命週期
///
/// 排除路徑：
/// - /api/v1/health
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAuditService _auditService;
    private readonly ILogger<AuditMiddleware> _logger;

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/health"
    };

    public AuditMiddleware(
        RequestDelegate next,
        IAuditService auditService,
        ILogger<AuditMiddleware> logger)
    {
        _next = next;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (ExcludedPaths.Contains(path))
        {
            await _next(context);
            return;
        }

        // 確保有 trace_id
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        context.Items["audit_trace_id"] = traceId;

        // 從 BrokerAuthMiddleware 注入的 claims 取得主體資訊
        var principalId = context.Items.TryGetValue(BrokerAuthMiddleware.PrincipalIdKey, out var pid) ? pid as string : null;
        var taskId = context.Items.TryGetValue(BrokerAuthMiddleware.TaskIdKey, out var tid) ? tid as string : null;
        var sessionId = context.Items.TryGetValue(BrokerAuthMiddleware.SessionIdKey, out var sid) ? sid as string : null;

        // 記錄請求
        try
        {
            _auditService.RecordEvent(
                traceId: traceId,
                eventType: "API_REQUEST",
                principalId: principalId,
                taskId: taskId,
                sessionId: sessionId,
                resourceRef: path,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    method = context.Request.Method,
                    path,
                    content_length = context.Request.ContentLength
                }));
        }
        catch (Exception ex)
        {
            // 稽核失敗不應阻斷業務流程
            _logger.LogError(ex, "Failed to record audit request event");
        }

        // 執行後續管線
        await _next(context);

        // 記錄回應
        try
        {
            _auditService.RecordEvent(
                traceId: traceId,
                eventType: "API_RESPONSE",
                principalId: principalId,
                taskId: taskId,
                sessionId: sessionId,
                resourceRef: path,
                details: System.Text.Json.JsonSerializer.Serialize(new
                {
                    status_code = context.Response.StatusCode,
                    path
                }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit response event");
        }
    }
}

/// <summary>
/// AuditMiddleware 擴展方法
/// </summary>
public static class AuditMiddlewareExtensions
{
    public static IApplicationBuilder UseBrokerAudit(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuditMiddleware>();
    }
}
