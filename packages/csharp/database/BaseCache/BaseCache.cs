/**
 * BaseCache - 輕量級記憶體快取
 *
 * 類似 Redis 的精簡版，零外部依賴
 *
 * 功能：
 * - Key-Value 快取 (含 TTL 過期)
 * - Queue 佇列 (FIFO)
 * - Stack 堆疊 (LIFO)
 * - List 列表
 * - Hash 雜湊表
 * - Set 集合
 * - Pub/Sub 發布訂閱
 * - 自動過期清理
 * - JSON 持久化
 *
 * 用法：
 *   var cache = new BaseCache();
 *   cache.Set("key", value, TimeSpan.FromMinutes(30));
 *   var value = cache.Get<T>("key");
 */

using System.Collections.Concurrent;
using System.Text.Json;

namespace BaseCache;

#region Core Cache

/// <summary>
/// 輕量級記憶體快取
/// </summary>
public class BaseCache : IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<object>> _queues = new();
    private readonly ConcurrentDictionary<string, ConcurrentStack<object>> _stacks = new();
    private readonly ConcurrentDictionary<string, List<object>> _lists = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> _hashes = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _sets = new();
    private readonly ConcurrentDictionary<string, List<Action<string, object?>>> _subscribers = new();

    private readonly object _listLock = new();
    private readonly object _setLock = new();

    private readonly Timer _cleanupTimer;
    private readonly CachOptions _options;
    private bool _disposed;

    /// <summary>
    /// 快取統計資訊
    /// </summary>
    public CacheStats Stats { get; } = new();

    #region Constructor

    public BaseCache() : this(new CachOptions()) { }

    public BaseCache(CachOptions options)
    {
        _options = options ?? new CachOptions();

        // 啟動過期清理計時器
        _cleanupTimer = new Timer(
            CleanupExpiredItems,
            null,
            _options.CleanupInterval,
            _options.CleanupInterval);
    }

    #endregion

    #region Key-Value Operations

    /// <summary>
    /// 設定快取值
    /// </summary>
    public void Set<T>(string key, T value, TimeSpan? ttl = null)
    {
        ValidateKey(key);

        var expiry = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : (DateTime?)null;
        var entry = new CacheEntry(value, expiry);

        _store.AddOrUpdate(key, entry, (_, _) => entry);
        Stats.IncrementWrites();

        // 檢查記憶體上限
        if (_options.MaxItems > 0 && _store.Count > _options.MaxItems)
        {
            EvictOldestItems();
        }
    }

    /// <summary>
    /// 取得快取值
    /// </summary>
    public T? Get<T>(string key)
    {
        Stats.IncrementReads();

        if (!_store.TryGetValue(key, out var entry))
        {
            Stats.IncrementMisses();
            return default;
        }

        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            Stats.IncrementMisses();
            return default;
        }

        Stats.IncrementHits();
        return (T?)entry.Value;
    }

    /// <summary>
    /// 嘗試取得快取值
    /// </summary>
    public bool TryGet<T>(string key, out T? value)
    {
        value = Get<T>(key);
        return value != null;
    }

    /// <summary>
    /// 取得或設定快取值
    /// </summary>
    public T GetOrSet<T>(string key, Func<T> factory, TimeSpan? ttl = null)
    {
        var value = Get<T>(key);
        if (value != null)
            return value;

        value = factory();
        Set(key, value, ttl);
        return value;
    }

    /// <summary>
    /// 刪除快取
    /// </summary>
    public bool Delete(string key)
    {
        return _store.TryRemove(key, out _);
    }

    /// <summary>
    /// 檢查 key 是否存在
    /// </summary>
    public bool Exists(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return false;

        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 設定過期時間
    /// </summary>
    public bool Expire(string key, TimeSpan ttl)
    {
        if (!_store.TryGetValue(key, out var entry))
            return false;

        entry.ExpiresAt = DateTime.UtcNow.Add(ttl);
        return true;
    }

    /// <summary>
    /// 取得剩餘存活時間
    /// </summary>
    public TimeSpan? TTL(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return null;

        if (!entry.ExpiresAt.HasValue)
            return TimeSpan.MaxValue; // 永不過期

        var remaining = entry.ExpiresAt.Value - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : null;
    }

    /// <summary>
    /// 取得所有符合模式的 keys
    /// </summary>
    public IEnumerable<string> Keys(string pattern = "*")
    {
        if (pattern == "*")
            return _store.Keys.Where(k => !IsExpired(k));

        var regex = new System.Text.RegularExpressions.Regex(
            "^" + pattern.Replace("*", ".*").Replace("?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return _store.Keys.Where(k => regex.IsMatch(k) && !IsExpired(k));
    }

    /// <summary>
    /// 數值遞增
    /// </summary>
    public long Increment(string key, long delta = 1)
    {
        var entry = _store.AddOrUpdate(
            key,
            _ => new CacheEntry(delta, null),
            (_, existing) =>
            {
                var current = Convert.ToInt64(existing.Value ?? 0);
                existing.Value = current + delta;
                return existing;
            });

        return Convert.ToInt64(entry.Value);
    }

    /// <summary>
    /// 數值遞減
    /// </summary>
    public long Decrement(string key, long delta = 1) => Increment(key, -delta);

    #endregion

    #region Queue Operations (FIFO)

    /// <summary>
    /// 入列
    /// </summary>
    public void Enqueue<T>(string key, T item)
    {
        ValidateKey(key);
        var queue = _queues.GetOrAdd(key, _ => new ConcurrentQueue<object>());
        queue.Enqueue(item!);
    }

    /// <summary>
    /// 出列
    /// </summary>
    public T? Dequeue<T>(string key)
    {
        if (!_queues.TryGetValue(key, out var queue))
            return default;

        return queue.TryDequeue(out var item) ? (T?)item : default;
    }

    /// <summary>
    /// 查看隊首 (不移除)
    /// </summary>
    public T? QueuePeek<T>(string key)
    {
        if (!_queues.TryGetValue(key, out var queue))
            return default;

        return queue.TryPeek(out var item) ? (T?)item : default;
    }

    /// <summary>
    /// 取得佇列長度
    /// </summary>
    public int QueueLength(string key)
    {
        return _queues.TryGetValue(key, out var queue) ? queue.Count : 0;
    }

    #endregion

    #region Stack Operations (LIFO)

    /// <summary>
    /// 推入堆疊
    /// </summary>
    public void Push<T>(string key, T item)
    {
        ValidateKey(key);
        var stack = _stacks.GetOrAdd(key, _ => new ConcurrentStack<object>());
        stack.Push(item!);
    }

    /// <summary>
    /// 彈出堆疊
    /// </summary>
    public T? Pop<T>(string key)
    {
        if (!_stacks.TryGetValue(key, out var stack))
            return default;

        return stack.TryPop(out var item) ? (T?)item : default;
    }

    /// <summary>
    /// 查看棧頂 (不移除)
    /// </summary>
    public T? StackPeek<T>(string key)
    {
        if (!_stacks.TryGetValue(key, out var stack))
            return default;

        return stack.TryPeek(out var item) ? (T?)item : default;
    }

    /// <summary>
    /// 取得堆疊長度
    /// </summary>
    public int StackLength(string key)
    {
        return _stacks.TryGetValue(key, out var stack) ? stack.Count : 0;
    }

    #endregion

    #region List Operations

    /// <summary>
    /// 左側插入
    /// </summary>
    public int LPush<T>(string key, T item)
    {
        ValidateKey(key);
        lock (_listLock)
        {
            var list = GetOrCreateList(key);
            list.Insert(0, item!);
            return list.Count;
        }
    }

    /// <summary>
    /// 右側插入
    /// </summary>
    public int RPush<T>(string key, T item)
    {
        ValidateKey(key);
        lock (_listLock)
        {
            var list = GetOrCreateList(key);
            list.Add(item!);
            return list.Count;
        }
    }

    /// <summary>
    /// 左側彈出
    /// </summary>
    public T? LPop<T>(string key)
    {
        lock (_listLock)
        {
            if (!_lists.TryGetValue(key, out var list) || list.Count == 0)
                return default;

            var item = list[0];
            list.RemoveAt(0);
            return (T?)item;
        }
    }

    /// <summary>
    /// 右側彈出
    /// </summary>
    public T? RPop<T>(string key)
    {
        lock (_listLock)
        {
            if (!_lists.TryGetValue(key, out var list) || list.Count == 0)
                return default;

            var item = list[^1];
            list.RemoveAt(list.Count - 1);
            return (T?)item;
        }
    }

    /// <summary>
    /// 取得範圍元素
    /// </summary>
    public List<T> LRange<T>(string key, int start, int stop)
    {
        lock (_listLock)
        {
            if (!_lists.TryGetValue(key, out var list))
                return new List<T>();

            // 處理負數索引
            if (start < 0) start = Math.Max(0, list.Count + start);
            if (stop < 0) stop = list.Count + stop;
            stop = Math.Min(stop, list.Count - 1);

            if (start > stop || start >= list.Count)
                return new List<T>();

            return list.Skip(start).Take(stop - start + 1).Cast<T>().ToList();
        }
    }

    /// <summary>
    /// 取得列表長度
    /// </summary>
    public int LLen(string key)
    {
        lock (_listLock)
        {
            return _lists.TryGetValue(key, out var list) ? list.Count : 0;
        }
    }

    /// <summary>
    /// 依索引取得元素
    /// </summary>
    public T? LIndex<T>(string key, int index)
    {
        lock (_listLock)
        {
            if (!_lists.TryGetValue(key, out var list))
                return default;

            if (index < 0) index = list.Count + index;
            if (index < 0 || index >= list.Count)
                return default;

            return (T?)list[index];
        }
    }

    private List<object> GetOrCreateList(string key)
    {
        if (!_lists.TryGetValue(key, out var list))
        {
            list = new List<object>();
            _lists[key] = list;
        }
        return list;
    }

    #endregion

    #region Hash Operations

    /// <summary>
    /// 設定雜湊欄位
    /// </summary>
    public void HSet<T>(string key, string field, T value)
    {
        ValidateKey(key);
        ValidateKey(field);
        var hash = _hashes.GetOrAdd(key, _ => new ConcurrentDictionary<string, object>());
        hash[field] = value!;
    }

    /// <summary>
    /// 取得雜湊欄位
    /// </summary>
    public T? HGet<T>(string key, string field)
    {
        if (!_hashes.TryGetValue(key, out var hash))
            return default;

        return hash.TryGetValue(field, out var value) ? (T?)value : default;
    }

    /// <summary>
    /// 取得所有雜湊欄位
    /// </summary>
    public Dictionary<string, T> HGetAll<T>(string key)
    {
        if (!_hashes.TryGetValue(key, out var hash))
            return new Dictionary<string, T>();

        return hash.ToDictionary(kvp => kvp.Key, kvp => (T)kvp.Value);
    }

    /// <summary>
    /// 刪除雜湊欄位
    /// </summary>
    public bool HDel(string key, string field)
    {
        if (!_hashes.TryGetValue(key, out var hash))
            return false;

        return hash.TryRemove(field, out _);
    }

    /// <summary>
    /// 檢查雜湊欄位是否存在
    /// </summary>
    public bool HExists(string key, string field)
    {
        if (!_hashes.TryGetValue(key, out var hash))
            return false;

        return hash.ContainsKey(field);
    }

    /// <summary>
    /// 取得所有雜湊欄位名稱
    /// </summary>
    public IEnumerable<string> HKeys(string key)
    {
        if (!_hashes.TryGetValue(key, out var hash))
            return Enumerable.Empty<string>();

        return hash.Keys;
    }

    /// <summary>
    /// 取得雜湊欄位數量
    /// </summary>
    public int HLen(string key)
    {
        return _hashes.TryGetValue(key, out var hash) ? hash.Count : 0;
    }

    /// <summary>
    /// 雜湊欄位數值遞增
    /// </summary>
    public long HIncr(string key, string field, long delta = 1)
    {
        var hash = _hashes.GetOrAdd(key, _ => new ConcurrentDictionary<string, object>());
        var newValue = hash.AddOrUpdate(
            field,
            delta,
            (_, existing) => Convert.ToInt64(existing) + delta);

        return Convert.ToInt64(newValue);
    }

    #endregion

    #region Set Operations

    /// <summary>
    /// 新增集合成員
    /// </summary>
    public bool SAdd(string key, string member)
    {
        ValidateKey(key);
        ValidateKey(member);
        lock (_setLock)
        {
            var set = GetOrCreateSet(key);
            return set.Add(member);
        }
    }

    /// <summary>
    /// 移除集合成員
    /// </summary>
    public bool SRemove(string key, string member)
    {
        lock (_setLock)
        {
            if (!_sets.TryGetValue(key, out var set))
                return false;

            return set.Remove(member);
        }
    }

    /// <summary>
    /// 取得所有集合成員
    /// </summary>
    public HashSet<string> SMembers(string key)
    {
        lock (_setLock)
        {
            if (!_sets.TryGetValue(key, out var set))
                return new HashSet<string>();

            return new HashSet<string>(set);
        }
    }

    /// <summary>
    /// 檢查是否為集合成員
    /// </summary>
    public bool SIsMember(string key, string member)
    {
        lock (_setLock)
        {
            if (!_sets.TryGetValue(key, out var set))
                return false;

            return set.Contains(member);
        }
    }

    /// <summary>
    /// 取得集合大小
    /// </summary>
    public int SCard(string key)
    {
        lock (_setLock)
        {
            return _sets.TryGetValue(key, out var set) ? set.Count : 0;
        }
    }

    /// <summary>
    /// 集合交集
    /// </summary>
    public HashSet<string> SIntersect(params string[] keys)
    {
        lock (_setLock)
        {
            if (keys.Length == 0) return new HashSet<string>();

            HashSet<string>? result = null;
            foreach (var key in keys)
            {
                if (!_sets.TryGetValue(key, out var set))
                    return new HashSet<string>();

                if (result == null)
                    result = new HashSet<string>(set);
                else
                    result.IntersectWith(set);
            }

            return result ?? new HashSet<string>();
        }
    }

    /// <summary>
    /// 集合聯集
    /// </summary>
    public HashSet<string> SUnion(params string[] keys)
    {
        lock (_setLock)
        {
            var result = new HashSet<string>();
            foreach (var key in keys)
            {
                if (_sets.TryGetValue(key, out var set))
                    result.UnionWith(set);
            }
            return result;
        }
    }

    private HashSet<string> GetOrCreateSet(string key)
    {
        if (!_sets.TryGetValue(key, out var set))
        {
            set = new HashSet<string>();
            _sets[key] = set;
        }
        return set;
    }

    #endregion

    #region Pub/Sub

    /// <summary>
    /// 訂閱頻道
    /// </summary>
    public void Subscribe(string channel, Action<string, object?> handler)
    {
        var handlers = _subscribers.GetOrAdd(channel, _ => new List<Action<string, object?>>());
        lock (handlers)
        {
            handlers.Add(handler);
        }
    }

    /// <summary>
    /// 取消訂閱
    /// </summary>
    public void Unsubscribe(string channel, Action<string, object?>? handler = null)
    {
        if (!_subscribers.TryGetValue(channel, out var handlers))
            return;

        lock (handlers)
        {
            if (handler == null)
                handlers.Clear();
            else
                handlers.Remove(handler);
        }
    }

    /// <summary>
    /// 發布訊息
    /// </summary>
    public int Publish(string channel, object? message)
    {
        if (!_subscribers.TryGetValue(channel, out var handlers))
            return 0;

        List<Action<string, object?>> handlersCopy;
        lock (handlers)
        {
            handlersCopy = handlers.ToList();
        }

        foreach (var handler in handlersCopy)
        {
            try
            {
                handler(channel, message);
            }
            catch
            {
                // 忽略訂閱者的錯誤
            }
        }

        return handlersCopy.Count;
    }

    #endregion

    #region Persistence

    /// <summary>
    /// 儲存到檔案
    /// </summary>
    public void SaveToFile(string filePath)
    {
        ValidateFilePath(filePath);

        var snapshot = new CacheSnapshot
        {
            Store = _store.Where(kvp => !kvp.Value.IsExpired)
                          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            Hashes = _hashes.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary(h => h.Key, h => h.Value)),
            Sets = _sets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList()),
            SavedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 從檔案載入
    /// </summary>
    public void LoadFromFile(string filePath)
    {
        ValidateFilePath(filePath);

        if (!File.Exists(filePath))
            return;

        var json = File.ReadAllText(filePath);
        var snapshot = JsonSerializer.Deserialize<CacheSnapshot>(json);

        if (snapshot == null)
            return;

        // 還原 Key-Value
        foreach (var kvp in snapshot.Store)
        {
            if (!kvp.Value.IsExpired)
                _store[kvp.Key] = kvp.Value;
        }

        // 還原 Hashes
        foreach (var hash in snapshot.Hashes)
        {
            var dict = new ConcurrentDictionary<string, object>();
            foreach (var field in hash.Value)
                dict[field.Key] = field.Value;
            _hashes[hash.Key] = dict;
        }

        // 還原 Sets
        foreach (var set in snapshot.Sets)
        {
            _sets[set.Key] = new HashSet<string>(set.Value);
        }
    }

    #endregion

    #region Maintenance

    /// <summary>
    /// 清除所有快取
    /// </summary>
    public void Clear()
    {
        _store.Clear();
        _queues.Clear();
        _stacks.Clear();
        _lists.Clear();
        _hashes.Clear();
        _sets.Clear();
        Stats.Reset();
    }

    /// <summary>
    /// 取得快取項目數量
    /// </summary>
    public int Count => _store.Count;

    private void CleanupExpiredItems(object? state)
    {
        var expiredKeys = _store
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _store.TryRemove(key, out _);
            Stats.IncrementEvictions();
        }
    }

    private void EvictOldestItems()
    {
        var itemsToRemove = _store
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .Take(_store.Count - _options.MaxItems + _options.EvictionCount)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in itemsToRemove)
        {
            _store.TryRemove(key, out _);
            Stats.IncrementEvictions();
        }
    }

    private bool IsExpired(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return true;

        if (entry.IsExpired)
        {
            _store.TryRemove(key, out _);
            return true;
        }

        return false;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (key.Length > 1024)
            throw new ArgumentException("Key length cannot exceed 1024 characters", nameof(key));

        // 禁止特殊控制字元
        foreach (var c in key)
        {
            if (char.IsControl(c))
                throw new ArgumentException("Key cannot contain control characters", nameof(key));
        }
    }

    private static void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        // 取得完整路徑並檢查是否嘗試目錄遍歷
        var fullPath = Path.GetFullPath(filePath);

        // 檢查是否包含危險路徑
        if (fullPath.Contains(".."))
            throw new ArgumentException("Path traversal is not allowed", nameof(filePath));

        // 檢查副檔名
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".json" && ext != ".cache")
            throw new ArgumentException("Only .json and .cache extensions are allowed", nameof(filePath));
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer.Dispose();
        Clear();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    #endregion
}

