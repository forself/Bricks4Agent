using System.Text.Json;
using CacheProtocol;
using Microsoft.Extensions.Logging;

namespace CacheServer.Engine;

/// <summary>
/// 快取引擎 — 封裝 BaseCache + CAS + Lock 操作
///
/// 統一入口，所有 OpCode 操作都透過此類執行。
/// 包含：
/// - 基本 KV 操作（GET/SET/DEL/EXISTS/EXPIRE/INCR）
/// - 原子 CAS 操作（CAS/CAS_GT/DECR_POS）
/// - 分散式鎖（LOCK/UNLOCK）
/// - Pub/Sub（PUBLISH/SUBSCRIBE/UNSUBSCRIBE）
///
/// 此類在 Leader 和 Follower 上都運行：
/// - Leader：接受所有操作
/// - Follower：僅接受讀取操作（寫入由 ReplicationReceiver 中繼）
/// </summary>
public class CacheEngine : IDisposable
{
    private readonly BaseCache.IBaseCache _cache;
    private readonly CompareAndSwap _cas;
    private readonly DistributedLockMgr _lockMgr;
    private readonly ILogger<CacheEngine> _logger;
    private bool _disposed;

    // 用於 Pub/Sub：channel → 訂閱者回呼清單
    // 由 ClientSession 管理訂閱/取消
    private readonly object _subLock = new();
    private readonly Dictionary<string, List<Action<string, string>>> _subscriptions = new();

    public CacheEngine(
        BaseCache.IBaseCache cache,
        ILogger<CacheEngine> logger)
    {
        _cache = cache;
        _cas = new CompareAndSwap();
        _lockMgr = new DistributedLockMgr();
        _logger = logger;
    }

    // ── 基本 KV 操作 ──

    /// <summary>GET — 讀取鍵值</summary>
    public CacheResponse Get(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        if (_cache.TryGet<JsonElement>(cmd.Key, out var value))
        {
            return CacheResponse.WithValue(cmd.Id, value);
        }

        // key 不存在：回傳 ok=true 但 value=null（區分 miss vs error）
        return CacheResponse.WithValue(cmd.Id, null);
    }

    /// <summary>SET — 寫入鍵值（含 TTL）</summary>
    public CacheResponse Set(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        if (cmd.Value == null)
            return CacheResponse.Fail(cmd.Id, "Value is required");

        var ttl = cmd.TtlMs > 0 ? TimeSpan.FromMilliseconds(cmd.TtlMs) : (TimeSpan?)null;
        _cache.Set(cmd.Key, cmd.Value.Value, ttl);
        return CacheResponse.Success(cmd.Id);
    }

    /// <summary>DEL — 刪除鍵</summary>
    public CacheResponse Delete(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        var deleted = _cache.Delete(cmd.Key);
        if (deleted)
            _cas.RemoveLock(cmd.Key);

        return new CacheResponse { Id = cmd.Id, Ok = true, Swapped = deleted };
    }

    /// <summary>EXISTS — 檢查鍵是否存在</summary>
    public CacheResponse Exists(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        var exists = _cache.Exists(cmd.Key);
        return new CacheResponse { Id = cmd.Id, Ok = true, Exists = exists };
    }

    /// <summary>EXPIRE — 設定 TTL</summary>
    public CacheResponse Expire(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        var ttl = TimeSpan.FromSeconds(cmd.ExpireSeconds);
        var result = _cache.Expire(cmd.Key, ttl);
        return new CacheResponse { Id = cmd.Id, Ok = result };
    }

    /// <summary>INCR — 原子遞增</summary>
    public CacheResponse Increment(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        var delta = cmd.Delta != 0 ? cmd.Delta : 1;
        var newValue = _cache.Increment(cmd.Key, delta);
        return CacheResponse.WithNumber(cmd.Id, newValue);
    }

    // ── 原子 CAS 操作 ──

    /// <summary>
    /// CAS_GT — Compare-And-Swap-If-Greater
    /// Replay seq 專用：只接受 newValue > current
    /// </summary>
    public CacheResponse CasIfGreater(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        var ttl = cmd.TtlMs > 0 ? TimeSpan.FromMilliseconds(cmd.TtlMs) : (TimeSpan?)null;
        var (swapped, currentValue) = _cas.CasIfGreater(_cache, cmd.Key, cmd.NewValue, ttl);

        return new CacheResponse
        {
            Id = cmd.Id,
            Ok = true,
            Swapped = swapped,
            NumValue = currentValue
        };
    }

    /// <summary>
    /// DECR_POS — Decrement-If-Positive
    /// Quota 專用：value > 0 才遞減
    /// </summary>
    public CacheResponse DecrIfPositive(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        var (success, newValue) = _cas.DecrIfPositive(_cache, cmd.Key);

        return new CacheResponse
        {
            Id = cmd.Id,
            Ok = true,
            Swapped = success,
            NumValue = newValue
        };
    }

