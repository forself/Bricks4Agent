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

    /// <summary>
    /// 執行 worker 已收到請求並回覆（成功或拒絕）。這種結果是最終結果：
    /// 降級分派器不得把 worker 的拒絕改交其他執行者重試。
    /// </summary>
    public bool AnsweredByWorker { get; set; }

    /// <summary>
    /// 分派時一開始就沒有可用的 worker，請求沒有送到任何 worker。
    /// 已送出後的逾時或傳輸失敗不算（worker 可能仍在處理），呼叫端不得把那種結果當成「忙碌、可以再送一次」。
    /// </summary>
    public bool NoWorkerAvailable { get; set; }

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

    /// <summary>沒有可用的 worker、請求沒有送出時的失敗（<see cref="NoWorkerAvailable"/> 為 true）。</summary>
    public static ExecutionResult NoWorker(string requestId, string errorMessage)
        => new()
        {
            RequestId = requestId,
            Success = false,
            ErrorMessage = errorMessage,
            NoWorkerAvailable = true
        };
}