#endregion

#region Supporting Classes

/// <summary>
/// 快取項目
/// </summary>
public class CacheEntry
{
    public object? Value { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public CacheEntry() { }

    public CacheEntry(object? value, DateTime? expiresAt)
    {
        Value = value;
        ExpiresAt = expiresAt;
        CreatedAt = DateTime.UtcNow;
    }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
}

/// <summary>
/// 快取選項
/// </summary>
public class CachOptions
{
    /// <summary>
    /// 過期清理間隔 (預設 1 分鐘)
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最大項目數 (0 = 無限制)
    /// </summary>
    public int MaxItems { get; set; } = 0;

    /// <summary>
    /// 達到上限時移除的項目數
    /// </summary>
    public int EvictionCount { get; set; } = 100;
}

/// <summary>
/// 快取統計
/// </summary>
public class CacheStats
{
    private long _hits;
    private long _misses;
    private long _reads;
    private long _writes;
    private long _evictions;

    public long Hits => _hits;
    public long Misses => _misses;
    public long Reads => _reads;
    public long Writes => _writes;
    public long Evictions => _evictions;

    public double HitRate => _reads > 0 ? (double)_hits / _reads * 100 : 0;

    internal void IncrementHits() => Interlocked.Increment(ref _hits);
    internal void IncrementMisses() => Interlocked.Increment(ref _misses);
    internal void IncrementReads() => Interlocked.Increment(ref _reads);
    internal void IncrementWrites() => Interlocked.Increment(ref _writes);
    internal void IncrementEvictions() => Interlocked.Increment(ref _evictions);

