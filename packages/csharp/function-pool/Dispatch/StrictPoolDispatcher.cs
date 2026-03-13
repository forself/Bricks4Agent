using BrokerCore.Contracts;
using BrokerCore.Services;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Dispatch;

/// <summary>
/// 嚴格模式分派器 — 生產環境用
///
/// 與 FallbackDispatcher 不同：
/// - 無 Worker → 直接返回 ExecutionResult.Fail()（503 語義）
/// - 絕不降級到 InProcessDispatcher
/// - 確保控制面/執行面完全分離
///
/// 配置：FunctionPool:StrictMode=true
/// </summary>
public class StrictPoolDispatcher : IExecutionDispatcher
{
    private readonly PoolDispatcher _pool;
    private readonly ILogger<StrictPoolDispatcher> _logger;

    public StrictPoolDispatcher(
        PoolDispatcher pool,
        ILogger<StrictPoolDispatcher> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    public async Task<ExecutionResult> DispatchAsync(ApprovedRequest request)
    {
        if (!_pool.HasAvailableWorker(request.CapabilityId))
        {
            _logger.LogWarning(
                "StrictMode: No worker available for capability '{Cap}'. " +
                "Request {R} rejected (503 semantics, no fallback).",
                request.CapabilityId, request.RequestId);

            return ExecutionResult.Fail(request.RequestId,
                $"[StrictMode] No available worker for capability '{request.CapabilityId}'. " +
                "Execution plane unavailable — request cannot be processed.");
        }

        var result = await _pool.DispatchAsync(request);

        if (!result.Success)
        {
            _logger.LogWarning(
                "StrictMode: Pool dispatch failed for request {R}: {Error}. No fallback.",
                request.RequestId, result.ErrorMessage);
        }

        return result;
    }
}
