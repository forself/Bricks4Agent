namespace BrokerCore.Contracts;

/// <summary>
/// 已批准的執行請求 —— broker 的輸出，交給執行層
/// Broker 永遠不知道工具怎麼執行，只知道結果
/// </summary>
public class ApprovedRequest
{
    /// <summary>執行請求 ID（用於結果回報對應）</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>能力 ID（對應 JS 工具名）</summary>
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>工具路由（read_file, list_directory 等）</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>請求 payload（JSON）</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>操作範圍（路徑限制等）</summary>
    public string Scope { get; set; } = "{}";

    /// <summary>追蹤 ID</summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>主體 ID</summary>
    public string PrincipalId { get; set; } = string.Empty;

    /// <summary>任務 ID</summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>Session ID</summary>
    public string SessionId { get; set; } = string.Empty;
}
