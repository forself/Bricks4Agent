using System.Collections.Concurrent;

namespace CacheServer.Engine;

/// <summary>
/// 分散式鎖管理器
///
/// Leader 維護鎖表 + Fencing Token（單調遞增），
/// 防止鎖過期後的 stale holder 問題。
///
/// 鎖語意：
/// - 每把鎖有一個 owner_id（持有者識別）
/// - 取得鎖時發放 fencing_token（單調遞增 long）
/// - 鎖有 TTL，過期自動釋放
/// - 釋放鎖需持有正確的 owner_id
/// - Fencing token 用於下游操作：舊 token 的操作會被拒絕
/// </summary>
public class DistributedLockMgr : IDisposable
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new();
    private long _fencingCounter; // 全域遞增，保證唯一
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    /// <summary>鎖條目</summary>
    private class LockEntry
    {
        public required string Resource { get; init; }
        public required string OwnerId { get; init; }
        public required long FencingToken { get; init; }
        public required DateTime AcquiredAt { get; init; }
        public required DateTime ExpiresAt { get; init; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    public DistributedLockMgr()
    {
        // 每 5 秒清理過期鎖
        _cleanupTimer = new Timer(CleanupExpiredLocks, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 嘗試取得鎖
    /// </summary>
    /// <param name="resource">資源名稱（鎖的鍵）</param>
    /// <param name="ownerId">擁有者 ID</param>
    /// <param name="timeout">鎖 TTL</param>
    /// <returns>(acquired, fencingToken) — 是否取得 + fencing token</returns>
    public (bool Acquired, long FencingToken) TryAcquire(
        string resource, string ownerId, TimeSpan timeout)
    {
        // 檢查是否已存在未過期的鎖
        if (_locks.TryGetValue(resource, out var existing))
        {
            if (!existing.IsExpired)
            {
                // 同一擁有者可重入（刷新 TTL）
                if (existing.OwnerId == ownerId)
                {
                    var refreshed = new LockEntry
                    {
                        Resource = resource,
                        OwnerId = ownerId,
                        FencingToken = existing.FencingToken,
                        AcquiredAt = existing.AcquiredAt,
                        ExpiresAt = DateTime.UtcNow.Add(timeout)
                    };
                    _locks[resource] = refreshed;
                    return (true, existing.FencingToken);
                }

                // 其他擁有者持有中 → 拒絕
                return (false, 0);
            }
            // 已過期 → 可搶佔
        }

        // 發放新 fencing token
        var token = Interlocked.Increment(ref _fencingCounter);

        var entry = new LockEntry
        {
            Resource = resource,
            OwnerId = ownerId,
            FencingToken = token,
            AcquiredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(timeout)
        };

        _locks[resource] = entry;
        return (true, token);
    }

    /// <summary>
    /// 釋放鎖
    /// </summary>
    /// <param name="resource">資源名稱</param>
    /// <param name="ownerId">擁有者 ID（必須與取得時一致）</param>
    /// <param name="fencingToken">Fencing token（可選驗證）</param>
    /// <returns>是否成功釋放</returns>
    public bool Release(string resource, string ownerId, long fencingToken = 0)
    {
        if (!_locks.TryGetValue(resource, out var entry))
            return false;

        // 只有擁有者可以釋放
        if (entry.OwnerId != ownerId)
            return false;

        // 如果提供了 fencing token，也需匹配
        if (fencingToken > 0 && entry.FencingToken != fencingToken)
            return false;

        return _locks.TryRemove(resource, out _);
    }

    /// <summary>
    /// 查詢鎖狀態
    /// </summary>
    public (bool IsLocked, string? OwnerId, long FencingToken, DateTime? ExpiresAt) GetLockInfo(string resource)
    {
        if (_locks.TryGetValue(resource, out var entry))
        {
            if (!entry.IsExpired)
                return (true, entry.OwnerId, entry.FencingToken, entry.ExpiresAt);
        }

        return (false, null, 0, null);
    }

    /// <summary>當前持有的鎖數量</summary>
    public int ActiveLockCount => _locks.Count(kv => !kv.Value.IsExpired);

    /// <summary>清理過期鎖</summary>
    private void CleanupExpiredLocks(object? state)
    {
        var expired = _locks.Where(kv => kv.Value.IsExpired).Select(kv => kv.Key).ToList();
        foreach (var key in expired)
        {
            _locks.TryRemove(key, out _);
        }
    }

    /// <summary>強制清除所有鎖（用於 Leader 降級時）</summary>
    public void ClearAll()
    {
        _locks.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();
    }
}
