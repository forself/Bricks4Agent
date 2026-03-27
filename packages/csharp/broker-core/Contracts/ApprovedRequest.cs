using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BrokerCore.Contracts;

/// <summary>
/// 已批准的執行請求 —— broker 的輸出，交給執行層
/// Broker 永遠不知道工具怎麼執行，只知道結果
///
/// 正式路徑：僅由 BrokerService (Step 12) 建構。
/// 其他地方建構會觸發 PEP bypass 偵測 warning。
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

    /// <summary>
    /// PEP bypass 偵測：記錄建構來源。
    /// 僅在 DEBUG build 中啟用以避免 production 效能影響。
    /// </summary>
    [ConditionalAttribute("DEBUG")]
    public static void WarnIfBypass(
        [CallerFilePath] string callerFile = "",
        [CallerMemberName] string callerMember = "")
    {
        // BrokerService.SubmitExecutionRequestAsync 是唯一正式建構點
        if (!callerFile.EndsWith("BrokerService.cs", StringComparison.OrdinalIgnoreCase))
        {
            Trace.TraceWarning(
                $"[PEP-BYPASS] ApprovedRequest constructed outside BrokerService: {callerFile}:{callerMember}. " +
                "This bypasses the 16-step PEP pipeline. See PipelineExceptions registry.");
        }
    }
}
