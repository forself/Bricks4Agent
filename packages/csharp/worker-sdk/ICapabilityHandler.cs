namespace WorkerSdk;

/// <summary>
/// Worker 能力處理器介面
///
/// 每個 handler 處理一種 capability（如 file.read、file.list）。
/// Worker 進程透過 WorkerHost.RegisterHandler() 註冊多個 handler。
/// </summary>
public interface ICapabilityHandler
{
    /// <summary>此 handler 支援的能力 ID</summary>
    string CapabilityId { get; }

    /// <summary>
    /// 執行能力
    /// </summary>
    /// <param name="requestId">請求 ID</param>
    /// <param name="route">工具路由（如 "read_file"）</param>
    /// <param name="payload">請求 payload（JSON）</param>
    /// <param name="scope">操作範圍（JSON）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>執行結果：(success, resultPayload?, error?)</returns>
    Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct);
}
