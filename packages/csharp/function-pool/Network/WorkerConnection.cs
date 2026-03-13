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

    // H-4 修復：持久化接收緩衝區，避免 TCP 讀取中多餘 bytes 被丟棄
    private byte[] _receiveBuffer = new byte[4096];
    private int _receiveFilled;

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
    /// H-4 修復：使用持久化接收緩衝區，保留 TCP 讀取中超出一個 frame 的剩餘 bytes
    /// </summary>
    public async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)?> ReceiveFrameAsync(
        CancellationToken ct = default)
    {
        if (_disposed) return null;

        while (!ct.IsCancellationRequested)
        {
            // 先嘗試從已有緩衝區解析（上次讀取可能已包含完整 frame）
            var result = TryParseAndAdvance();
            if (result.HasValue)
            {
                return result.Value;
            }

            // 確保緩衝區有空間
            if (_receiveFilled >= _receiveBuffer.Length)
            {
                var newBuffer = new byte[_receiveBuffer.Length * 2];
                Buffer.BlockCopy(_receiveBuffer, 0, newBuffer, 0, _receiveFilled);
                _receiveBuffer = newBuffer;
            }

            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(
                    _receiveBuffer.AsMemory(_receiveFilled, _receiveBuffer.Length - _receiveFilled), ct);
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

            _receiveFilled += bytesRead;
            LastActivity = DateTime.UtcNow;

            // 再次嘗試解析（加入新資料後）
            result = TryParseAndAdvance();
            if (result.HasValue)
            {
                return result.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// 嘗試從持久化緩衝區解析一個完整 frame，成功後移除已消耗的 bytes
    /// </summary>
    private (byte OpCode, ReadOnlyMemory<byte> Payload)? TryParseAndAdvance()
    {
        if (_receiveFilled < FrameCodec.HeaderSize)
            return null;

        var span = _receiveBuffer.AsSpan(0, _receiveFilled);
        if (FrameCodec.TryParse(span, out var frame))
        {
            var payload = new byte[frame.Payload.Length];
            frame.Payload.Span.CopyTo(payload);

            // 將剩餘 bytes 移到緩衝區開頭（保留後續 frame 資料）
            var remaining = _receiveFilled - frame.TotalLength;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_receiveBuffer, frame.TotalLength, _receiveBuffer, 0, remaining);
            }
            _receiveFilled = remaining;

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
