using System.Text.Json;
using CacheProtocol;
using BrokerCore.Contracts;
using BrokerCore.Services;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Dispatch;

/// <summary>
/// 功能池分派器 — IExecutionDispatcher 實作
///
/// 職責：
/// 1. 接收 ApprovedRequest → 按 CapabilityId 查詢 WorkerRegistry
/// 2. Round-Robin 選取可用 Worker → 發送 WORKER_EXECUTE
/// 3. 等待 WORKER_RESULT（含超時）
/// 4. 失敗重試（嘗試下一個 Worker 實例）
/// 5. 所有 Worker 不可用 → 返回失敗
/// </summary>
public class PoolDispatcher : IExecutionDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IWorkerRegistry _registry;
    private readonly PoolConfig _config;
    private readonly ILogger<PoolDispatcher> _logger;

    public PoolDispatcher(
        IWorkerRegistry registry,
        PoolConfig config,
        ILogger<PoolDispatcher> logger)
    {
        _registry = registry;
        _config = config;
        _logger = logger;
    }

    /// <summary>是否有指定能力的可用 Worker</summary>
    public bool HasAvailableWorker(string capabilityId)
    {
        return _registry.HasAvailableWorker(capabilityId);
    }

    public async Task<ExecutionResult> DispatchAsync(ApprovedRequest request)
    {
        for (int attempt = 0; attempt <= _config.MaxRetries; attempt++)
        {
            var conn = _registry.GetAvailableWorker(request.CapabilityId);
            if (conn == null)
            {
                _logger.LogDebug(
                    "No available worker for capability '{Cap}' (attempt {A})",
                    request.CapabilityId, attempt);
                return ExecutionResult.Fail(request.RequestId,
                    $"No available worker for capability '{request.CapabilityId}'");
            }

            try
            {
                // 增加活躍任務計數
                _registry.IncrementActiveTask(conn.WorkerId);

                // 編碼 WORKER_EXECUTE frame
                var executeCmd = new WorkerExecuteCommand
                {
                    RequestId = request.RequestId,
                    CapabilityId = request.CapabilityId,
                    Route = request.Route,
                    Payload = request.Payload,
                    Scope = request.Scope,
                    TraceId = request.TraceId
                };

                var payload = JsonSerializer.SerializeToUtf8Bytes(executeCmd, JsonOptions);
                var frame = FrameCodec.Encode(OpCodes.WORKER_EXECUTE, payload);

                _logger.LogDebug(
                    "Dispatching to worker {W}: requestId={R} capability={C} route={Route}",
                    conn.WorkerId, request.RequestId, request.CapabilityId, request.Route);

                // 發送並等待結果（含 timeout）
                var (respOpCode, respPayload) = await conn.SendAndWaitAsync(
                    frame, request.RequestId, _config.DispatchTimeout);

                // 成功完成 → 歸還活躍任務計數
                _registry.DecrementActiveTask(conn.WorkerId);

                return ParseExecutionResult(request.RequestId, respOpCode, respPayload);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(
                    "Worker dispatch timeout for request {R}: {Error}",
                    request.RequestId, ex.Message);

                // 超時 → 減少活躍任務計數（可能 Worker 仍在執行，但我們放棄等待）
                _registry.DecrementActiveTask(conn.WorkerId);

                if (attempt < _config.MaxRetries)
                    continue;
            }
            catch (Exception ex) when (attempt < _config.MaxRetries)
            {
                _logger.LogWarning(ex,
                    "Worker dispatch failed for request {R} (attempt {A}), retrying",
                    request.RequestId, attempt + 1);

                // 異常重試 → 歸還活躍任務計數
                _registry.DecrementActiveTask(conn.WorkerId);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Worker dispatch failed for request {R}, no more retries",
                    request.RequestId);

                // 最終失敗 → 歸還活躍任務計數
                _registry.DecrementActiveTask(conn.WorkerId);

                return ExecutionResult.Fail(request.RequestId,
                    $"Worker dispatch failed: {ex.Message}");
            }
        }

        return ExecutionResult.Fail(request.RequestId, "All worker dispatch attempts failed");
    }

    /// <summary>解析 WORKER_RESULT payload → ExecutionResult</summary>
    private ExecutionResult ParseExecutionResult(
        string requestId, byte opCode, ReadOnlyMemory<byte> payload)
    {
        if (opCode != OpCodes.WORKER_RESULT)
        {
            return ExecutionResult.Fail(requestId,
                $"Unexpected response OpCode: 0x{opCode:X2}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<WorkerResultMessage>(
                payload.Span, JsonOptions);

            if (result == null)
                return ExecutionResult.Fail(requestId, "Failed to deserialize worker result");

            if (result.Success)
            {
                return ExecutionResult.Ok(requestId, result.ResultPayload ?? "{}");
            }
            else
            {
                return ExecutionResult.Fail(requestId, result.Error ?? "Worker execution failed");
            }
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(requestId,
                $"Failed to parse worker result: {ex.Message}");
        }
    }
}

// ── 協議訊息 DTO ──

/// <summary>WORKER_EXECUTE payload（Broker → Worker）</summary>
public class WorkerExecuteCommand
{
    public string RequestId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Scope { get; set; } = "{}";
    public string TraceId { get; set; } = string.Empty;
}

/// <summary>WORKER_RESULT payload（Worker → Broker）</summary>
public class WorkerResultMessage
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ResultPayload { get; set; }
    public string? Error { get; set; }
}
