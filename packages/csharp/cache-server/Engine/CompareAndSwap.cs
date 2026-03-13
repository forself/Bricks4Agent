using System.Collections.Concurrent;

namespace CacheServer.Engine;

/// <summary>
/// 原子性 Compare-And-Swap 操作
///
/// 提供 per-key 鎖粒度的 CAS 操作：
/// - CAS_GT (Compare-And-Swap-If-Greater)：Replay seq 專用
/// - DECR_POS (Decrement-If-Positive)：Quota 專用
/// - CAS (精確比較後交換)
///
/// 所有操作在 Leader 本地原子執行，不需要外部鎖。
/// </summary>
public class CompareAndSwap
{
    private readonly ConcurrentDictionary<string, object> _keyLocks = new();

    /// <summary>
    /// 取得 per-key 鎖物件
    /// </summary>
    private object GetLock(string key)
    {
        return _keyLocks.GetOrAdd(key, _ => new object());
    }

    /// <summary>
    /// Compare-And-Swap-If-Greater
    ///
    /// 如果 current &lt; newValue，則更新為 newValue。
    /// 用於 replay seq 推進：只接受嚴格遞增的序號。
    /// </summary>
    /// <param name="cache">底層快取</param>
    /// <param name="key">鍵名</param>
    /// <param name="newValue">新值</param>
    /// <param name="ttl">TTL</param>
    /// <returns>(swapped, currentValue) — 是否成功交換 + 交換後的當前值</returns>
    public (bool Swapped, long CurrentValue) CasIfGreater(
        BaseCache.IBaseCache cache, string key, long newValue, TimeSpan? ttl = null)
    {
        lock (GetLock(key))
        {
            var current = cache.TryGet<long>(key, out var val) ? val : 0;

            if (newValue > current)
            {
                cache.Set(key, newValue, ttl);
                return (true, newValue);
            }

            return (false, current);
        }
    }

    /// <summary>
    /// Decrement-If-Positive
    ///
    /// 如果 current > 0，遞減 1 並回傳新值。
    /// 用於 quota 消耗：配額耗盡後拒絕。
    /// </summary>
    /// <param name="cache">底層快取</param>
    /// <param name="key">鍵名</param>
    /// <returns>(success, newValue) — 是否成功遞減 + 遞減後的值</returns>
    public (bool Success, long NewValue) DecrIfPositive(BaseCache.IBaseCache cache, string key)
    {
        lock (GetLock(key))
        {
            var current = cache.TryGet<long>(key, out var val) ? val : 0;

            if (current > 0)
            {
                var newVal = current - 1;
                cache.Set(key, newVal);
                return (true, newVal);
            }

            return (false, current);
        }
    }

    /// <summary>
    /// Compare-And-Swap（精確比較）
    ///
    /// 如果 current == expected，則更新為 newValue。
    /// 通用 CAS 操作。
    /// </summary>
    /// <param name="cache">底層快取</param>
    /// <param name="key">鍵名</param>
    /// <param name="expected">預期的當前值</param>
    /// <param name="newValue">新值</param>
    /// <param name="ttl">TTL</param>
    /// <returns>是否成功交換</returns>
    public bool CompareAndSet<T>(
        BaseCache.IBaseCache cache, string key, T expected, T newValue, TimeSpan? ttl = null)
        where T : IEquatable<T>
    {
        lock (GetLock(key))
        {
            if (cache.TryGet<T>(key, out var current))
            {
                if (current != null && current.Equals(expected))
                {
                    cache.Set(key, newValue, ttl);
                    return true;
                }
                return false;
            }

            // key 不存在，expected 為 default 時可以設定
            if (EqualityComparer<T>.Default.Equals(expected, default!))
            {
                cache.Set(key, newValue, ttl);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 清理已無用的 per-key 鎖（避免記憶體洩漏）
    /// 在快取項過期或刪除時呼叫
    /// </summary>
    public void RemoveLock(string key)
    {
        _keyLocks.TryRemove(key, out _);
    }

    /// <summary>
    /// 當前追蹤的鎖數量（監控用）
    /// </summary>
    public int LockCount => _keyLocks.Count;
}
