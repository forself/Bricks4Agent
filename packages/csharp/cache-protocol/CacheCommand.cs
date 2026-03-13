using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheProtocol;

/// <summary>
/// 快取命令（Request payload）
/// 所有欄位都是可選的，依 OpCode 決定哪些欄位有效
/// </summary>
public class CacheCommand
{
    /// <summary>請求 ID（用於 request-response 配對）</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>鍵名</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>值（JSON 序列化的任意型別）</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>TTL（毫秒，0 = 永不過期）</summary>
    [JsonPropertyName("ttl_ms")]
    public long TtlMs { get; set; }

    /// <summary>CAS：預期值</summary>
    [JsonPropertyName("expected")]
    public JsonElement? Expected { get; set; }

    /// <summary>CAS_GT：閾值（current < threshold 時才更新）</summary>
    [JsonPropertyName("threshold")]
    public long Threshold { get; set; }

    /// <summary>CAS/CAS_GT/INCR：新值或 delta</summary>
    [JsonPropertyName("new_value")]
    public long NewValue { get; set; }

    /// <summary>INCR：遞增量</summary>
    [JsonPropertyName("delta")]
    public long Delta { get; set; } = 1;

    /// <summary>LOCK：資源名稱</summary>
    [JsonPropertyName("resource")]
    public string? Resource { get; set; }

    /// <summary>LOCK：擁有者 ID</summary>
    [JsonPropertyName("owner_id")]
    public string? OwnerId { get; set; }

    /// <summary>LOCK：超時（毫秒）</summary>
    [JsonPropertyName("timeout_ms")]
    public long TimeoutMs { get; set; }

    /// <summary>LOCK/UNLOCK：Fencing Token</summary>
    [JsonPropertyName("fencing_token")]
    public long FencingToken { get; set; }

    /// <summary>PUBLISH/SUBSCRIBE：頻道</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>PUBLISH：訊息內容</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>EXPIRE：TTL 秒數</summary>
    [JsonPropertyName("expire_seconds")]
    public double ExpireSeconds { get; set; }
}

/// <summary>
/// 快取回應（Response payload）
/// </summary>
public class CacheResponse
{
    /// <summary>請求 ID（與 CacheCommand.Id 對應）</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>是否成功</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>值（GET 回應）</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>CAS 是否成功交換</summary>
    [JsonPropertyName("swapped")]
    public bool Swapped { get; set; }

    /// <summary>數值結果（INCR/DECR_POS 回應）</summary>
    [JsonPropertyName("num_value")]
    public long NumValue { get; set; }

    /// <summary>鍵是否存在（EXISTS 回應）</summary>
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }

    /// <summary>LOCK：Fencing Token</summary>
    [JsonPropertyName("fencing_token")]
    public long FencingToken { get; set; }

    /// <summary>LOCK：是否取得鎖</summary>
    [JsonPropertyName("acquired")]
    public bool Acquired { get; set; }

    /// <summary>錯誤訊息</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>REDIRECT：Leader 位址</summary>
    [JsonPropertyName("leader_host")]
    public string? LeaderHost { get; set; }

    /// <summary>REDIRECT：Leader 端口</summary>
    [JsonPropertyName("leader_port")]
    public int LeaderPort { get; set; }

    /// <summary>PUB_MESSAGE：頻道</summary>
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    /// <summary>PUB_MESSAGE：訊息</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>版本號（用於樂觀並行控制）</summary>
    [JsonPropertyName("version")]
    public long Version { get; set; }

    // ── 工廠方法 ──

    public static CacheResponse Success(string requestId) => new() { Id = requestId, Ok = true };
    public static CacheResponse Fail(string requestId, string error) => new() { Id = requestId, Ok = false, Error = error };

    public static CacheResponse WithValue(string requestId, JsonElement? value) => new()
    {
        Id = requestId, Ok = true, Value = value
    };

    public static CacheResponse WithSwap(string requestId, bool swapped) => new()
    {
        Id = requestId, Ok = true, Swapped = swapped
    };

    public static CacheResponse WithNumber(string requestId, long value) => new()
    {
        Id = requestId, Ok = true, NumValue = value
    };

    public static CacheResponse WithLock(string requestId, bool acquired, long fencingToken) => new()
    {
        Id = requestId, Ok = true, Acquired = acquired, FencingToken = fencingToken
    };

    public static CacheResponse Redirect(string requestId, string host, int port) => new()
    {
        Id = requestId, Ok = false, LeaderHost = host, LeaderPort = port
    };

    public static CacheResponse PubMessage(string channel, string message) => new()
    {
        Ok = true, Channel = channel, Message = message
    };
}

/// <summary>
/// 複製日誌條目（Leader → Follower）
/// </summary>
public class ReplicationEntry
{
    [JsonPropertyName("lsn")]
    public long Lsn { get; set; }

    [JsonPropertyName("op")]
    public byte OpCode { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    [JsonPropertyName("ttl_ms")]
    public long TtlMs { get; set; }

    [JsonPropertyName("ts")]
    public long TimestampMs { get; set; }

    /// <summary>CAS_GT 專用：新的 long 值</summary>
    [JsonPropertyName("new_value")]
    public long NewValue { get; set; }
}

/// <summary>
/// 選舉投票請求
/// </summary>
public class VoteRequest
{
    [JsonPropertyName("candidate_id")]
    public string CandidateId { get; set; } = string.Empty;

    [JsonPropertyName("last_lsn")]
    public long LastAppliedLsn { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("term")]
    public long Term { get; set; }
}

/// <summary>
/// 選舉投票回應
/// </summary>
public class VoteResponse
{
    [JsonPropertyName("voter_id")]
    public string VoterId { get; set; } = string.Empty;

    [JsonPropertyName("granted")]
    public bool Granted { get; set; }

    [JsonPropertyName("term")]
    public long Term { get; set; }
}

/// <summary>
/// Leader 宣告
/// </summary>
public class LeaderAnnouncement
{
    [JsonPropertyName("leader_id")]
    public string LeaderId { get; set; } = string.Empty;

    [JsonPropertyName("leader_host")]
    public string LeaderHost { get; set; } = string.Empty;

    [JsonPropertyName("leader_port")]
    public int LeaderPort { get; set; }

    [JsonPropertyName("term")]
    public long Term { get; set; }
}
