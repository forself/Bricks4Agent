using System.Text;
using System.Text.Json;

namespace CacheProtocol;

/// <summary>
/// 快取協議序列化工具
///
/// 負責 CacheCommand / CacheResponse ↔ byte[] 的轉換，
/// 以及完整 frame 的組裝/拆解。
/// </summary>
public static class CacheSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    };

    // ── 序列化 ──

    /// <summary>將 CacheCommand 序列化為 JSON bytes</summary>
    public static byte[] SerializeCommand(CacheCommand command)
    {
        return JsonSerializer.SerializeToUtf8Bytes(command, JsonOptions);
    }

    /// <summary>將 CacheResponse 序列化為 JSON bytes</summary>
    public static byte[] SerializeResponse(CacheResponse response)
    {
        return JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
    }

    /// <summary>將任意物件序列化為 JSON bytes</summary>
    public static byte[] Serialize<T>(T obj)
    {
        return JsonSerializer.SerializeToUtf8Bytes(obj, JsonOptions);
    }

    // ── 反序列化 ──

    /// <summary>從 JSON bytes 反序列化 CacheCommand</summary>
    public static CacheCommand? DeserializeCommand(ReadOnlySpan<byte> payload)
    {
        return JsonSerializer.Deserialize<CacheCommand>(payload, JsonOptions);
    }

    /// <summary>從 JSON bytes 反序列化 CacheResponse</summary>
    public static CacheResponse? DeserializeResponse(ReadOnlySpan<byte> payload)
    {
        return JsonSerializer.Deserialize<CacheResponse>(payload, JsonOptions);
    }

    /// <summary>從 JSON bytes 反序列化任意型別</summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    // ── Frame 組裝 ──

    /// <summary>組裝完整的 request frame</summary>
    public static byte[] EncodeRequest(byte opCode, CacheCommand command)
    {
        var payload = SerializeCommand(command);
        return FrameCodec.Encode(opCode, payload);
    }

    /// <summary>組裝完整的 response frame</summary>
    public static byte[] EncodeResponse(byte opCode, CacheResponse response)
    {
        var payload = SerializeResponse(response);
        return FrameCodec.Encode(opCode, payload);
    }

    /// <summary>組裝完整的 response OK frame</summary>
    public static byte[] EncodeOk(CacheResponse response)
    {
        return EncodeResponse(OpCodes.RESPONSE_OK, response);
    }

    /// <summary>組裝完整的 error frame</summary>
    public static byte[] EncodeError(string requestId, string error)
    {
        return EncodeResponse(OpCodes.RESPONSE_ERR, CacheResponse.Fail(requestId, error));
    }

    /// <summary>組裝 REDIRECT frame</summary>
    public static byte[] EncodeRedirect(string requestId, string leaderHost, int leaderPort)
    {
        return EncodeResponse(OpCodes.REDIRECT, CacheResponse.Redirect(requestId, leaderHost, leaderPort));
    }

    /// <summary>組裝 PUB_MESSAGE frame（Server → Client 推送）</summary>
    public static byte[] EncodePubMessage(string channel, string message)
    {
        return EncodeResponse(OpCodes.PUB_MESSAGE, CacheResponse.PubMessage(channel, message));
    }

    // ── 值轉換工具 ──

    /// <summary>將 .NET 值轉為 JsonElement（用於存入 CacheCommand/CacheResponse）</summary>
    public static JsonElement ToJsonElement<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>從 JsonElement 還原為 .NET 型別</summary>
    public static T? FromJsonElement<T>(JsonElement? element)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Null
            || element.Value.ValueKind == JsonValueKind.Undefined)
            return default;

        return element.Value.Deserialize<T>(JsonOptions);
    }

    /// <summary>生成請求 ID</summary>
    public static string NewRequestId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }
}
