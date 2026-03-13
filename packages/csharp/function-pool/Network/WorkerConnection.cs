using System.Collections.Concurrent;
using System.Net.Sockets;
using CacheProtocol;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Network;

/// <summary>
/// Worker TCP 連線封裝（Broker 端管理）
///
/// 職責：
/// 1. 封裝 TCP 連線的讀寫操作
/// 2. SemaphoreSlim 保護並發收發
/// 3. 支援 SendAndWaitAsync（發送 WORKER_EXECUTE 並等待 WORKER_RESULT）
/// 4. 支援 SendFrameAsync（僅發送，如 PONG、REGISTER_ACK）
/// </summary>
public class WorkerConnection : IAsyncDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // 等待中的請求（requestId → TaskCompletionSource）
    private readonly ConcurrentDictionary<string, TaskCompletionSource<(byte OpCode, ReadOnlyMemory<byte> Payload)>>
        _pendingRequests = new();

    private volatile bool _disposed;

    public string WorkerId { get; set; } = string.Empty;
    public string RemoteEndpoint { get; }
    public bool IsConnected => !_disposed && _tcpClient.Connected;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public WorkerConnection(TcpClient tcpClient, ILogger logger)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _logger = logger;
        RemoteEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    /// <summary>
    /// 發送 frame 並等待特定 requestId 的回應
    /// </summary>
    public async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)> SendAndWaitAsync(
        byte[] frame, string requestId, TimeSpan timeout, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<(byte, ReadOnlyMemory<byte>)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pendingRequests[requestId] = tcs;

        try
        {
            await SendFrameAsync(frame, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(Timeout.Infinite, timeoutCts.Token));

            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }

            throw new TimeoutException(
                $"Worker dispatch timeout ({timeout.TotalSeconds}s) for request {requestId}");
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// 發送 frame（不等待回應）
    /// </summary>
    public async Task SendFrameAsync(byte[] frame, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WorkerConnection));

        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
            LastActivity = DateTime.UtcNow;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 完成等待中的請求（由 WorkerSession 的接收迴圈呼叫）
    /// </summary>
    public bool CompleteRequest(string requestId, byte opCode, ReadOnlyMemory<byte> payload)
    {
        if (_pendingRequests.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetResult((opCode, payload));
            return true;
        }
        return false;
    }

    /// <summary>
    /// 取消所有等待中的請求（Worker 斷線時呼叫）
    /// </summary>
    public void CancelAllPending(string reason)
    {
        foreach (var (requestId, tcs) in _pendingRequests)
        {
            tcs.TrySetException(new IOException($"Worker disconnected: {reason}"));
        }
        _pendingRequests.Clear();
    }

    /// <summary>
    /// 讀取一個完整的 frame（阻塞直到接收完成）
    /// </summary>
    public async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)?> ReceiveFrameAsync(
        CancellationToken ct = default)
    {
        if (_disposed) return null;

        var buffer = new byte[4096];
        int filled = 0;

        while (!ct.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(
                    buffer.AsMemory(filled, buffer.Length - filled), ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }

            if (bytesRead == 0)
                return null; // 連線已關閉

            filled += bytesRead;
            LastActivity = DateTime.UtcNow;

            // 嘗試解析 frame（同步，避免 Span 跨 await）
            var result = TryParseFromBuffer(buffer, filled);
            if (result.HasValue)
            {
                return result.Value;
            }

            // Buffer 不足，擴容
            if (filled >= buffer.Length)
            {
                var newBuffer = new byte[buffer.Length * 2];
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, filled);
                buffer = newBuffer;
            }
        }

        return null;
    }

    /// <summary>同步 frame 解析（避免 Span 跨 await）</summary>
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

    public void Close()
    {
        if (_disposed) return;
        _disposed = true;

        CancelAllPending("Connection closing");

        try { _stream.Dispose(); } catch { }
        try { _tcpClient.Dispose(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        Close();
        _writeLock.Dispose();
    }
}
