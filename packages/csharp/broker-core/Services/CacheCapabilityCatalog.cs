using BrokerCore.Data;
using BrokerCore.Models;
using CacheClient;

namespace BrokerCore.Services;

/// <summary>
/// 快取版能力目錄（Phase 2）
///
/// 增強原 CapabilityCatalog：
/// - GetActiveGrant: 快取優先 → miss 時 DB fallback → 回填
/// - ConsumeQuota: 快取 DECR_POS → DB write-through
///
/// 快取鍵映射：
/// - grant:{principalId}:{taskId}:{sessionId}:{capabilityId} → JSON(grant)
/// - quota:{grantId} → int (remaining_quota)
///
/// 設計原則：
/// - Grant 資料在 session 內穩定 → 適合快取（TTL = grant 過期時間）
/// - Quota 消耗需要原子性 → DECR_POS + DB write-through
/// - 建立/修改 grant → DB → 快取失效
/// </summary>
public class CacheCapabilityCatalog : ICapabilityCatalog
{
    private readonly BrokerDb _db;
    private readonly IDistributedCache _cache;
    private readonly ICapabilityCatalog _dbCatalog;

    // 快取鍵前綴
    private const string GrantPrefix = "grant:";
    private const string QuotaPrefix = "quota:";

    // Grant 快取 TTL
    private static readonly TimeSpan GrantTtl = TimeSpan.FromMinutes(30);

    public CacheCapabilityCatalog(
        BrokerDb db,
        IDistributedCache cache,
        ICapabilityCatalog dbCatalog)
    {
        _db = db;
        _cache = cache;
        _dbCatalog = dbCatalog;
    }

    /// <inheritdoc />
    public Capability? GetCapability(string capabilityId)
    {
        // 能力定義變動極少，直接查 DB（Phase 1 只有 6 個能力）
        return _dbCatalog.GetCapability(capabilityId);
    }

    /// <inheritdoc />
    public List<Capability> ListCapabilities(string? filter = null)
    {
        return _dbCatalog.ListCapabilities(filter);
    }

    /// <inheritdoc />
    public CapabilityGrant? GetActiveGrant(
        string principalId, string taskId, string sessionId, string capabilityId)
    {
        // 組合快取鍵
        var cacheKey = $"{GrantPrefix}{principalId}:{taskId}:{sessionId}:{capabilityId}";

        // 嘗試從快取讀取
        try
        {
            var cached = _cache.GetAsync<CachedGrant>(cacheKey)
                .GetAwaiter().GetResult();

            if (cached != null)
            {
                // 檢查是否過期
                if (cached.ExpiresAt > DateTime.UtcNow)
                {
                    return ToCapabilityGrant(cached);
                }

                // 已過期，刪除快取
                _ = _cache.DeleteAsync(cacheKey);
            }
        }
        catch
        {
            // 快取不可用
        }

        // DB fallback
        var grant = _dbCatalog.GetActiveGrant(principalId, taskId, sessionId, capabilityId);

        if (grant != null)
        {
            // 回填快取
            var ttl = grant.ExpiresAt > DateTime.UtcNow
                ? grant.ExpiresAt - DateTime.UtcNow
                : GrantTtl;

            if (ttl > TimeSpan.Zero)
            {
                var cached = ToCachedGrant(grant);
                _ = _cache.SetAsync(cacheKey, cached, ttl < GrantTtl ? ttl : GrantTtl);

                // 同時快取 quota（若有限配額）
                if (grant.RemainingQuota >= 0)
                {
                    _ = _cache.SetAsync(
                        QuotaPrefix + grant.GrantId,
                        (long)grant.RemainingQuota,
                        ttl < GrantTtl ? ttl : GrantTtl);
                }
            }
        }

        return grant;
    }

    /// <inheritdoc />
    public CapabilityGrant CreateGrant(
        string taskId, string sessionId, string principalId,
        string capabilityId, string scopeOverride, int quota, DateTime expiresAt)
    {
        // 先寫 DB
        var grant = _dbCatalog.CreateGrant(taskId, sessionId, principalId,
            capabilityId, scopeOverride, quota, expiresAt);

        // 快取 grant
        var cacheKey = $"{GrantPrefix}{principalId}:{taskId}:{sessionId}:{capabilityId}";
        var ttl = expiresAt > DateTime.UtcNow ? expiresAt - DateTime.UtcNow : GrantTtl;
        var cached = ToCachedGrant(grant);
        _ = _cache.SetAsync(cacheKey, cached, ttl < GrantTtl ? ttl : GrantTtl);

        // 快取 quota
        if (quota >= 0)
        {
            _ = _cache.SetAsync(QuotaPrefix + grant.GrantId, (long)quota, ttl < GrantTtl ? ttl : GrantTtl);
        }

        return grant;
    }

    /// <inheritdoc />
    public bool ConsumeQuota(string grantId)
    {
        // 先嘗試快取 DECR_POS（原子操作）
        try
        {
            var result = _cache.DecrIfPositiveAsync(QuotaPrefix + grantId)
                .GetAwaiter().GetResult();

            if (result.Success)
            {
                // 快取成功遞減 → 非同步更新 DB（write-through）
                _ = Task.Run(() =>
                {
                    _dbCatalog.ConsumeQuota(grantId);
                });
                return true;
            }

            // DECR_POS 失敗（quota = 0 或 key 不存在）
            // key 不存在時走 DB fallback
            if (result.NewValue == 0 && !_cache.ExistsAsync(QuotaPrefix + grantId).GetAwaiter().GetResult())
            {
                // key 不在快取中 → DB fallback
                return _dbCatalog.ConsumeQuota(grantId);
            }

            // quota 已耗盡
            return false;
        }
        catch
        {
            // 快取不可用 → DB fallback
            return _dbCatalog.ConsumeQuota(grantId);
        }
    }

    // ── 快取序列化 DTO ──

    /// <summary>快取用的精簡 Grant 資料</summary>
    private class CachedGrant
    {
        public string GrantId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string PrincipalId { get; set; } = "";
        public string CapabilityId { get; set; } = "";
        public string ScopeOverride { get; set; } = "";
        public int RemainingQuota { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private static CachedGrant ToCachedGrant(CapabilityGrant grant) => new()
    {
        GrantId = grant.GrantId,
        TaskId = grant.TaskId,
        SessionId = grant.SessionId,
        PrincipalId = grant.PrincipalId,
        CapabilityId = grant.CapabilityId,
        ScopeOverride = grant.ScopeOverride,
        RemainingQuota = grant.RemainingQuota,
        IssuedAt = grant.IssuedAt,
        ExpiresAt = grant.ExpiresAt
    };

    private static CapabilityGrant ToCapabilityGrant(CachedGrant cached) => new()
    {
        GrantId = cached.GrantId,
        TaskId = cached.TaskId,
        SessionId = cached.SessionId,
        PrincipalId = cached.PrincipalId,
        CapabilityId = cached.CapabilityId,
        ScopeOverride = cached.ScopeOverride,
        RemainingQuota = cached.RemainingQuota,
        IssuedAt = cached.IssuedAt,
        ExpiresAt = cached.ExpiresAt,
        Status = GrantStatus.Active
    };
}
