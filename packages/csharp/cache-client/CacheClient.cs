using System.Text.Json;
using CacheProtocol;

namespace CacheClient;

/// <summary>
/// 分散式快取客戶端 — TCP 連線池 + 自動路由
///
/// 職責：
/// 1. 管理到每個叢集節點的連線池
/// 2. 讀取操作 → Round-Robin 任意節點
/// 3. 寫入操作 → 路由到 Leader
/// 4. REDIRECT → 自動更新 Leader 並重試
/// 5. 節點故障 → 自動 failover 到下一個節點
/// 6. 背景健康檢查 → 定期 PING 所有連線池
/// </summary>
public class DistributedCacheClient : IDistributedCache
{
    private readonly CacheClientOptions _options;
    private readonly LeaderTracker _tracker;
    private readonly ConnectionPool[] _pools;
    private readonly Timer? _healthCheckTimer;
    private volatile bool _disposed;

    /// <summary>整體健康狀態（至少一個節點可用）</summary>
    public bool IsHealthy => _pools.Any(p => p.IsHealthy);

    public DistributedCacheClient(CacheClientOptions options)
    {
        _options = options;
        _tracker = new LeaderTracker(options.Nodes);

        _pools = new ConnectionPool[_tracker.NodeCount];
        for (int i = 0; i < _tracker.NodeCount; i++)
        {
            var (host, port) = _tracker.GetNodeAddress(i);
            _pools[i] = new ConnectionPool(host, port, options);
        }

        // 啟動背景健康檢查（定期 PING 所有連線池）
        if (options.HeartbeatInterval > TimeSpan.Zero)
        {
            _healthCheckTimer = new Timer(
                _ => _ = RunHealthCheckAsync(),
                null,
                options.HeartbeatInterval,
                options.HeartbeatInterval);
        }
    }

    /// <summary>背景健康檢查：PING 所有連線池</summary>
    private async Task RunHealthCheckAsync()
    {
        if (_disposed) return;

        var tasks = _pools.Select(pool => SafeHealthCheckAsync(pool));
        await Task.WhenAll(tasks);
    }

    private static async Task SafeHealthCheckAsync(ConnectionPool pool)
    {
        try { await pool.HealthCheckAsync(); }
        catch { /* 健康檢查失敗不應傳播 */ }
    }

    // ── 基本 KV ──

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var cmd = new CacheCommand { Id = CacheSerializer.NewRequestId(), Key = key };
        var (_, resp) = await SendReadAsync(OpCodes.GET, cmd, ct);

        if (!resp.Ok)
            return default;

