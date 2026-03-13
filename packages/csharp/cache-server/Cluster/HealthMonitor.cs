using System.Net.Sockets;
using CacheProtocol;
using Microsoft.Extensions.Logging;

namespace CacheServer.Cluster;

/// <summary>
/// 叢集健康監控器
///
/// 職責：
/// 1. Leader → Follower：定期發送叢集心跳
/// 2. Follower → Leader：偵測心跳超時 → 觸發選舉
/// 3. 維護 Peer 連線狀態
///
/// 心跳協議：
/// - Leader 每 1 秒發送 CLUSTER_HEARTBEAT 到所有 Follower
/// - 心跳 payload 包含 Leader 的 nodeId, term, lastLsn
/// - Follower 超過 5 秒未收到心跳 → 觸發 LeaderElection
/// </summary>
public class HealthMonitor : IDisposable
{
    private readonly ClusterConfig _config;
    private readonly ILogger<HealthMonitor> _logger;

    private Timer? _heartbeatTimer;
    private DateTime _lastLeaderHeartbeat = DateTime.UtcNow;
    private volatile bool _disposed;

    // 配置
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LeaderTimeout = TimeSpan.FromSeconds(5);

    // Peer 連線（nodeId → TcpClient）
    private readonly Dictionary<string, TcpClient> _peerConnections = new();
    private readonly object _peerLock = new();

    // 事件：Leader 超時
    public event Action? OnLeaderTimeout;

    // 當前狀態
    public NodeRole CurrentRole { get; set; } = NodeRole.Follower;
    public string? CurrentLeaderId { get; set; }
    public long CurrentTerm { get; set; }

    public HealthMonitor(ClusterConfig config, ILogger<HealthMonitor> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>啟動健康監控</summary>
    public void Start()
    {
        _heartbeatTimer = new Timer(HeartbeatTick, null, HeartbeatInterval, HeartbeatInterval);
    }

    /// <summary>停止健康監控</summary>
    public void Stop()
    {
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    /// <summary>心跳 tick</summary>
    private void HeartbeatTick(object? state)
    {
        if (_disposed) return;

        try
        {
            if (CurrentRole == NodeRole.Leader)
            {
                // Leader：向所有 Follower 發送心跳
                SendHeartbeatToFollowers();
            }
            else if (CurrentRole == NodeRole.Follower)
            {
                // Follower：檢查是否收到 Leader 心跳
                CheckLeaderAlive();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in heartbeat tick");
        }
    }

    /// <summary>Leader: 向所有 Follower 發送心跳</summary>
    private void SendHeartbeatToFollowers()
    {
        var peers = _config.GetPeers();

        foreach (var peer in peers)
        {
            try
            {
                SendHeartbeatToPeer(peer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to send heartbeat to {NodeId}: {Error}",
                    peer.NodeId, ex.Message);
            }
        }
    }

    /// <summary>發送心跳到指定 peer</summary>
    private void SendHeartbeatToPeer(PeerInfo peer)
    {
        var conn = GetOrCreateConnection(peer);
        if (conn == null || !conn.Connected) return;

        var heartbeat = new
        {
            leader_id = _config.NodeId,
            term = CurrentTerm,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var payload = CacheSerializer.Serialize(heartbeat);
        var frame = FrameCodec.Encode(OpCodes.CLUSTER_HEARTBEAT, payload);

        try
        {
            var stream = conn.GetStream();
            stream.Write(frame);
            stream.Flush();
        }
        catch
        {
            // 連線失敗 → 清除並下次重建
            RemoveConnection(peer.NodeId);
        }
    }

    /// <summary>Follower: 檢查 Leader 是否存活</summary>
    private void CheckLeaderAlive()
    {
        var elapsed = DateTime.UtcNow - _lastLeaderHeartbeat;

        if (elapsed > LeaderTimeout)
        {
            _logger.LogWarning(
                "Leader timeout ({Elapsed:F1}s > {Threshold:F1}s). Triggering election.",
                elapsed.TotalSeconds, LeaderTimeout.TotalSeconds);

            OnLeaderTimeout?.Invoke();
        }
    }

    /// <summary>更新 Leader 心跳接收時間（Follower 收到心跳時呼叫）</summary>
    public void RecordLeaderHeartbeat(string leaderId, long term)
    {
        _lastLeaderHeartbeat = DateTime.UtcNow;
        CurrentLeaderId = leaderId;

        if (term > CurrentTerm)
            CurrentTerm = term;
    }

    /// <summary>重置心跳超時（選舉結束時呼叫）</summary>
    public void ResetLeaderTimeout()
    {
        _lastLeaderHeartbeat = DateTime.UtcNow;
    }

    // ── Peer 連線管理 ──

    private TcpClient? GetOrCreateConnection(PeerInfo peer)
    {
        lock (_peerLock)
        {
            if (_peerConnections.TryGetValue(peer.NodeId, out var existing) && existing.Connected)
                return existing;

            try
            {
                var client = new TcpClient();
                client.Connect(peer.Host, peer.Port);
                client.NoDelay = true;
                _peerConnections[peer.NodeId] = client;
                return client;
            }
            catch
            {
                return null;
            }
        }
    }

    private void RemoveConnection(string nodeId)
    {
        lock (_peerLock)
        {
            if (_peerConnections.Remove(nodeId, out var conn))
            {
                try { conn.Dispose(); } catch { }
            }
        }
    }

    public void CloseAllConnections()
    {
        lock (_peerLock)
        {
            foreach (var conn in _peerConnections.Values)
            {
                try { conn.Dispose(); } catch { }
            }
            _peerConnections.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        CloseAllConnections();
    }
}
