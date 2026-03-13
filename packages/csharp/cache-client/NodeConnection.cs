using System.Net.Sockets;
using System.Text.Json;
using CacheProtocol;

namespace CacheClient;

/// <summary>
/// 單一 TCP 連線封裝
///
/// 負責：
/// 1. TCP 連線建立與管理
/// 2. Frame 發送與接收（同步 request-response）
/// 3. 接收 Pub/Sub 推送訊息
/// 4. 心跳 (PING/PONG)
/// </summary>
public class NodeConnection : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _operationTimeout;

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendRecvLock = new(1, 1);
    private volatile bool _connected;
    private volatile bool _disposed;

    // Pub/Sub 推送回呼
    private Action<string, string>? _pubSubHandler;

    public string Host => _host;
    public int Port => _port;
    public bool IsConnected => _connected && _tcpClient?.Connected == true;
    public DateTime LastUsedAt { get; private set; } = DateTime.UtcNow;

    public NodeConnection(string host, int port, TimeSpan connectTimeout, TimeSpan operationTimeout)
    {
        _host = host;
        _port = port;
        _connectTimeout = connectTimeout;
        _operationTimeout = operationTimeout;
    }

    /// <summary>建立連線</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        _tcpClient = new TcpClient { NoDelay = true };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_connectTimeout);

        await _tcpClient.ConnectAsync(_host, _port, timeoutCts.Token);
        _stream = _tcpClient.GetStream();
        _connected = true;
    }

    /// <summary>
    /// 發送請求並等待回應（request-response 模式）
    /// </summary>
    public async Task<(byte OpCode, CacheResponse Response)> SendAsync(
        byte opCode, CacheCommand command, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected");

        await _sendRecvLock.WaitAsync(ct);
        try
        {
            // 編碼並發送
            var frame = CacheSerializer.EncodeRequest(opCode, command);
            await _stream!.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);

            // 接收回應
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_operationTimeout);

            var (respOpCode, respPayload) = await ReceiveFrameAsync(timeoutCts.Token);

            LastUsedAt = DateTime.UtcNow;

            // 處理 Pub/Sub 推送（在等待回應期間可能收到）
            while (respOpCode == OpCodes.PUB_MESSAGE)
            {
                HandlePubMessage(respPayload);
                (respOpCode, respPayload) = await ReceiveFrameAsync(timeoutCts.Token);
            }

            // 反序列化回應
            var response = CacheSerializer.DeserializeResponse(respPayload.Span)
                ?? new CacheResponse { Ok = false, Error = "Failed to deserialize response" };

            return (respOpCode, response);
        }
        finally
        {
            _sendRecvLock.Release();
        }
    }

    /// <summary>發送 PING 並等待 PONG</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return false;

        await _sendRecvLock.WaitAsync(ct);
        try
        {
            var frame = FrameCodec.EncodeEmpty(OpCodes.PING);
            await _stream!.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var (respOpCode, _) = await ReceiveFrameAsync(timeoutCts.Token);
            LastUsedAt = DateTime.UtcNow;
            return respOpCode == OpCodes.PONG;
        }
        catch
        {
            _connected = false;
            return false;
        }
        finally
        {
            _sendRecvLock.Release();
        }
    }

    /// <summary>設定 Pub/Sub 推送處理器</summary>
    public void SetPubSubHandler(Action<string, string>? handler)
    {
        _pubSubHandler = handler;
    }

    /// <summary>接收一個完整的 frame</summary>
    private async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)> ReceiveFrameAsync(
        CancellationToken ct)
    {
        var buffer = new byte[4096];
        int filled = 0;

        while (true)
        {
            var bytesRead = await _stream!.ReadAsync(
                buffer.AsMemory(filled, buffer.Length - filled), ct);

            if (bytesRead == 0)
                throw new IOException("Connection closed by remote");

            filled += bytesRead;

            // 嘗試解析 frame（同步，避免 Span 跨 await）
            var result = TryParseFromBuffer(buffer, 0, filled);
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
    }

    /// <summary>同步 frame 解析（避免 Span 跨 await）</summary>
    private static (byte OpCode, ReadOnlyMemory<byte> Payload)? TryParseFromBuffer(
        byte[] buffer, int offset, int length)
    {
        if (length < FrameCodec.HeaderSize)
            return null;

        var span = buffer.AsSpan(offset, length);
        if (FrameCodec.TryParse(span, out var frame))
        {
            // 複製 payload 到獨立的 Memory（因為 buffer 會被覆寫）
            var payload = new byte[frame.Payload.Length];
            frame.Payload.CopyTo(payload);
            return (frame.OpCode, payload);
        }
        return null;
    }

    /// <summary>處理 Pub/Sub 推送</summary>
    private void HandlePubMessage(ReadOnlyMemory<byte> payload)
    {
        try
        {
            var response = CacheSerializer.DeserializeResponse(payload.Span);
            if (response?.Channel != null && _pubSubHandler != null)
            {
                _pubSubHandler(response.Channel, response.Message ?? "");
            }
        }
        catch { /* ignore pub/sub errors */ }
    }

    /// <summary>關閉連線</summary>
    public void Close()
    {
        _connected = false;
        try { _stream?.Dispose(); } catch { }
        try { _tcpClient?.Dispose(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _sendRecvLock.Dispose();
    }
}
