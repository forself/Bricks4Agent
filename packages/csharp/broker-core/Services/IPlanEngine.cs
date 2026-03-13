using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 因果工作流引擎 —— DAG 排程 + 逐節點裁決執行
///
/// Phase 4 為循序執行（同級不並行），Phase 5 再支援並行節點。
/// 核心流程：
/// 1. ValidateDag → 確認無環 + 計算拓撲序
/// 2. Plan.State = Running
/// 3. 拓撲序迴圈：GetReadyNodes → DataFlow 注入 → PEP 裁決 → 執行 → 寫出結果
/// 4. 全節點完成 → Plan.State = Completed
/// </summary>
public interface IPlanEngine
{
    /// <summary>
    /// 提交並執行計畫（非同步循序）
    /// H-3 修復：完整 async 鏈，消除 sync-over-async
    /// M-8 修復：CancellationToken 傳播，支援請求取消
    /// </summary>
    /// <param name="planId">計畫 ID</param>
    /// <param name="principalId">提交者 principal_id</param>
    /// <param name="sessionId">Session ID（PEP 需要）</param>
    /// <param name="traceId">追蹤 ID</param>
    /// <param name="cancellationToken">取消令牌（預設 = 不取消）</param>
    /// <returns>執行後的計畫狀態</returns>
    Task<Plan> SubmitAndExecuteAsync(string planId, string principalId,
                                      string sessionId, string traceId,
                                      CancellationToken cancellationToken = default);
}
