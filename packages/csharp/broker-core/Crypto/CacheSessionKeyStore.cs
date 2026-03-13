using CacheClient;

namespace BrokerCore.Crypto;

/// <summary>
/// 快取版 Session 金鑰存儲（Phase 2）
///
/// 快取層級：
/// - Store: 寫入快取 + DB
/// - Retrieve: 快取優先 → miss 時 DB fallback → 回填快取
/// - TryAdvanceSeq: 快取 CAS_GT → miss 時 DB fallback
///
/// 快取鍵映射：
/// - skey:{sessionId} → encrypted_session_key (不可變)
/// - seq:{sessionId}  → last_seen_seq (CAS_GT)
///
/// 特性：
/// - session_key 為不可變資料，一旦快取命中就不需再查 DB
/// - seq 透過 CAS_GT 保證嚴格遞增
/// - 快取不可用時透過 FallbackDecorator 自動降級到 DB
/// </summary>
public class CacheSessionKeyStore : ISessionKeyStore
{
    private readonly IDistributedCache _cache;
    private readonly ISessionKeyStore _dbFallback;

    // 快取鍵前綴
    private const string SessionKeyPrefix = "skey:";
    private const string SeqPrefix = "seq:";

    // Session 預設 TTL（與 session 本身的過期時間同步）
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(2);

    public CacheSessionKeyStore(IDistributedCache cache, ISessionKeyStore dbFallback)
    {
        _cache = cache;
        _dbFallback = dbFallback;
    }

    /// <inheritdoc />
    public void Store(string sessionId, byte[] sessionKey)
    {
        // 先寫 DB（持久化保證）
        _dbFallback.Store(sessionId, sessionKey);

        // 再寫快取（加速後續讀取）
        // 注意：這裡存的是 DB 層已加密的 session_key
        // 但 ISessionKeyStore.Store() 介面接受的是明文 session_key
        // 直接快取明文 → Retrieve 時直接回傳
        var base64Key = Convert.ToBase64String(sessionKey);
        _ = _cache.SetAsync(SessionKeyPrefix + sessionId, base64Key, DefaultTtl);

        // 初始化 seq = 0
        _ = _cache.SetAsync(SeqPrefix + sessionId, 0L, DefaultTtl);
    }

    /// <inheritdoc />
    public byte[]? Retrieve(string sessionId)
    {
        // 嘗試從快取讀取
        try
        {
            var cached = _cache.GetAsync<string>(SessionKeyPrefix + sessionId)
                .GetAwaiter().GetResult();

            if (cached != null)
                return Convert.FromBase64String(cached);
        }
        catch
        {
            // 快取不可用，走 DB
        }

        // 快取 miss → DB fallback
        var key = _dbFallback.Retrieve(sessionId);
        if (key != null)
        {
            // 回填快取
            var base64Key = Convert.ToBase64String(key);
            _ = _cache.SetAsync(SessionKeyPrefix + sessionId, base64Key, DefaultTtl);
        }

        return key;
    }

    /// <inheritdoc />
    public void Remove(string sessionId)
    {
        // 先清快取
        _ = _cache.DeleteAsync(SessionKeyPrefix + sessionId);
        _ = _cache.DeleteAsync(SeqPrefix + sessionId);

        // 再清 DB
        _dbFallback.Remove(sessionId);
    }

    /// <inheritdoc />
    public bool TryAdvanceSeq(string sessionId, int newSeq)
    {
        // 嘗試透過快取 CAS_GT
        try
        {
            var result = _cache.CasIfGreaterAsync(
                SeqPrefix + sessionId, newSeq, DefaultTtl)
                .GetAwaiter().GetResult();

            if (result.Swapped)
                return true;

            // CAS_GT 失敗：newSeq <= current → replay
            if (result.CurrentValue >= newSeq)
                return false;

            // CAS_GT 結果不明確 → 走 DB 確認
        }
        catch
        {
            // 快取不可用
        }

        // DB fallback
        return _dbFallback.TryAdvanceSeq(sessionId, newSeq);
    }
}
