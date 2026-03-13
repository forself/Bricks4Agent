namespace CacheClient;

/// <summary>
/// 快取降級裝飾器 — Cache miss 或 Cache 不可用時自動降級到 DB
///
/// 用途：
/// 包裝 IDistributedCache，當快取操作失敗時，
/// 回傳 default 值或不拋異常，讓呼叫端（broker 服務）
/// 可以透過 DB fallback 繼續運作。
///
/// 設計原則：
/// - 快取不可用 ≠ 系統不可用
/// - 所有操作都有 try-catch，不讓快取異常傳播到 broker
/// - 提供 OnFallback 事件，讓 broker 決定是否走 DB 路徑
/// </summary>
public class FallbackDecorator : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private volatile bool _circuitOpen;
    private DateTime _circuitOpenedAt;
    private static readonly TimeSpan CircuitResetInterval = TimeSpan.FromSeconds(30);

    /// <summary>降級回呼（快取操作失敗時通知）</summary>
    public event Action<string, Exception>? OnFallback;

    public FallbackDecorator(IDistributedCache inner)
    {
        _inner = inner;
    }

    /// <summary>快取是否可用（false = 應走 DB 路徑）</summary>
    public bool IsAvailable => !_circuitOpen ||
        (DateTime.UtcNow - _circuitOpenedAt) > CircuitResetInterval;

    // ── 基本 KV ──

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (!IsAvailable) return default;

        try
        {
            return await _inner.GetAsync<T>(key, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(GetAsync), ex);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (!IsAvailable) return;

        try
        {
            await _inner.SetAsync(key, value, ttl, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(SetAsync), ex);
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!IsAvailable) return false;

        try
        {
            return await _inner.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(DeleteAsync), ex);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        if (!IsAvailable) return false;

        try
        {
            return await _inner.ExistsAsync(key, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(ExistsAsync), ex);
            return false;
        }
    }

    public async Task<long> IncrementAsync(string key, long delta = 1, CancellationToken ct = default)
    {
        if (!IsAvailable) throw new CacheUnavailableException("Cache circuit open");

        try
        {
            return await _inner.IncrementAsync(key, delta, ct);
        }
        catch (CacheUnavailableException) { throw; }
        catch (Exception ex)
        {
            OpenCircuit(nameof(IncrementAsync), ex);
            throw new CacheUnavailableException("Cache unavailable", ex);
        }
    }

    // ── 原子 CAS 操作 ──

    public async Task<CasResult> CasIfGreaterAsync(
        string key, long newValue, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        if (!IsAvailable) throw new CacheUnavailableException("Cache circuit open");

        try
        {
            return await _inner.CasIfGreaterAsync(key, newValue, ttl, ct);
        }
        catch (CacheUnavailableException) { throw; }
        catch (Exception ex)
        {
            OpenCircuit(nameof(CasIfGreaterAsync), ex);
            throw new CacheUnavailableException("Cache unavailable", ex);
        }
    }

    public async Task<DecrResult> DecrIfPositiveAsync(string key, CancellationToken ct = default)
    {
        if (!IsAvailable) throw new CacheUnavailableException("Cache circuit open");

        try
        {
            return await _inner.DecrIfPositiveAsync(key, ct);
        }
        catch (CacheUnavailableException) { throw; }
        catch (Exception ex)
        {
            OpenCircuit(nameof(DecrIfPositiveAsync), ex);
            throw new CacheUnavailableException("Cache unavailable", ex);
        }
    }

    // ── 分散式鎖 ──

    public async Task<IDistributedLock?> TryAcquireLockAsync(
        string resource, string ownerId, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!IsAvailable) return null;

        try
        {
            return await _inner.TryAcquireLockAsync(resource, ownerId, timeout, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(TryAcquireLockAsync), ex);
            return null;
        }
    }

    // ── Pub/Sub ──

    public async Task PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        if (!IsAvailable) return;

        try
        {
            await _inner.PublishAsync(channel, message, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(PublishAsync), ex);
        }
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string channel, Func<string, string, Task> handler, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new NoOpSubscription();

        try
        {
            return await _inner.SubscribeAsync(channel, handler, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(SubscribeAsync), ex);
            return new NoOpSubscription();
        }
    }

    // ── 強一致讀取 ──

    public async Task<T?> GetStrongAsync<T>(string key, CancellationToken ct = default)
    {
        if (!IsAvailable) return default;

        try
        {
            return await _inner.GetStrongAsync<T>(key, ct);
        }
        catch (Exception ex)
        {
            OpenCircuit(nameof(GetStrongAsync), ex);
            return default;
        }
    }

    // ── Circuit Breaker ──

    private void OpenCircuit(string operation, Exception ex)
    {
        _circuitOpen = true;
        _circuitOpenedAt = DateTime.UtcNow;
        OnFallback?.Invoke(operation, ex);
    }

    /// <summary>強制重置 circuit（用於管理員操作）</summary>
    public void ResetCircuit()
    {
        _circuitOpen = false;
    }

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
    }

    /// <summary>空訂閱句柄</summary>
    private class NoOpSubscription : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>快取不可用異常（circuit breaker 開啟或連線失敗）</summary>
public class CacheUnavailableException : Exception
{
    public CacheUnavailableException(string message) : base(message) { }
    public CacheUnavailableException(string message, Exception inner) : base(message, inner) { }
}