    internal void Reset()
    {
        _hits = _misses = _reads = _writes = _evictions = 0;
    }

    public override string ToString() =>
        $"Hits: {Hits}, Misses: {Misses}, HitRate: {HitRate:F2}%, Reads: {Reads}, Writes: {Writes}, Evictions: {Evictions}";
}

/// <summary>
/// 快取快照 (用於持久化)
/// </summary>
internal class CacheSnapshot
{
    public Dictionary<string, CacheEntry> Store { get; set; } = new();
    public Dictionary<string, Dictionary<string, object>> Hashes { get; set; } = new();
    public Dictionary<string, List<string>> Sets { get; set; } = new();
    public DateTime SavedAt { get; set; }
}

#endregion

#region Extensions

/// <summary>
/// 快取擴充方法
/// </summary>
public static class BaseCacheExtensions
{
    /// <summary>
    /// 批次設定
    /// </summary>
    public static void SetMany<T>(this BaseCache cache, IDictionary<string, T> items, TimeSpan? ttl = null)
    {
        foreach (var kvp in items)
        {
            cache.Set(kvp.Key, kvp.Value, ttl);
        }
    }

    /// <summary>
    /// 批次取得
    /// </summary>
    public static Dictionary<string, T?> GetMany<T>(this BaseCache cache, IEnumerable<string> keys)
    {
        return keys.ToDictionary(key => key, key => cache.Get<T>(key));
    }

    /// <summary>
    /// 批次刪除
    /// </summary>
    public static int DeleteMany(this BaseCache cache, IEnumerable<string> keys)
    {
        return keys.Count(key => cache.Delete(key));
    }

    /// <summary>
    /// 依模式刪除
    /// </summary>
    public static int DeleteByPattern(this BaseCache cache, string pattern)
    {
        var keys = cache.Keys(pattern).ToList();
        return cache.DeleteMany(keys);
    }
}

#endregion
