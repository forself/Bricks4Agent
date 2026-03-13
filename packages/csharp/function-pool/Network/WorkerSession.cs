using System.Text.Json;
using CacheProtocol;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Network;

/// <summary>
/// Worker 連線 Session（每個 TCP 連線一個）
///
/// 職責：
/// 1. 處理 WORKER_REGISTER → 註冊到 WorkerRegistry
/// 2. 處理 PING → 回覆 PONG
/// 3. 處理 WORKER_RESULT → 完成 PoolDispatcher 的等待
/// 4. 處理 WORKER_DEREGISTER → 從 Registry 移除
/// 5. 處理 WORKER_STATUS_ACK → 更新 Worker 負載資訊
/// </summary>
public class WorkerSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkerConnection _connection;
    private readonly IWorkerRegistry _registry;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    private string? _workerId;
    private volatile bool _disposed;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
    public bool IsActive => !_disposed && _connection.IsConnected;
    public DateTime LastActivityAt => _connection.LastActivity;

    public WorkerSession(
        WorkerConnection connection,
        IWorkerRegistry registry,
        ILogger logger)
    {
        _connection = connection;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>處理此 session 的所有訊息（主迴圈）</summary>
    public async Task ProcessAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);

        _logger.LogDebug("Worker session {Id} started from {Ep}",
            Id, _connection.RemoteEndpoint);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                var frame = await _connection.ReceiveFrameAsync(linked.Token);
                if (frame == null)
                {
                    _logger.LogInformation("Worker session {Id} connection closed", Id);
                    break;
                }

                var (opCode, payload) = frame.Value;
                await HandleFrameAsync(opCode, payload, linked.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker session {Id} error", Id);
        }
        finally
        {
            // 清理：從 registry 移除
            if (_workerId != null)
            {
                _registry.Deregister(_workerId);
            }
        }
    }

    /// <summary>處理單一 frame</summary>
    private async Task HandleFrameAsync(byte opCode, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        switch (opCode)
        {
            case OpCodes.WORKER_REGISTER:
                await HandleRegisterAsync(payload, ct);
                break;

            case OpCodes.PING:
                await HandlePingAsync(ct);
                break;

            case OpCodes.WORKER_RESULT:
                HandleWorkerResult(payload);
                break;

            case OpCodes.WORKER_DEREGISTER:
                HandleDeregister(payload);
                break;

            case OpCodes.WORKER_STATUS_ACK:
                HandleStatusAck(payload);
                break;

            default:
                _logger.LogWarning("Worker session {Id}: unknown OpCode 0x{Op:X2}",
                    Id, opCode);
                break;
        }
    }

    /// <summary>處理 WORKER_REGISTER</summary>
    private async Task HandleRegisterAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        try
        {
            var registerMsg = JsonSerializer.Deserialize<WorkerRegisterMessage>(
                payload.Span, JsonOptions);

            if (registerMsg == null || string.IsNullOrEmpty(registerMsg.WorkerId))
            {
                await SendRegisterAckAsync(false, "", "Invalid register message", ct);
                return;
            }

            _workerId = registerMsg.WorkerId;
            _connection.WorkerId = _workerId;

            var workerInfo = new WorkerInfo
            {
                WorkerId = registerMsg.WorkerId,
                Capabilities = registerMsg.Capabilities ?? new List<string>(),
                MaxConcurrent = registerMsg.MaxConcurrent > 0 ? registerMsg.MaxConcurrent : 4,
                RemoteEndpoint = _connection.RemoteEndpoint
            };

            var success = _registry.Register(workerInfo, _connection);

            await SendRegisterAckAsync(success, registerMsg.WorkerId,
                success ? null : "Registration failed", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker session {Id}: register failed", Id);
            await SendRegisterAckAsync(false, "", $"Register error: {ex.Message}", ct);
        }
    }

    /// <summary>發送 WORKER_REGISTER_ACK</summary>
    private async Task SendRegisterAckAsync(bool ok, string workerId, string? error, CancellationToken ct)
    {
        var ack = new { ok, worker_id = workerId, error };
        var payload = JsonSerializer.SerializeToUtf8Bytes(ack, JsonOptions);
        var frame = FrameCodec.Encode(OpCodes.WORKER_REGISTER_ACK, payload);
        await _connection.SendFrameAsync(frame, ct);
    }

    /// <summary>處理 PING → 回覆 PONG + 更新心跳</summary>
    private async Task HandlePingAsync(CancellationToken ct)
    {
        if (_workerId != null)
        {
            _registry.UpdateHeartbeat(_workerId);
        }

        var pongFrame = FrameCodec.EncodeEmpty(OpCodes.PONG);
        await _connection.SendFrameAsync(pongFrame, ct);
    }

    /// <summary>處理 WORKER_RESULT → 完成 PoolDispatcher 的等待</summary>
    private void HandleWorkerResult(ReadOnlyMemory<byte> payload)
    {
        try
        {
            // 解析 requestId 以匹配等待中的請求
            var doc = JsonDocument.Parse(payload);
            var requestId = doc.RootElement.TryGetProperty("request_id", out var rid)
                ? rid.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(requestId))
            {
                _logger.LogWarning("Worker session {Id}: WORKER_RESULT missing request_id", Id);
                return;
            }

            // 減少活躍任務計數
            if (_workerId != null)
                _registry.DecrementActiveTask(_workerId);

            // 完成等待
            _connection.CompleteRequest(requestId, OpCodes.WORKER_RESULT, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker session {Id}: error handling WORKER_RESULT", Id);
        }
    }

    /// <summary>處理 WORKER_DEREGISTER</summary>
    private void HandleDeregister(ReadOnlyMemory<byte> payload)
    {
        _logger.LogInformation("Worker session {Id}: received DEREGISTER", Id);

        if (_workerId != null)
        {
            _registry.Deregister(_workerId);
            _workerId = null;
        }

        Stop();
    }

    /// <summary>處理 WORKER_STATUS_ACK</summary>
    private void HandleStatusAck(ReadOnlyMemory<byte> payload)
    {
        // 可用於更新 Worker 負載資訊（未來擴展）
        if (_workerId != null)
        {
            _registry.UpdateHeartbeat(_workerId);
        }
    }

    /// <summary>停止此 session</summary>
    public void Stop()
    {
        _cts.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();

        if (_workerId != null)
        {
            _registry.Deregister(_workerId);
        }

        await _connection.DisposeAsync();
    }
}

// ── 協議訊息 DTO ──

internal class WorkerRegisterMessage
{
    public string WorkerId { get; set; } = string.Empty;
    public List<string>? Capabilities { get; set; }
    public int MaxConcurrent { get; set; } = 4;
    public Dictionary<string, string>? Metadata { get; set; }
}
