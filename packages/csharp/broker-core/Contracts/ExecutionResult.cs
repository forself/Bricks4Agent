namespace BrokerCore.Contracts;

/// <summary>
/// 執行結果 —— 執行層 → broker 的回報
/// </summary>
public class ExecutionResult
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }

    /// <summary>結果 payload（JSON）</summary>
    public string? ResultPayload { get; set; }

    /// <summary>錯誤訊息（失敗時）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>證據引用（稽核用）</summary>
    public string? EvidenceRef { get; set; }

    public static ExecutionResult Ok(string requestId, string resultPayload, string? evidenceRef = null)
        => new()
        {
            RequestId = requestId,
            Success = true,
            ResultPayload = resultPayload,
            EvidenceRef = evidenceRef
        };

    public static ExecutionResult Fail(string requestId, string errorMessage)
        => new()
        {
            RequestId = requestId,
            Success = false,
            ErrorMessage = errorMessage
        };
}
