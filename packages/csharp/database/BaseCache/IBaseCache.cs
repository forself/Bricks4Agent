namespace BaseCache;

/// <summary>
/// BaseCache 介面抽取 — 啟用 DI 注入與測試替身
///
/// Phase 2: 分散式快取服務的核心引擎介面
/// </summary>
public interface IBaseCache : IDisposable
{
    // ── Key-Value ──

    void Set<T>(string key, T value, TimeSpan? ttl = null);
    T? Get<T>(string key);
    bool TryGet<T>(string key, out T? value);
    T GetOrSet<T>(string key, Func<T> factory, TimeSpan? ttl = null);
    bool Delete(string key);
    bool Exists(string key);
    bool Expire(string key, TimeSpan ttl);
    TimeSpan? TTL(string key);
    IEnumerable<string> Keys(string pattern = "*");
    long Increment(string key, long delta = 1);
    long Decrement(string key, long delta = 1);

    // ── Queue (FIFO) ──

    void Enqueue<T>(string key, T item);
    T? Dequeue<T>(string key);
    T? QueuePeek<T>(string key);
    int QueueLength(string key);

    // ── Stack (LIFO) ──

    void Push<T>(string key, T item);
    T? Pop<T>(string key);
    T? StackPeek<T>(string key);
    int StackLength(string key);

    // ── List ──

    int LPush<T>(string key, T item);
    int RPush<T>(string key, T item);
    T? LPop<T>(string key);
    T? RPop<T>(string key);
    List<T> LRange<T>(string key, int start, int stop);
    int LLen(string key);
    T? LIndex<T>(string key, int index);

    // ── Hash ──

    void HSet<T>(string key, string field, T value);
    T? HGet<T>(string key, string field);
    Dictionary<string, T> HGetAll<T>(string key);
    bool HDel(string key, string field);
    bool HExists(string key, string field);
    IEnumerable<string> HKeys(string key);
    int HLen(string key);
    long HIncr(string key, string field, long delta = 1);

    // ── Set ──

    bool SAdd(string key, string member);
    bool SRemove(string key, string member);
    HashSet<string> SMembers(string key);
    bool SIsMember(string key, string member);
    int SCard(string key);
    HashSet<string> SIntersect(params string[] keys);
    HashSet<string> SUnion(params string[] keys);

    // ── Pub/Sub ──

    void Subscribe(string channel, Action<string, object?> handler);
    void Unsubscribe(string channel, Action<string, object?>? handler = null);
    int Publish(string channel, object? message);

    // ── Persistence ──

    void SaveToFile(string filePath);
    void LoadFromFile(string filePath);

    // ── Maintenance ──

    void Clear();
    int Count { get; }
    CacheStats Stats { get; }
}
