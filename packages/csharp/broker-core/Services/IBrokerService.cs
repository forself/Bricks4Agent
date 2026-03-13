using BrokerCore.Contracts;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Broker 門面服務 —— 協調所有子服務
/// 核心 PEP 邏輯的唯一入口
/// </summary>
public interface IBrokerService
{
    /// <summary>建立任務</summary>
    BrokerTask CreateTask(string submittedBy, string taskType, string scopeDescriptor);

    /// <summary>查詢任務</summary>
    BrokerTask? GetTask(string taskId);

    /// <summary>取消任務（級聯撤權）</summary>
    bool CancelTask(string taskId, string cancelledBy, string reason);

    /// <summary>
    /// 提交執行請求（核心 PEP 16 步流程）
    /// </summary>
    ExecutionRequest SubmitExecutionRequest(
        string principalId,
        string taskId,
        string sessionId,
        string capabilityId,
        string intent,
        string requestPayload,
        string idempotencyKey,
        string traceId);

    /// <summary>查詢執行請求</summary>
    ExecutionRequest? GetExecutionRequest(string requestId);
}