    /// <summary>
    /// CAS — Compare-And-Swap（精確比較）
    /// </summary>
    public CacheResponse CompareAndSwap(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Key))
            return CacheResponse.Fail(cmd.Id, "Key is required");

        // 使用 long 型別進行 CAS（broker 場景主要是 long）
        var expected = cmd.Threshold; // reuse threshold field for expected
        var newVal = cmd.NewValue;
        var ttl = cmd.TtlMs > 0 ? TimeSpan.FromMilliseconds(cmd.TtlMs) : (TimeSpan?)null;
        var swapped = _cas.CompareAndSet(_cache, cmd.Key, expected, newVal, ttl);

        return CacheResponse.WithSwap(cmd.Id, swapped);
    }

    // ── 分散式鎖 ──

    /// <summary>LOCK — 嘗試取得鎖</summary>
    public CacheResponse Lock(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Resource))
            return CacheResponse.Fail(cmd.Id, "Resource is required");
        if (string.IsNullOrEmpty(cmd.OwnerId))
            return CacheResponse.Fail(cmd.Id, "OwnerId is required");

        var timeout = cmd.TimeoutMs > 0
            ? TimeSpan.FromMilliseconds(cmd.TimeoutMs)
            : TimeSpan.FromSeconds(30); // 預設 30 秒

        var (acquired, fencingToken) = _lockMgr.TryAcquire(cmd.Resource, cmd.OwnerId, timeout);

        return CacheResponse.WithLock(cmd.Id, acquired, fencingToken);
    }

    /// <summary>UNLOCK — 釋放鎖</summary>
    public CacheResponse Unlock(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Resource))
            return CacheResponse.Fail(cmd.Id, "Resource is required");
        if (string.IsNullOrEmpty(cmd.OwnerId))
            return CacheResponse.Fail(cmd.Id, "OwnerId is required");

        var released = _lockMgr.Release(cmd.Resource, cmd.OwnerId, cmd.FencingToken);

        return new CacheResponse { Id = cmd.Id, Ok = released };
    }

    // ── Pub/Sub ──

    /// <summary>PUBLISH — 發布訊息到頻道</summary>
    public CacheResponse PublishMessage(CacheCommand cmd)
    {
        if (string.IsNullOrEmpty(cmd.Channel))
            return CacheResponse.Fail(cmd.Id, "Channel is required");

        var message = cmd.Message ?? "";
        int delivered = 0;

        lock (_subLock)
        {
            if (_subscriptions.TryGetValue(cmd.Channel, out var handlers))
            {
                foreach (var handler in handlers.ToList())
                {
                    try
                    {
                        handler(cmd.Channel, message);
                        delivered++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Pub/Sub handler error: channel={Channel}", cmd.Channel);
                    }
                }
            }
        }

        return CacheResponse.WithNumber(cmd.Id, delivered);
    }

    /// <summary>註冊頻道訂閱</summary>
    public void Subscribe(string channel, Action<string, string> handler)
    {
        lock (_subLock)
        {
            if (!_subscriptions.TryGetValue(channel, out var handlers))
            {
                handlers = new List<Action<string, string>>();
                _subscriptions[channel] = handlers;
            }
            handlers.Add(handler);
        }
    }

    /// <summary>取消頻道訂閱</summary>
    public void Unsubscribe(string channel, Action<string, string>? handler = null)
    {
        lock (_subLock)
        {
            if (!_subscriptions.TryGetValue(channel, out var handlers))
                return;

            if (handler != null)
                handlers.Remove(handler);
            else
                handlers.Clear();

            if (handlers.Count == 0)
                _subscriptions.Remove(channel);
        }
    }

    // ── 直接操作（供複製引擎使用，繞過 CacheCommand 解析） ──

    /// <summary>直接寫入（複製引擎用）</summary>
    public void DirectSet(string key, JsonElement value, TimeSpan? ttl = null)
    {
        _cache.Set(key, value, ttl);
    }

    /// <summary>直接刪除（複製引擎用）</summary>
    public bool DirectDelete(string key)
    {
        _cas.RemoveLock(key);
        return _cache.Delete(key);
    }

    /// <summary>直接 CAS_GT（複製引擎用）</summary>
    public (bool Swapped, long CurrentValue) DirectCasIfGreater(
        string key, long newValue, TimeSpan? ttl = null)
    {
        return _cas.CasIfGreater(_cache, key, newValue, ttl);
    }

    /// <summary>直接 DECR_POS（複製引擎用）</summary>
    public (bool Success, long NewValue) DirectDecrIfPositive(string key)
    {
        return _cas.DecrIfPositive(_cache, key);
    }

    /// <summary>直接 INCR（複製引擎用）</summary>
    public long DirectIncrement(string key, long delta = 1)
    {
        return _cache.Increment(key, delta);
    }

    /// <summary>直接 EXPIRE（複製引擎用）</summary>
    public bool DirectExpire(string key, TimeSpan ttl)
    {
        return _cache.Expire(key, ttl);
    }

    // ── 狀態查詢 ──

    /// <summary>快取條目數</summary>
    public int Count => _cache.Count;

    /// <summary>快取統計</summary>
    public BaseCache.CacheStats CacheStats => _cache.Stats;

    /// <summary>CAS 鎖數量</summary>
    public int CasLockCount => _cas.LockCount;

    /// <summary>分散式鎖數量</summary>
    public int DistributedLockCount => _lockMgr.ActiveLockCount;

    /// <summary>分散式鎖管理器（供 CommandRouter 查詢）</summary>
    public DistributedLockMgr LockManager => _lockMgr;

    /// <summary>取得所有快取 key（快照用）</summary>
    public IEnumerable<string> GetAllKeys() => _cache.Keys();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lockMgr.Dispose();
    }
}