        return CacheSerializer.FromJsonElement<T>(resp.Value);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Key = key,
            Value = CacheSerializer.ToJsonElement(value),
            TtlMs = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : 0
        };

        var (_, resp) = await SendWriteAsync(OpCodes.SET, cmd, ct);

        if (!resp.Ok)
            throw new CacheOperationException($"SET failed: {resp.Error}");
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var cmd = new CacheCommand { Id = CacheSerializer.NewRequestId(), Key = key };
        var (_, resp) = await SendWriteAsync(OpCodes.DEL, cmd, ct);
        return resp.Ok && resp.Swapped;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var cmd = new CacheCommand { Id = CacheSerializer.NewRequestId(), Key = key };
        var (_, resp) = await SendReadAsync(OpCodes.EXISTS, cmd, ct);
        return resp.Ok && resp.Exists;
    }

    public async Task<long> IncrementAsync(string key, long delta = 1, CancellationToken ct = default)
    {
        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Key = key,
            Delta = delta
        };

        var (_, resp) = await SendWriteAsync(OpCodes.INCR, cmd, ct);

        if (!resp.Ok)
            throw new CacheOperationException($"INCR failed: {resp.Error}");

        return resp.NumValue;
    }

    // ── 原子 CAS 操作 ──

    public async Task<CasResult> CasIfGreaterAsync(
        string key, long newValue, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Key = key,
            NewValue = newValue,
            TtlMs = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : 0
        };

        var (_, resp) = await SendWriteAsync(OpCodes.CAS_GT, cmd, ct);

        if (!resp.Ok)
            throw new CacheOperationException($"CAS_GT failed: {resp.Error}");

        return new CasResult(resp.Swapped, resp.NumValue);
    }

    public async Task<DecrResult> DecrIfPositiveAsync(string key, CancellationToken ct = default)
    {
        var cmd = new CacheCommand { Id = CacheSerializer.NewRequestId(), Key = key };
        var (_, resp) = await SendWriteAsync(OpCodes.DECR_POS, cmd, ct);

        if (!resp.Ok)
            throw new CacheOperationException($"DECR_POS failed: {resp.Error}");

        return new DecrResult(resp.Swapped, resp.NumValue);
    }

    // ── 分散式鎖 ──

    public async Task<IDistributedLock?> TryAcquireLockAsync(
        string resource, string ownerId, TimeSpan timeout, CancellationToken ct = default)
    {
        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Resource = resource,
            OwnerId = ownerId,
            TimeoutMs = (long)timeout.TotalMilliseconds
        };

        var (_, resp) = await SendWriteAsync(OpCodes.LOCK, cmd, ct);

        if (!resp.Ok || !resp.Acquired)
            return null;

        return new DistributedLockHandle(this, resource, ownerId, resp.FencingToken);
    }

    internal async Task ReleaseLockAsync(string resource, string ownerId, long fencingToken, CancellationToken ct)
    {
        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Resource = resource,
            OwnerId = ownerId,
            FencingToken = fencingToken
        };

        await SendWriteAsync(OpCodes.UNLOCK, cmd, ct);
    }

    // ── Pub/Sub ──

    public async Task PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Channel = channel,
            Message = message
        };

        await SendWriteAsync(OpCodes.PUBLISH, cmd, ct);
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        string channel, Func<string, string, Task> handler, CancellationToken ct = default)
    {
        // 訂閱需要在特定連線上保持，以便接收推送
        // 嘗試任意可用節點（Leader 優先，失敗則嘗試其他節點）
        NodeConnection? conn = null;
        Exception? lastEx = null;

        // 先嘗試 Leader
        try
        {
            var leaderPool = _pools[_tracker.LeaderIndex % _pools.Length];
            conn = await leaderPool.GetConnectionAsync(ct);
        }
        catch (Exception ex)
        {
            lastEx = ex;
        }

        // Leader 失敗 → 輪詢其他節點
        if (conn == null || !conn.IsConnected)
        {
            for (int i = 0; i < _pools.Length; i++)
            {
                try
                {
                    conn = await _pools[i].GetConnectionAsync(ct);
                    if (conn.IsConnected)
                        break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }
        }

        if (conn == null || !conn.IsConnected)
        {
            throw new CacheConnectionException(
                $"Cannot subscribe to channel '{channel}': no available nodes",
                lastEx!);
        }

        conn.SetPubSubHandler((ch, msg) =>
        {
            _ = handler(ch, msg);
        });

        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Channel = channel
        };

        await conn.SendAsync(OpCodes.SUBSCRIBE, cmd, ct);

        return new SubscriptionHandle(conn, channel);
    }

    // ── 強一致讀取 ──

    public async Task<T?> GetStrongAsync<T>(string key, CancellationToken ct = default)
    {
        // 強制路由到 Leader
        var cmd = new CacheCommand { Id = CacheSerializer.NewRequestId(), Key = key };
        var (_, resp) = await SendToLeaderAsync(OpCodes.GET, cmd, ct);

        if (!resp.Ok)
            return default;

        return CacheSerializer.FromJsonElement<T>(resp.Value);
    }

    // ── 內部路由邏輯 ──

    /// <summary>讀取操作：Round-Robin 到任意節點</summary>
    private async Task<(byte OpCode, CacheResponse Response)> SendReadAsync(
        byte opCode, CacheCommand cmd, CancellationToken ct)
    {
        int retries = _options.MaxRetries + 1;

        for (int attempt = 0; attempt < retries; attempt++)
        {
            var nodeIndex = _tracker.NextReadIndex();

            try
            {
                var pool = _pools[nodeIndex % _pools.Length];
                var conn = await pool.GetConnectionAsync(ct);
                var (respOp, resp) = await conn.SendAsync(opCode, cmd, ct);

                // REDIRECT → 路由到 Leader
                if (respOp == OpCodes.REDIRECT && resp.LeaderHost != null)
                {
                    _tracker.UpdateLeader(resp.LeaderHost, resp.LeaderPort);
                    return await SendToLeaderAsync(opCode, cmd, ct);
                }

                return (respOp, resp);
            }
            catch (Exception) when (attempt < retries - 1)
            {
                // 下一個節點重試
                continue;
            }
        }

        return (OpCodes.RESPONSE_ERR, CacheResponse.Fail(cmd.Id, "All nodes failed"));
    }

    /// <summary>寫入操作：路由到 Leader</summary>
    private async Task<(byte OpCode, CacheResponse Response)> SendWriteAsync(
        byte opCode, CacheCommand cmd, CancellationToken ct)
    {
        return await SendToLeaderAsync(opCode, cmd, ct);
    }

    /// <summary>發送到 Leader（含 REDIRECT 處理）</summary>
    private async Task<(byte OpCode, CacheResponse Response)> SendToLeaderAsync(
        byte opCode, CacheCommand cmd, CancellationToken ct)
    {
        int retries = _options.MaxRetries + 1;

        for (int attempt = 0; attempt < retries; attempt++)
        {
            var leaderIndex = _tracker.LeaderIndex;

            try
            {
                var pool = _pools[leaderIndex % _pools.Length];
                var conn = await pool.GetConnectionAsync(ct);
                var (respOp, resp) = await conn.SendAsync(opCode, cmd, ct);

                // REDIRECT → 更新 Leader 並重試
                if (respOp == OpCodes.REDIRECT && resp.LeaderHost != null)
                {
                    _tracker.UpdateLeader(resp.LeaderHost, resp.LeaderPort);
                    continue;
                }

                return (respOp, resp);
            }
            catch (Exception) when (attempt < retries - 1)
            {
                // Leader 可能故障，嘗試 rotate
                _tracker.RotateLeader();
                continue;
            }
        }

        return (OpCodes.RESPONSE_ERR, CacheResponse.Fail(cmd.Id, "Leader unreachable"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止健康檢查
        if (_healthCheckTimer != null)
        {
            await _healthCheckTimer.DisposeAsync();
        }

        foreach (var pool in _pools)
        {
            await pool.DisposeAsync();
        }
    }
}

/// <summary>分散式鎖句柄實作</summary>
internal class DistributedLockHandle : IDistributedLock
{
    private readonly DistributedCacheClient _client;
    private readonly string _resource;
    private readonly string _ownerId;
    private volatile bool _released;

    public string Resource { get; }
    public long FencingToken { get; }
    public bool IsValid => !_released;

    public DistributedLockHandle(
        DistributedCacheClient client,
        string resource,
        string ownerId,
        long fencingToken)
    {
        _client = client;
        _resource = resource;
        _ownerId = ownerId;
        Resource = resource;
        FencingToken = fencingToken;
    }

    public async ValueTask DisposeAsync()
    {
        if (_released) return;
        _released = true;

        try
        {
            await _client.ReleaseLockAsync(_resource, _ownerId, FencingToken, CancellationToken.None);
        }
        catch { /* best effort release */ }
    }
}

/// <summary>Pub/Sub 訂閱句柄</summary>
internal class SubscriptionHandle : IAsyncDisposable
{
    private readonly NodeConnection _conn;
    private readonly string _channel;
    private volatile bool _disposed;

    public SubscriptionHandle(NodeConnection conn, string channel)
    {
        _conn = conn;
        _channel = channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            var cmd = new CacheCommand
            {
                Id = CacheSerializer.NewRequestId(),
                Channel = _channel
            };
            await _conn.SendAsync(OpCodes.UNSUBSCRIBE, cmd);
            _conn.SetPubSubHandler(null);
        }
        catch { /* best effort */ }
    }
}

/// <summary>快取操作異常</summary>
public class CacheOperationException : Exception
{
    public CacheOperationException(string message) : base(message) { }
    public CacheOperationException(string message, Exception inner) : base(message, inner) { }
}
