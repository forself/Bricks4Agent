using System.Collections.Concurrent;

namespace CacheClient;

/// <summary>
/// 每節點連線池
///
/// 管理到單一快取節點的多個 TCP 連線：
/// - 固定池大小（預設 4）
/// - 連線借出/歸還（round-robin）
/// - 故障連線自動重建
/// - 閒置連線回收
/// - 心跳保活
/// </summary>
public class ConnectionPool : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _poolSize;
    private readonly CacheClientOptions _options;

    private readonly NodeConnection[] _connections;
    private readonly SemaphoreSlim[] _connectionLocks;
    private int _roundRobin;
    private volatile bool _disposed;
    private volatile bool _healthy = true;

    public string Host => _host;
    public int Port => _port;
    public bool IsHealthy => _healthy;

    public ConnectionPool(string host, int port, CacheClientOptions options)
    {
        _host = host;
        _port = port;
        _poolSize = options.PoolSize;
        _options = options;

        _connections = new NodeConnection[_poolSize];
        _connectionLocks = new SemaphoreSlim[_poolSize];

        for (int i = 0; i < _poolSize; i++)
        {
            _connections[i] = new NodeConnection(host, port, options.ConnectTimeout, options.OperationTimeout);
            _connectionLocks[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// 取得一個可用連線（Round-Robin）
    /// </summary>
    public async Task<NodeConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var startIndex = Interlocked.Increment(ref _roundRobin) % _poolSize;

        // 嘗試從 startIndex 開始找到可用連線
        for (int i = 0; i < _poolSize; i++)
        {
            var idx = (startIndex + i) % _poolSize;
            var conn = _connections[idx];

            if (conn.IsConnected)
                return conn;

            // 嘗試重建連線
            if (await _connectionLocks[idx].WaitAsync(0, ct))
            {
                try
                {
                    if (!conn.IsConnected)
                    {
                        conn.Close();
                        var newConn = new NodeConnection(_host, _port, _options.ConnectTimeout, _options.OperationTimeout);
                        await newConn.ConnectAsync(ct);
                        _connections[idx] = newConn;
                        _healthy = true;
                        return newConn;
                    }
                    return conn;
                }
                catch
                {
                    // 連線失敗，繼續嘗試下一個
                }
                finally
                {
                    _connectionLocks[idx].Release();
                }
            }
        }

        // 所有連線都不可用，嘗試強制重建第一個
        var forceIdx = startIndex % _poolSize;
        await _connectionLocks[forceIdx].WaitAsync(ct);
        try
        {
            _connections[forceIdx].Close();
            var newConn = new NodeConnection(_host, _port, _options.ConnectTimeout, _options.OperationTimeout);
            await newConn.ConnectAsync(ct);
            _connections[forceIdx] = newConn;
            _healthy = true;
            return newConn;
        }
        catch (Exception ex)
        {
            _healthy = false;
            throw new CacheConnectionException(
                $"Failed to connect to {_host}:{_port}: {ex.Message}", ex);
        }
        finally
        {
            _connectionLocks[forceIdx].Release();
        }
    }

    /// <summary>心跳：檢查所有連線健康狀態</summary>
    public async Task HealthCheckAsync(CancellationToken ct = default)
    {
        bool anyHealthy = false;

        for (int i = 0; i < _poolSize; i++)
        {
            var conn = _connections[i];
            if (conn.IsConnected)
            {
                if (await conn.PingAsync(ct))
                {
                    anyHealthy = true;
                }
            }
        }

        _healthy = anyHealthy || _poolSize == 0;
    }

    /// <summary>關閉池中所有連線</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        for (int i = 0; i < _poolSize; i++)
        {
            await _connections[i].DisposeAsync();
            _connectionLocks[i].Dispose();
        }
    }
}

/// <summary>快取連線異常</summary>
public class CacheConnectionException : Exception
{
    public CacheConnectionException(string message) : base(message) { }
    public CacheConnectionException(string message, Exception inner) : base(message, inner) { }
}
