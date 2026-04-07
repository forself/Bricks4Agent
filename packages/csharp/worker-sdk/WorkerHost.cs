using System.Net.Sockets;
using System.Text.Json;
using BrokerCore.Services;
using CacheProtocol;
using Microsoft.Extensions.Logging;

namespace WorkerSdk;

/// <summary>
/// Worker 進程主框架
///
/// 職責：
/// 1. TCP 連入 Broker PoolListener
/// 2. 發送 WORKER_REGISTER
/// 3. 啟動心跳 Timer
/// 4. 接收分派迴圈（WORKER_EXECUTE → handler → WORKER_RESULT）
/// 5. 自動重連（可選）
/// </summary>
public class WorkerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkerHostOptions _options;
    private readonly ILogger<WorkerHost> _logger;
    private readonly Dictionary<string, ICapabilityHandler> _handlers = new();
    private readonly WorkerIdentityAuthService _workerIdentityAuthService =
        new(new WorkerIdentityAuthOptions(), new WorkerAuthNonceStore());

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private Timer? _heartbeatTimer;
    private volatile bool _running;

    public WorkerHost(WorkerHostOptions options, ILogger<WorkerHost> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>註冊能力處理器</summary>
    public void RegisterHandler(ICapabilityHandler handler)
    {
        _handlers[handler.CapabilityId] = handler;
        _logger.LogInformation("Registered handler: {Cap}", handler.CapabilityId);
    }

    /// <summary>啟動 Worker（阻塞直到取消）</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        if (_handlers.Count == 0)
        {
            _logger.LogError("No handlers registered. Exiting.");
            return;
        }

        _running = true;

        while (_running && !ct.IsCancellationRequested)
        {
            try
            {
                // 1. 連線
                await ConnectAsync(ct);

                // 2. 註冊
                await RegisterAsync(ct);

                // 3. 啟動心跳
                StartHeartbeat();

                _logger.LogInformation(
                    "Worker {WorkerId} connected and registered. Capabilities: [{Caps}]",
                    _options.WorkerId, string.Join(", ", _handlers.Keys));

                // 4. 接收分派迴圈
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Worker connection error. Reconnecting...");
            }
            finally
            {
                StopHeartbeat();
                Disconnect();
            }

            // 重連等待
            if (_running && _options.AutoReconnect && !ct.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Reconnecting in {Interval}s...", _options.ReconnectIntervalSeconds);
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(_options.ReconnectIntervalSeconds), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        _logger.LogInformation("Worker {WorkerId} stopped.", _options.WorkerId);
    }

    /// <summary>TCP 連線到 Broker</summary>
    private async Task ConnectAsync(CancellationToken ct)
    {
        _tcpClient = new TcpClient { NoDelay = true };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

        _logger.LogDebug("Connecting to broker {Host}:{Port}...",
            _options.BrokerHost, _options.BrokerPort);

        await _tcpClient.ConnectAsync(
            _options.BrokerHost, _options.BrokerPort, timeoutCts.Token);

        _stream = _tcpClient.GetStream();
    }

    /// <summary>發送 WORKER_REGISTER 並等待 ACK</summary>
    private async Task RegisterAsync(CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var capabilities = _handlers.Keys.ToList();
        var maxConcurrent = _options.MaxConcurrent;
        var signature = string.IsNullOrWhiteSpace(_options.WorkerAuthKeyId) ||
            string.IsNullOrWhiteSpace(_options.WorkerAuthSharedSecret)
            ? string.Empty
            : _workerIdentityAuthService.SignWorkerRegister(
                _options.WorkerType,
                _options.WorkerAuthKeyId,
                _options.WorkerAuthSharedSecret,
                _options.WorkerId,
                capabilities,
                maxConcurrent,
                timestamp,
                nonce);

        var registerMsg = new
        {
            worker_id = _options.WorkerId,
            worker_type = _options.WorkerType,
            capabilities,
            max_concurrent = maxConcurrent,
            key_id = _options.WorkerAuthKeyId,
            timestamp = timestamp.ToString("O"),
            nonce,
            signature
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(registerMsg, JsonOptions);
        var frame = FrameCodec.Encode(OpCodes.WORKER_REGISTER, payload);

        await _stream!.WriteAsync(frame, ct);
        await _stream.FlushAsync(ct);

        // 等待 ACK
        var (ackOpCode, ackPayload) = await ReceiveFrameAsync(ct);

        if (ackOpCode != OpCodes.WORKER_REGISTER_ACK)
        {
            throw new InvalidOperationException(
                $"Expected WORKER_REGISTER_ACK, got 0x{ackOpCode:X2}");
        }

        var ack = JsonSerializer.Deserialize<RegisterAckMessage>(ackPayload.Span, JsonOptions);
        if (ack == null || !ack.Ok)
        {
            throw new InvalidOperationException(
                $"Worker registration failed: {ack?.Error ?? "unknown error"}");
        }

        _logger.LogDebug("Registration ACK received for {WorkerId}", _options.WorkerId);
    }

    /// <summary>接收分派迴圈</summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (opCode, payload) = await ReceiveFrameAsync(ct);

            switch (opCode)
            {
                case OpCodes.WORKER_EXECUTE:
                    // 在背景處理（不阻塞接收迴圈）
                    _ = HandleExecuteAsync(payload, ct);
                    break;

                case OpCodes.PONG:
                    // 心跳回應（忽略）
                    break;

                case OpCodes.WORKER_STATUS:
                    await HandleStatusQueryAsync(ct);
                    break;

                default:
                    _logger.LogWarning("Unknown OpCode from broker: 0x{Op:X2}", opCode);
                    break;
            }
        }
    }

    /// <summary>處理 WORKER_EXECUTE</summary>
    private async Task HandleExecuteAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        string requestId = "";
        try
        {
            var cmd = JsonSerializer.Deserialize<ExecuteCommand>(payload.Span, JsonOptions);
            if (cmd == null)
            {
                _logger.LogWarning("Failed to deserialize WORKER_EXECUTE");
                return;
            }

            requestId = cmd.RequestId;

            _logger.LogDebug("Executing: requestId={R} capability={C} route={Route}",
                cmd.RequestId, cmd.CapabilityId, cmd.Route);

            // 找到對應 handler
            if (!_handlers.TryGetValue(cmd.CapabilityId, out var handler))
            {
                await SendResultAsync(cmd.RequestId, false, null,
                    $"No handler for capability '{cmd.CapabilityId}'", ct);
                return;
            }

            // 執行
            var (success, resultPayload, error) = await handler.ExecuteAsync(
                cmd.RequestId, cmd.Route, cmd.Payload, cmd.Scope, ct);

            await SendResultAsync(cmd.RequestId, success, resultPayload, error, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing request {R}", requestId);
            try
            {
                await SendResultAsync(requestId, false, null,
                    $"Worker execution error: {ex.Message}", ct);
            }
            catch { /* best effort */ }
        }
    }

    /// <summary>發送 WORKER_RESULT</summary>
    private async Task SendResultAsync(
        string requestId, bool success, string? resultPayload, string? error,
        CancellationToken ct)
    {
        var result = new
        {
            request_id = requestId,
            success,
            result_payload = resultPayload,
            error
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
        var frame = FrameCodec.Encode(OpCodes.WORKER_RESULT, payload);

        await _stream!.WriteAsync(frame, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>處理 WORKER_STATUS 查詢</summary>
    private async Task HandleStatusQueryAsync(CancellationToken ct)
    {
        var status = new { active_tasks = 0, load_pct = 0 };
        var payload = JsonSerializer.SerializeToUtf8Bytes(status, JsonOptions);
        var frame = FrameCodec.Encode(OpCodes.WORKER_STATUS_ACK, payload);

        await _stream!.WriteAsync(frame, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>接收一個完整 frame</summary>
    private async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)> ReceiveFrameAsync(
        CancellationToken ct)
    {
        var buffer = new byte[4096];
        int filled = 0;

        while (!ct.IsCancellationRequested)
        {
            var bytesRead = await _stream!.ReadAsync(
                buffer.AsMemory(filled, buffer.Length - filled), ct);

            if (bytesRead == 0)
                throw new IOException("Connection closed by broker");

            filled += bytesRead;

            // 同步解析（避免 Span 跨 await）
            var result = TryParseFromBuffer(buffer, filled);
            if (result.HasValue)
                return result.Value;

            // 擴容
            if (filled >= buffer.Length)
            {
                var newBuffer = new byte[buffer.Length * 2];
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, filled);
                buffer = newBuffer;
            }
        }

        throw new OperationCanceledException();
    }

    /// <summary>同步 frame 解析</summary>
    private static (byte OpCode, ReadOnlyMemory<byte> Payload)? TryParseFromBuffer(
        byte[] buffer, int filled)
    {
        if (filled < FrameCodec.HeaderSize)
            return null;

        var span = buffer.AsSpan(0, filled);
        if (FrameCodec.TryParse(span, out var frame))
        {
            var payload = new byte[frame.Payload.Length];
            frame.Payload.Span.CopyTo(payload);
            return (frame.OpCode, payload);
        }
        return null;
    }

    /// <summary>啟動心跳 Timer</summary>
    private void StartHeartbeat()
    {
        var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        _heartbeatTimer = new Timer(async _ =>
        {
            try
            {
                if (_stream != null && _tcpClient?.Connected == true)
                {
                    var ping = FrameCodec.EncodeEmpty(OpCodes.PING);
                    await _stream.WriteAsync(ping);
                    await _stream.FlushAsync();
                }
            }
            catch
            {
                // 心跳失敗 → 接收迴圈會偵測到斷線
            }
        }, null, interval, interval);
    }

    /// <summary>停止心跳</summary>
    private void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>斷開連線</summary>
    private void Disconnect()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
        _stream = null;
        _tcpClient = null;
    }

    /// <summary>停止 Worker</summary>
    public void Stop()
    {
        _running = false;
    }
}

// ── 內部 DTO ──

internal class RegisterAckMessage
{
    public bool Ok { get; set; }
    public string? WorkerId { get; set; }
    public string? Error { get; set; }
}

internal class ExecuteCommand
{
    public string RequestId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Scope { get; set; } = "{}";
    public string TraceId { get; set; } = string.Empty;
}
