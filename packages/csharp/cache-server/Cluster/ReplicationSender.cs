using System.Net.Sockets;
using CacheProtocol;
using CacheServer.Engine;
using Microsoft.Extensions.Logging;

namespace CacheServer.Cluster;

/// <summary>
/// 複製發送器（Leader 端）
///
/// 職責：
/// 1. 攔截所有寫入操作 → 追加 ReplicationLog
/// 2. 發送 REPLICATE frame 到所有 Follower
/// 3. 等待 Quorum 確認（含自身已套用）
/// 4. 回傳結果給 Client
///
/// 寫入路徑：
///   Client → CommandRouter → ReplicationSender.ReplicateWrite()
///     1. Leader 本地套用到 CacheEngine
///     2. 追加 ReplicationLog
///     3. 發送 REPLICATE 到所有 Follower
///     4. 等待 Quorum（N/2+1，含自身）確認
///     5. 回傳 OK 給 Client
///
/// 若 Quorum 失敗（多數 Follower 不可達），仍回傳 OK（leader 已套用），
/// 但記錄警告。下次 Follower 連線時會增量補齊。
/// </summary>
public class ReplicationSender : IDisposable
{
    private readonly ClusterConfig _config;
    private readonly ReplicationLog _replicationLog;
    private readonly ILogger<ReplicationSender> _logger;

    // Follower 連線（nodeId → TcpClient）
    private readonly Dictionary<string, TcpClient> _followerConnections = new();
    private readonly object _connLock = new();

    // Quorum 等待超時
    private static readonly TimeSpan QuorumTimeout = TimeSpan.FromSeconds(3);

    private volatile bool _disposed;

    public ReplicationSender(
        ClusterConfig config,
        ReplicationLog replicationLog,
        ILogger<ReplicationSender> logger)
    {
        _config = config;
        _replicationLog = replicationLog;
        _logger = logger;
    }

    /// <summary>
    /// 複製寫入操作到 Follower 叢集
    ///
    /// Leader 已在本地套用操作，此方法負責：
    /// 1. 追加 ReplicationLog
    /// 2. 發送 REPLICATE 到所有 Follower
    /// 3. 等待 Quorum 確認
    /// </summary>
    /// <returns>是否達到 Quorum</returns>
    public async Task<bool> ReplicateWriteAsync(
        byte opCode, string key, System.Text.Json.JsonElement? value,
        long ttlMs = 0, long newValue = 0)
    {
        if (!_config.ClusterEnabled || _config.Peers.Count == 0)
            return true; // 單節點模式，無需複製

        // 1. 追加 ReplicationLog
        var entry = _replicationLog.Append(opCode, key, value, ttlMs, newValue);

        // 2. 發送 REPLICATE 到所有 Follower
        var peers = _config.GetPeers();
        var quorum = _config.QuorumSize;
        int acks = 1; // 自身已套用 = 1 票

        if (peers.Count == 0)
            return true;

        // 編碼 REPLICATE frame
        var payload = CacheSerializer.Serialize(entry);
        var frame = FrameCodec.Encode(OpCodes.REPLICATE, payload);

        // 3. 並行發送到所有 Follower
        var replicateTasks = peers.Select(peer => SendReplicateAsync(peer, frame, entry.Lsn));
        var results = await Task.WhenAll(replicateTasks);

        foreach (var acked in results)
        {
            if (acked)
                Interlocked.Increment(ref acks);
        }

        var quorumReached = acks >= quorum;

        if (!quorumReached)
        {
            _logger.LogWarning(
                "Quorum NOT reached for LSN {Lsn}: {Acks}/{Quorum} acks (continuing anyway)",
                entry.Lsn, acks, quorum);
        }
        else
        {
            _logger.LogDebug(
                "Quorum OK for LSN {Lsn}: {Acks}/{Quorum} acks",
                entry.Lsn, acks, quorum);
        }

        return quorumReached;
    }

    /// <summary>
    /// 批量複製多個條目（新 Follower 增量同步用）
    /// </summary>
    public async Task SendIncrementalSync(PeerInfo peer, long afterLsn)
    {
        var entries = _replicationLog.GetEntriesAfter(afterLsn);

        if (entries.Count == 0)
        {
            _logger.LogDebug("No entries to sync to {NodeId} (afterLsn={Lsn})", peer.NodeId, afterLsn);
            return;
        }

        _logger.LogInformation(
            "Sending incremental sync to {NodeId}: {Count} entries (LSN {From}..{To})",
            peer.NodeId, entries.Count, entries[0].Lsn, entries[^1].Lsn);

        foreach (var entry in entries)
        {
            var payload = CacheSerializer.Serialize(entry);
            var frame = FrameCodec.Encode(OpCodes.REPLICATE, payload);

            var acked = await SendReplicateAsync(peer, frame, entry.Lsn);
            if (!acked)
            {
                _logger.LogWarning(
                    "Incremental sync to {NodeId} failed at LSN {Lsn}",
                    peer.NodeId, entry.Lsn);
                break;
            }
        }
    }

    /// <summary>發送單條 REPLICATE 到指定 Follower</summary>
    private async Task<bool> SendReplicateAsync(PeerInfo peer, byte[] frame, long lsn)
    {
        try
        {
            var conn = GetOrCreateConnection(peer);
            if (conn == null || !conn.Connected)
                return false;

            var stream = conn.GetStream();

            // 發送 REPLICATE frame
            await stream.WriteAsync(frame);
            await stream.FlushAsync();

            // 等待 REPLICATE_ACK（帶超時）
            using var cts = new CancellationTokenSource(QuorumTimeout);

            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, cts.Token);
            if (bytesRead == 0)
            {
                RemoveConnection(peer.NodeId);
                return false;
            }

            // 解析 ACK
            var ackResult = TryParseReplicateAck(buffer, bytesRead);
            if (ackResult == null)
                return false;

            return ackResult.Ok && ackResult.NumValue >= lsn;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Replicate ACK timeout from {NodeId} for LSN {Lsn}", peer.NodeId, lsn);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Replicate to {NodeId} failed for LSN {Lsn}: {Error}",
                peer.NodeId, lsn, ex.Message);
            RemoveConnection(peer.NodeId);
            return false;
        }
    }

    /// <summary>解析 REPLICATE_ACK 回應（同步方法避免 Span in async）</summary>
    private static CacheResponse? TryParseReplicateAck(byte[] buffer, int length)
    {
        try
        {
            var span = buffer.AsSpan(0, length);
            if (FrameCodec.TryParse(span, out var frame))
            {
                if (frame.OpCode == OpCodes.REPLICATE_ACK || frame.OpCode == OpCodes.RESPONSE_OK)
                {
                    return CacheSerializer.Deserialize<CacheResponse>(frame.Payload.Span);
                }
            }
        }
        catch { }
        return null;
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
                client.SendTimeout = (int)QuorumTimeout.TotalMilliseconds;
                client.ReceiveTimeout = (int)QuorumTimeout.TotalMilliseconds;
                client.Connect(peer.Host, peer.Port);
                _followerConnections[peer.NodeId] = client;
                _logger.LogDebug("Connected to Follower {NodeId} at {Host}:{Port}",
                    peer.NodeId, peer.Host, peer.Port);
                return client;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot connect to Follower {NodeId}: {Error}",
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

    /// <summary>關閉所有 Follower 連線</summary>
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
