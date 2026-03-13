namespace CacheClient;

/// <summary>
/// 分散式快取客戶端介面
///
/// Broker 服務使用此介面存取快取叢集。
/// 支援：
/// - 基本 KV 操作（Get/Set/Delete）
/// - 原子操作（CAS_GT/DECR_POS/INCR）
/// - 分散式鎖（Lock/Unlock）
/// - Pub/Sub（Publish/Subscribe）
/// - 強一致讀取（GetStrong → 強制讀 Leader）
///
/// 路由邏輯：
/// - 讀取操作 → Round-Robin 任意節點
/// - 寫入/CAS/鎖 → Leader 節點
/// - 強讀取 → Leader 節點
/// - REDIRECT → 自動更新 Leader 並重試
/// </summary>
public interface IDistributedCache : IAsyncDisposable
{
    // ── 基本 KV ──

    /// <summary>讀取鍵值（路由到任意節點）</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>寫入鍵值（路由到 Leader）</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>刪除鍵（路由到 Leader）</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>檢查鍵是否存在（路由到任意節點）</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>原子遞增（路由到 Leader）</summary>
    Task<long> IncrementAsync(string key, long delta = 1, CancellationToken ct = default);

    // ── 原子 CAS 操作 ──

    /// <summary>
    /// CAS_GT — Compare-And-Swap-If-Greater
    /// Replay seq 專用：只接受 newValue > current
    /// </summary>
    /// <returns>(swapped, currentValue)</returns>
    Task<CasResult> CasIfGreaterAsync(string key, long newValue, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// DECR_POS — Decrement-If-Positive
    /// Quota 專用：value > 0 才遞減
    /// </summary>
    /// <returns>(success, newValue)</returns>
    Task<DecrResult> DecrIfPositiveAsync(string key, CancellationToken ct = default);

    // ── 分散式鎖 ──

    /// <summary>
    /// 嘗試取得分散式鎖
    /// </summary>
    /// <returns>鎖句柄（null = 取得失敗），使用 DisposeAsync 釋放鎖</returns>
    Task<IDistributedLock?> TryAcquireLockAsync(
        string resource,
        string ownerId,
        TimeSpan timeout,
        CancellationToken ct = default);

    // ── Pub/Sub ──

    /// <summary>發布訊息到頻道（路由到 Leader）</summary>
    Task PublishAsync(string channel, string message, CancellationToken ct = default);

    /// <summary>
    /// 訂閱頻道
    /// </summary>
    /// <returns>訂閱句柄，使用 DisposeAsync 取消訂閱</returns>
    Task<IAsyncDisposable> SubscribeAsync(
        string channel,
        Func<string, string, Task> handler,
        CancellationToken ct = default);

    // ── 強一致讀取 ──

    /// <summary>強制從 Leader 讀取（確保最新值）</summary>
    Task<T?> GetStrongAsync<T>(string key, CancellationToken ct = default);
}

/// <summary>CAS_GT 結果</summary>
public readonly record struct CasResult(bool Swapped, long CurrentValue);

/// <summary>DECR_POS 結果</summary>
public readonly record struct DecrResult(bool Success, long NewValue);

/// <summary>分散式鎖句柄</summary>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>鎖的資源名稱</summary>
    string Resource { get; }

    /// <summary>Fencing Token（下游操作用）</summary>
    long FencingToken { get; }

    /// <summary>鎖是否仍有效</summary>
    bool IsValid { get; }
}
