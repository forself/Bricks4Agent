using BrokerCore.Contracts;

namespace BrokerCore.Services;

/// <summary>
/// 執行轉發介面 —— Control/Execution Plane 邊界契約
///
/// Broker（Control Plane）只做裁決與轉交，絕不自己執行工具動作。
/// 所有已批准的請求透過此介面轉交給 Execution Plane。
///
/// Phase 1: InProcessDispatcher（僅低風險讀取，inline 處理）
/// Phase 2: HttpDispatcher / MessageQueueDispatcher（跨進程 / 跨節點）
/// </summary>
public interface IExecutionDispatcher
{
    /// <summary>
    /// 將已批准的請求轉交給執行層
    /// </summary>
    /// <param name="approvedRequest">已通過政策裁決的請求</param>
    /// <returns>執行結果</returns>
    Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest);
}
