using System.Buffers;
using System.Net.Sockets;
using CacheProtocol;
using CacheServer.Engine;
using Microsoft.Extensions.Logging;

namespace CacheServer.Server;

/// <summary>
/// 每個 TCP 連線的 session 狀態
///
/// 負責：
/// 1. 從 TCP stream 讀取 frame（length-prefixed）
/// 2. 解碼 frame → 委託 CommandRouter 處理
/// 3. 將回應 frame 寫回 TCP stream
/// 4. 管理該連線的 Pub/Sub 訂閱
/// 5. 心跳（PING/PONG）
/// </summary>
public class ClientSession : IAsyncDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly CommandRouter _router;
    private readonly ILogger _logger;
    private readonly string _remoteEndpoint;
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Pub/Sub 訂閱追蹤（此連線訂閱了哪些頻道）
    private readonly HashSet<string> _subscribedChannels = new();
    private readonly object _subLock = new();

    // 讀取 buffer
    private const int InitialBufferSize = 4096;
    private const int MaxBufferSize = 1024 * 1024; // 1 MB

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;
    public bool IsActive { get; private set; } = true;

    public ClientSession(
        TcpClient tcpClient,
        CommandRouter router,
        ILogger logger)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _router = router;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _remoteEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    /// <summary>
    /// 處理連線：持續讀取 frame 並處理，直到連線關閉或取消
    /// </summary>
    public async Task ProcessAsync(CancellationToken serverCt)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt, _cts.Token);
        var ct = linkedCts.Token;

        _logger.LogInformation("Client connected: {Endpoint} (session={SessionId})", _remoteEndpoint, Id);

        var buffer = new byte[InitialBufferSize];
        int offset = 0; // 未消費資料的起始位置
        int filled = 0; // buffer 中有效資料的總長度

        try
        {
            while (!ct.IsCancellationRequested && _tcpClient.Connected)
            {
                // 讀取更多資料
                var bytesRead = await _stream.ReadAsync(
                    buffer.AsMemory(filled, buffer.Length - filled), ct);

                if (bytesRead == 0)
                {
                    _logger.LogInformation("Client disconnected: {Endpoint}", _remoteEndpoint);
                    break;
                }

                filled += bytesRead;
                LastActivityAt = DateTime.UtcNow;

                // 嘗試解析所有完整的 frame
                while (filled - offset >= FrameCodec.HeaderSize)
                {
                    // 提取 frame 到同步方法（ReadOnlySpan 不能跨 await）
                    var parseResult = TryParseFrame(buffer, offset, filled - offset);

                    if (parseResult.Error != null)
                    {
                        _logger.LogWarning("Protocol error from {Endpoint}: {Error}", _remoteEndpoint, parseResult.Error);
                        return; // 關閉連線
                    }

                    if (!parseResult.Success)
                        break; // 資料不足，等待更多

                    // 處理 frame
                    await HandleFrameAsync(parseResult.Frame, ct);

                    offset += parseResult.Frame.TotalLength;
                }

                // 整理 buffer：將未消費資料移到開頭
                if (offset > 0)
                {
                    var remaining = filled - offset;
                    if (remaining > 0)
                        Buffer.BlockCopy(buffer, offset, buffer, 0, remaining);
                    filled = remaining;
                    offset = 0;
                }

                // 如果 buffer 接近滿了，擴容
                if (filled >= buffer.Length * 3 / 4 && buffer.Length < MaxBufferSize)
                {
                    var newBuffer = new byte[Math.Min(buffer.Length * 2, MaxBufferSize)];
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, filled);
                    buffer = newBuffer;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (IOException)
        {
            // 連線中斷
            _logger.LogInformation("Connection lost: {Endpoint}", _remoteEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing client {Endpoint}", _remoteEndpoint);
        }
        finally
        {
            IsActive = false;
            CleanupSubscriptions();
        }
    }

    /// <summary>
    /// 同步 frame 解析（避免 ReadOnlySpan 在 async 方法中的限制）
    /// </summary>
    private static FrameParseResult TryParseFrame(byte[] buffer, int offset, int length)
    {
        try
        {
            var span = buffer.AsSpan(offset, length);
            if (FrameCodec.TryParse(span, out var frame))
                return new FrameParseResult(true, frame, null);
            return new FrameParseResult(false, default, null);
        }
        catch (ProtocolException ex)
        {
            return new FrameParseResult(false, default, ex.Message);
        }
    }

    private readonly record struct FrameParseResult(
        bool Success,
        FrameCodec.ParsedFrame Frame,
        string? Error);

    /// <summary>處理單一 frame</summary>
    private async Task HandleFrameAsync(FrameCodec.ParsedFrame frame, CancellationToken ct)
    {
        // PING → PONG
        if (frame.OpCode == OpCodes.PING)
        {
            await SendFrameAsync(FrameCodec.EncodeEmpty(OpCodes.PONG), ct);
            return;
        }

        // PONG → 忽略（client 回覆）
        if (frame.OpCode == OpCodes.PONG)
            return;

        // SUBSCRIBE/UNSUBSCRIBE 特殊處理（需要 session 參與）
        if (frame.OpCode == OpCodes.SUBSCRIBE || frame.OpCode == OpCodes.UNSUBSCRIBE)
        {
            await HandleSubscriptionAsync(frame, ct);
            return;
        }

        // 其他操作交由 CommandRouter
        var responseFrame = _router.Handle(frame);
        if (responseFrame != null)
        {
            await SendFrameAsync(responseFrame, ct);
        }
    }

    /// <summary>處理 SUBSCRIBE / UNSUBSCRIBE</summary>
    private async Task HandleSubscriptionAsync(FrameCodec.ParsedFrame frame, CancellationToken ct)
    {
        CacheCommand? cmd = null;
        try
        {
            cmd = CacheSerializer.DeserializeCommand(frame.Payload.Span);
        }
        catch
        {
            await SendFrameAsync(
                CacheSerializer.EncodeError("", "Invalid command payload"), ct);
            return;
        }

        if (cmd == null || string.IsNullOrEmpty(cmd.Channel))
        {
            await SendFrameAsync(
                CacheSerializer.EncodeError(cmd?.Id ?? "", "Channel is required"), ct);
            return;
        }

        if (frame.OpCode == OpCodes.SUBSCRIBE)
        {
            // 建立 handler：收到 publish 時推送到此 client
            void handler(string channel, string message)
            {
                _ = SendPubMessageAsync(channel, message);
            }

            _router.Engine.Subscribe(cmd.Channel, handler);

            lock (_subLock)
            {
                _subscribedChannels.Add(cmd.Channel);
            }

            await SendFrameAsync(
                CacheSerializer.EncodeOk(CacheResponse.Success(cmd.Id)), ct);
        }
        else // UNSUBSCRIBE
        {
            _router.Engine.Unsubscribe(cmd.Channel);

            lock (_subLock)
            {
                _subscribedChannels.Remove(cmd.Channel);
            }

            await SendFrameAsync(
                CacheSerializer.EncodeOk(CacheResponse.Success(cmd.Id)), ct);
        }
    }

    /// <summary>推送 Pub/Sub 訊息到此 client</summary>
    private async Task SendPubMessageAsync(string channel, string message)
    {
        if (!IsActive) return;

        try
        {
            var frame = CacheSerializer.EncodePubMessage(channel, message);
            await SendFrameAsync(frame, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push pub message to {Endpoint}", _remoteEndpoint);
        }
    }

    /// <summary>發送 frame（執行緒安全）</summary>
    public async Task SendFrameAsync(byte[] frame, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>清理此連線的所有訂閱</summary>
    private void CleanupSubscriptions()
    {
        lock (_subLock)
        {
            foreach (var channel in _subscribedChannels)
            {
                try { _router.Engine.Unsubscribe(channel); } catch { }
            }
            _subscribedChannels.Clear();
        }
    }

    /// <summary>停止此 session</summary>
    public void Stop()
    {
        _cts.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        IsActive = false;
        CleanupSubscriptions();
        _cts.Cancel();
        _cts.Dispose();
        _writeLock.Dispose();

        try { _stream.Dispose(); } catch { }
        try { _tcpClient.Dispose(); } catch { }
    }
}
