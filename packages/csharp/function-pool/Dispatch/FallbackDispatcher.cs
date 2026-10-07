using BrokerCore.Contracts;
using BrokerCore.Services;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Dispatch;

/// <summary>
/// 降級分派器 — PoolDispatcher + InProcessDispatcher 組合
///
/// 策略：
/// 1. 有可用 Worker → PoolDispatcher
/// 2. 沒有可用 Worker，或分派因傳輸、逾時失敗，且 InProcess 可處理 → InProcessDispatcher（降級）
/// 3. Worker 已回覆的結果（含拒絕）是最終結果，不降級
/// 4. 兩者都不可用 → 返回失敗
/// </summary>
public class FallbackDispatcher : IExecutionDispatcher
{
    private readonly PoolDispatcher _pool;
    private readonly IExecutionDispatcher _fallback;
    private readonly Func<string, bool>? _canFallbackHandle;
    private readonly ILogger<FallbackDispatcher> _logger;

    /// <summary>
    /// 建構降級分派器
    /// </summary>
    /// <param name="pool">功能池分派器</param>
    /// <param name="fallback">降級分派器（InProcessDispatcher）</param>
    /// <param name="canFallbackHandle">判斷降級分派器是否支援指定 route</param>
    /// <param name="logger">日誌</param>
    public FallbackDispatcher(
        PoolDispatcher pool,
        IExecutionDispatcher fallback,
        Func<string, bool>? canFallbackHandle,
        ILogger<FallbackDispatcher> logger)
    {
        _pool = pool;
        _fallback = fallback;
        _canFallbackHandle = canFallbackHandle;
        _logger = logger;
    }

    public async Task<ExecutionResult> DispatchAsync(ApprovedRequest request)
    {
        // 請求是否一開始就沒有送到任何 worker（逾時或傳輸失敗時請求已送出，不算）。
        var notSentToAWorker = true;

        // 嘗試功能池
        if (_pool.HasAvailableWorker(request.CapabilityId))
        {
            var result = await _pool.DispatchAsync(request);
            notSentToAWorker = result.NoWorkerAvailable;

            // 成功 → 直接返回
            if (result.Success)
                return result;

            // worker 已回覆的拒絕是最終結果：不改交 InProcess 重試。
            if (result.AnsweredByWorker)
            {
                _logger.LogInformation(
                    "Worker refused {Route}; not falling back. Error: {Error}",
                    request.Route, result.ErrorMessage);
                return result;
            }

            // 傳輸或逾時失敗 → 嘗試降級
            _logger.LogWarning(
                "Pool dispatch failed for {Route}: {Error}. Checking fallback...",
                request.Route, result.ErrorMessage);
        }
        else
        {
            _logger.LogDebug(
                "No worker available for capability '{Cap}', checking fallback...",
                request.CapabilityId);
        }

        // 降級到 InProcessDispatcher
        if (_canFallbackHandle != null && _canFallbackHandle(request.Route))
        {
            _logger.LogInformation(
                "Falling back to InProcessDispatcher for route={Route}",
                request.Route);
            return await _fallback.DispatchAsync(request);
        }

        // 兩者都不可用
        var message = $"No available worker for capability '{request.CapabilityId}' " +
                      $"and route '{request.Route}' is not supported by fallback dispatcher.";
        return notSentToAWorker
            ? ExecutionResult.NoWorker(request.RequestId, message)
            : ExecutionResult.Fail(request.RequestId, message);
    }
}
