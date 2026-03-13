using System.Diagnostics;

namespace Broker.Helpers;

/// <summary>
/// 統一 API 回應格式（從 BaseController 模式萃取，無 MVC 依賴）
/// 所有回應都經 EncryptionMiddleware 加密後送出
/// </summary>
public static class ApiResponseHelper
{
    public static ApiResponse<T> Success<T>(T? data = default, string message = "ok")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            TraceId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
        };
    }

    public static ApiResponse<object> Error(string message, int code = 400)
    {
        return new ApiResponse<object>
        {
            Success = false,
            Message = message,
            ErrorCode = code,
            TraceId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
        };
    }
}

/// <summary>統一 API 回應結構</summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public int? ErrorCode { get; set; }
    public string TraceId { get; set; } = string.Empty;
}
