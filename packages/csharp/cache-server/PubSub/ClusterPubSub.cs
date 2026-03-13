using System.Net.Sockets;
using CacheProtocol;
using CacheServer.Cluster;
using Microsoft.Extensions.Logging;

namespace CacheServer.PubSub;

/// <summary>
/// 跨節點 Pub/Sub 轉發器
///
/// 職責：
/// 1. Leader 收到 PUBLISH → 轉發到所有 Follower
/// 2. Follower 收到轉發的 PUBLISH → 投遞到本地訂閱者
/// 3. 確保叢集中所有節點的訂閱者都能收到訊息
///
/// 典型使用場景：
/// - Epoch 廣播：admin 呼叫 IncrementEpoch → broker 的 CacheRevocationService
///   PUBLISH "epoch_changed" → Leader 轉發到所有 Follower → 所有 broker
///   的訂閱連線收到推送 → 本地快取即時失效
///
/// 流程：
///   Client (broker) → PUBLISH "epoch_changed" → Leader
///     ├── Leader 本地投遞（已有訂閱者的 ClientSession 收到）
///     ├── Leader → ClusterPubSub.ForwardPublishToFollowers()
///     │     ├── Follower-1：收到 → 本地投遞（該 Follower 上的訂閱者收到）
///     │     └── Follower-2：收到 → 本地投遞
///     └── Leader 回應 Client OK
///
/// 設計考量：
/// - 轉發是 fire-and-forget（不阻塞 PUBLISH 回應）
/// - Follower 故障不影響 PUBLISH 成功
/// - 重連後訂閱者需要重新訂閱（無訂閱持久化）
/// </summary>
public class ClusterPubSub : IDisposable
{
    private readonly ClusterConfig _config;
    private readonly ILogger<ClusterPubSub> _logger;

    // Follower 連線（用於轉發 PUBLISH）
    private readonly Dictionary<string, TcpClient> _followerConnections = new();
    private readonly object _connLock = new();

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);
    private volatile bool _disposed;

    public ClusterPubSub(ClusterConfig config, ILogger<ClusterPubSub> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Leader 端：將 PUBLISH 訊息轉發到所有 Follower
    ///
    /// 呼叫時機：Leader 的 CommandRouter 處理 PUBLISH 後，
    /// 非同步轉發到所有 Follower，確保跨節點訂閱者都收到。
    /// </summary>
    /// <param name="channel">頻道名稱</param>
    /// <param name="message">訊息內容</param>
    public async Task ForwardPublishToFollowersAsync(string channel, string message)
    {
        if (!_config.ClusterEnabled || _config.Peers.Count == 0)
            return;

        var cmd = new CacheCommand
        {
            Id = CacheSerializer.NewRequestId(),
            Channel = channel,
            Message = message
        };

        var payload = CacheSerializer.SerializeCommand(cmd);
        var frame = FrameCodec.Encode(OpCodes.PUBLISH, payload);

        var peers = _config.GetPeers();
        var tasks = peers.Select(peer => ForwardToPeerAsync(peer, frame, channel));

        await Task.WhenAll(tasks);
    }

    /// <summary>轉發 PUBLISH 到單一 Follower</summary>
    private async Task ForwardToPeerAsync(PeerInfo peer, byte[] frame, string channel)
    {
        try
        {
            var conn = GetOrCreateConnection(peer);
            if (conn == null || !conn.Connected)
                return;

            var stream = conn.GetStream();
            await stream.WriteAsync(frame);
            await stream.FlushAsync();

            _logger.LogDebug("Forwarded PUBLISH to {NodeId}: channel={Channel}",
                peer.NodeId, channel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to forward PUBLISH to {NodeId}: {Error}",
                peer.NodeId, ex.Message);
            RemoveConnection(peer.NodeId);
        }
    }

    // ── 連線管理 ──

    private TcpClient? GetOrCreateConnection(PeerInfo peer)
    {
        lock (_connLock)
        {
            if (_followerConnections.TryGetValue(peer.NodeId, out var existing) && existing.Connected)
                return existing;

            try
            {
                var client = new TcpClient();
                client.NoDelay = true;
                client.SendTimeout = (int)SendTimeout.TotalMilliseconds;
                client.Connect(peer.Host, peer.Port);
                _followerConnections[peer.NodeId] = client;
                return client;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot connect to {NodeId} for pub/sub: {Error}",
                    peer.NodeId, ex.Message);
                return null;
            }
        }
    }

    private void RemoveConnection(string nodeId)
    {
        lock (_connLock)
        {
            if (_followerConnections.Remove(nodeId, out var conn))
            {
                try { conn.Dispose(); } catch { }
            }
        }
    }

    /// <summary>關閉所有連線</summary>
    public void CloseAllConnections()
    {
        lock (_connLock)
        {
            foreach (var conn in _followerConnections.Values)
            {
                try { conn.Dispose(); } catch { }
            }
            _followerConnections.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAllConnections();
    }
}
