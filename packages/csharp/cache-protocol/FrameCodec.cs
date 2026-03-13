using System.Buffers.Binary;

namespace CacheProtocol;

/// <summary>
/// Length-Prefixed Frame 編解碼器
///
/// Wire Format:
///   [Magic: 2 bytes] [Length: 4 bytes BE] [OpCode: 1 byte] [Payload: variable]
///
/// Magic = 0xCA 0xCE（識別協議，快速拒絕非法封包）
/// Length = Payload 長度（不含 header 7 bytes）
/// OpCode = 操作類型
/// Payload = UTF-8 JSON
///
/// 最大封包大小：16 MB（防止 OOM）
/// </summary>
public static class FrameCodec
{
    public const byte MagicByte1 = 0xCA;
    public const byte MagicByte2 = 0xCE;
    public const int HeaderSize = 7; // Magic(2) + Length(4) + OpCode(1)
    public const int MaxPayloadSize = 16 * 1024 * 1024; // 16 MB

    /// <summary>
    /// 編碼一個 frame
    /// </summary>
    /// <param name="opCode">操作類型</param>
    /// <param name="payload">Payload bytes（UTF-8 JSON）</param>
    /// <returns>完整的 wire frame bytes</returns>
    public static byte[] Encode(byte opCode, byte[] payload)
    {
        if (payload.Length > MaxPayloadSize)
            throw new ArgumentException($"Payload too large: {payload.Length} bytes (max {MaxPayloadSize})");

        var frame = new byte[HeaderSize + payload.Length];

        // Magic
        frame[0] = MagicByte1;
        frame[1] = MagicByte2;

        // Length (Big-Endian uint32)
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(2, 4), (uint)payload.Length);

        // OpCode
        frame[6] = opCode;

        // Payload
        Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);

        return frame;
    }

    /// <summary>
    /// 編碼一個無 payload 的 frame（如 PING/PONG）
    /// </summary>
    public static byte[] EncodeEmpty(byte opCode) => Encode(opCode, Array.Empty<byte>());

    /// <summary>
    /// Frame 解析結果
    /// </summary>
    public readonly struct ParsedFrame
    {
        public readonly byte OpCode;
        public readonly ReadOnlyMemory<byte> Payload;
        public readonly int TotalLength; // header + payload

        public ParsedFrame(byte opCode, ReadOnlyMemory<byte> payload, int totalLength)
        {
            OpCode = opCode;
            Payload = payload;
            TotalLength = totalLength;
        }
    }

    /// <summary>
    /// 嘗試從 buffer 解析一個完整 frame
    /// </summary>
    /// <param name="buffer">輸入 buffer</param>
    /// <param name="frame">解析結果</param>
    /// <returns>
    /// true = 成功解析一個完整 frame
    /// false = 資料不足，需等待更多資料
    /// </returns>
    /// <exception cref="ProtocolException">協議錯誤（magic 不匹配、payload 過大）</exception>
    public static bool TryParse(ReadOnlySpan<byte> buffer, out ParsedFrame frame)
    {
        frame = default;

        // 至少需要 header
        if (buffer.Length < HeaderSize)
            return false;

        // 驗證 Magic
        if (buffer[0] != MagicByte1 || buffer[1] != MagicByte2)
            throw new ProtocolException($"Invalid magic bytes: 0x{buffer[0]:X2} 0x{buffer[1]:X2}");

        // 讀取 Length
        var payloadLength = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(2, 4));

        if (payloadLength > MaxPayloadSize)
            throw new ProtocolException($"Payload too large: {payloadLength} bytes (max {MaxPayloadSize})");

        var totalLength = HeaderSize + payloadLength;

        // 資料不足
        if (buffer.Length < totalLength)
            return false;

        var opCode = buffer[6];
        var payload = buffer.Slice(HeaderSize, payloadLength).ToArray();

        frame = new ParsedFrame(opCode, payload, totalLength);
        return true;
    }
}

/// <summary>
/// 協議錯誤例外
/// </summary>
public class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
    public ProtocolException(string message, Exception inner) : base(message, inner) { }
}
