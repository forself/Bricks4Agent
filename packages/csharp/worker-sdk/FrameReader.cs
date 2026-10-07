using CacheProtocol;

namespace WorkerSdk;

/// <summary>
/// 從一條 stream 依序讀出完整 frame。
///
/// 讀到的位元組留在這個物件的緩衝區：一次讀取若同時收到兩個以上的 frame（或下一個 frame 的開頭），
/// 剩下的位元組會留給下一次 <see cref="ReadFrameAsync"/>，不會被丟掉。
/// 一條連線只用一個 reader，且同一時間只有一個呼叫者在讀（WorkerHost 的接收迴圈）。
/// </summary>
internal sealed class FrameReader
{
    private const int InitialBufferSize = 4096;

    private readonly Stream _stream;
    private byte[] _buffer = new byte[InitialBufferSize];
    private int _start;
    private int _filled;

    public FrameReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>目前緩衝區中尚未交出的位元組數（測試用）。</summary>
    internal int BufferedBytes => _filled - _start;

    /// <summary>讀出下一個完整 frame；連線關閉時丟出 <see cref="IOException"/>。</summary>
    public async Task<(byte OpCode, ReadOnlyMemory<byte> Payload)> ReadFrameAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // 先用緩衝區中已有的資料：上一次讀取可能已經帶進完整的下一個 frame。
            var parsed = TryTakeFrame();
            if (parsed.HasValue)
                return parsed.Value;

            EnsureSpace();

            var bytesRead = await _stream.ReadAsync(_buffer.AsMemory(_filled, _buffer.Length - _filled), ct);
            if (bytesRead == 0)
                throw new IOException("Connection closed by broker");

            _filled += bytesRead;
        }
    }

    /// <summary>緩衝區中有完整 frame 時取出它並前移起點；否則回傳 null。同步執行（Span 不跨 await）。</summary>
    private (byte OpCode, ReadOnlyMemory<byte> Payload)? TryTakeFrame()
    {
        var available = _filled - _start;
        if (available < FrameCodec.HeaderSize)
            return null;

        if (!FrameCodec.TryParse(_buffer.AsSpan(_start, available), out var frame))
            return null;

        _start += frame.TotalLength;
        if (_start == _filled)
        {
            _start = 0;
            _filled = 0;
        }

        return (frame.OpCode, frame.Payload);
    }

    /// <summary>確保緩衝區尾端還有空間：先把未交出的資料移到開頭，仍不夠時加倍擴容。</summary>
    private void EnsureSpace()
    {
        if (_filled < _buffer.Length)
            return;

        var pending = _filled - _start;
        if (_start > 0)
        {
            Buffer.BlockCopy(_buffer, _start, _buffer, 0, pending);
            _start = 0;
            _filled = pending;
            if (_filled < _buffer.Length)
                return;
        }

        // FrameCodec.TryParse 會拒絕超過 MaxPayloadSize 的 frame，所以緩衝區不會無限成長。
        var newBuffer = new byte[_buffer.Length * 2];
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _filled);
        _buffer = newBuffer;
    }
}
