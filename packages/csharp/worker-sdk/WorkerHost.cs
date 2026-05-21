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

    // 序列化所有 frame 寫入：WORKER_RESULT 是 fire-and-forget 並發處理（見 ReceiveLoopAsync 的 _ = HandleExecuteAsync），
    // 加上心跳 PING 在獨立 thread 寫，沒鎖的話兩個 WriteAsync 會在同一個 NetworkStream 上交錯 → broker
    // 端讀到 "Invalid magic bytes"。這把鎖讓每個 frame 完整寫完才放下一個（鏡像 broker 端的 WorkerConnection._writeLock）。
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // 持久化接收緩衝區。一次 TCP read 可能含「多個」frame —— broker 在並發 dispatch（MaxParallel>1）時
    // 會把多個 WORKER_EXECUTE 連續寫出、TCP 合併成一段。舊版每次 ReceiveFrameAsync 用全新 local buffer、
    // 解析完第一個 frame 就 return、把後面的 frame bytes 丟掉 → 下次從 socket 讀到的是「下一段」、
    // 跟被丟掉的尾段對不上 → "Invalid magic bytes" → 連線崩 → 整批 cascade。這就是長期被迫 sequential 的真因。
    // 改成持久化 buffer + 解析後保留剩餘 bytes（鏡像 broker 端 WorkerConnection 的 H-4 修法）。
    private byte[] _rxBuffer = new byte[4096];
    private int _rxFilled;

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
        int consecutiveFailures = 0;
        var rng = new Random();
        const int MaxBackoffSeconds = 60;

        while (_running && !ct.IsCancellationRequested)
        {
            bool connectedThisAttempt = false;
            try
            {
                // 1. 連線
                await ConnectAsync(ct);

                // 2. 註冊
                await RegisterAsync(ct);

                // 3. 啟動心跳
                StartHeartbeat();

                connectedThisAttempt = true;
                consecutiveFailures = 0;  // 連上就重置 backoff

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

            // 連線曾成功但現在斷了 → 清計數當作「剛斷線」
            // 從來沒連上就累加 failure（backoff 用）
            if (!connectedThisAttempt) consecutiveFailures++;

            // 重連等待：指數 backoff + jitter，從 base (5s) 開始、每次雙倍、最多 MaxBackoffSeconds
            if (_running && _options.AutoReconnect && !ct.IsCancellationRequested)
            {
                var baseSeconds = _options.ReconnectIntervalSeconds;
                var expSeconds = Math.Min(MaxBackoffSeconds, baseSeconds * Math.Pow(2, Math.Min(consecutiveFailures, 6)));
                var jitter = rng.NextDouble();  // 0-1s
                var waitSeconds = expSeconds + jitter;

                _logger.LogInformation(
                    "Reconnecting in {Wait:F1}s (consecutive failures: {N})",
                    waitSeconds, consecutiveFailures);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
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
        _rxFilled = 0;   // 新連線：清掉上條連線殘留的半截 frame bytes
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

        await WriteFrameAsync(frame, ct);

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

        await WriteFrameAsync(frame, ct);
    }

    /// <summary>處理 WORKER_STATUS 查詢</summary>
    private async Task HandleStatusQueryAsync(CancellationToken ct)
    {
        var status = new { active_tasks = 0, load_pct = 0 };
        var payload = JsonSerializer.SerializeToUtf8Bytes(status, JsonOptions);
        var frame = FrameCodec.Encode(OpCodes.WORKER_STATUS_ACK, payload);

        await WriteFrameAsync(frame, ct);
    }

    /// <summary>序列化寫入一個完整 frame（write lock 保護、避免並發 frame 在 stream 上交錯）</summary>
    private async Task WriteFrameAsync(byte[] frame, CancellationToken ct = default)
    {
        var stream = _stream;
        if (stream == null) throw new InvalidOperationException("Worker stream not connected");
        await _writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>接收一個完整 frame（持久化 buffer、保留一次 read 內多餘的 frame bytes）</summary>
    private async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)> ReceiveFrameAsync(
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 先試從既有 buffer 解（上次 read 可能已含 ≥1 個完整 frame）
            var parsed = TryParseAndAdvance();
            if (parsed.HasValue) return parsed.Value;

            // 擴容
            if (_rxFilled >= _rxBuffer.Length)
            {
                var newBuffer = new byte[_rxBuffer.Length * 2];
                Buffer.BlockCopy(_rxBuffer, 0, newBuffer, 0, _rxFilled);
                _rxBuffer = newBuffer;
            }

            var bytesRead = await _stream!.ReadAsync(
                _rxBuffer.AsMemory(_rxFilled, _rxBuffer.Length - _rxFilled), ct);

            if (bytesRead == 0)
                throw new IOException("Connection closed by broker");

            _rxFilled += bytesRead;

            parsed = TryParseAndAdvance();
            if (parsed.HasValue) return parsed.Value;
        }

        throw new OperationCanceledException();
    }

    /// <summary>從持久化 buffer 解一個 frame，成功後把已消耗 bytes 移除、剩餘留到開頭。</summary>
    private (byte OpCode, ReadOnlyMemory<byte> Payload)? TryParseAndAdvance()
    {
        if (_rxFilled < FrameCodec.HeaderSize) return null;

        var span = _rxBuffer.AsSpan(0, _rxFilled);
        if (FrameCodec.TryParse(span, out var frame))
        {
            var payload = new byte[frame.Payload.Length];
            frame.Payload.Span.CopyTo(payload);

            var remaining = _rxFilled - frame.TotalLength;
            if (remaining > 0)
                Buffer.BlockCopy(_rxBuffer, frame.TotalLength, _rxBuffer, 0, remaining);
            _rxFilled = remaining;

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
                    await WriteFrameAsync(ping);
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
