using System.Net.Sockets;
using System.Text.Json;
using CacheProtocol;
using CacheServer.Engine;
using Microsoft.Extensions.Logging;

namespace CacheServer.Cluster;

/// <summary>
/// 全量快照傳輸
///
/// 用途：
/// - 新 Follower 加入叢集時，若 ReplicationLog 差距過大（無法增量同步），
///   Leader 發送全量快照讓 Follower 從零恢復。
///
/// 傳輸協議：
/// 1. Follower 向 Leader 發送 SNAPSHOT 請求（含 follower 的 currentLsn）
/// 2. Leader 判斷：
///    a. 若 followerLsn >= earliestLogLsn → 回應增量同步即可（不需快照）
///    b. 若差距過大 → 開始全量快照
/// 3. Leader 遍歷 CacheEngine 所有 key-value → 逐筆發送 REPLICATE
/// 4. 最後發送 SNAPSHOT_DONE 標記（特殊 REPLICATE，lsn = currentLsn, key = "__snapshot_done__"）
/// 5. Follower 收到 SNAPSHOT_DONE → 設定 LSN → 完成
///
/// 注意：
/// - 快照傳輸期間，新的寫入仍會被追加到 ReplicationLog
/// - 快照完成後，Follower 用增量同步追趕快照期間的寫入
/// </summary>
public class SnapshotTransfer
{
    private readonly CacheEngine _engine;
    private readonly ReplicationLog _replicationLog;
    private readonly ReplicationSender _replicationSender;
    private readonly ReplicationReceiver _replicationReceiver;
    private readonly ClusterConfig _config;
    private readonly ILogger<SnapshotTransfer> _logger;

    /// <summary>快照完成標記 key</summary>
    public const string SnapshotDoneKey = "__snapshot_done__";

    public SnapshotTransfer(
        CacheEngine engine,
        ReplicationLog replicationLog,
        ReplicationSender replicationSender,
        ReplicationReceiver replicationReceiver,
        ClusterConfig config,
        ILogger<SnapshotTransfer> logger)
    {
        _engine = engine;
        _replicationLog = replicationLog;
        _replicationSender = replicationSender;
        _replicationReceiver = replicationReceiver;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 處理 Follower 的 SNAPSHOT 請求（Leader 端）
    ///
    /// 判斷需要全量快照還是增量同步，並執行傳輸。
    /// </summary>
    /// <param name="followerLsn">Follower 當前的 LSN</param>
    /// <param name="peer">目標 Follower 資訊</param>
    public async Task HandleSnapshotRequest(long followerLsn, PeerInfo peer)
    {
        var earliestLsn = _replicationLog.EarliestLsn;
        var currentLsn = _replicationLog.CurrentLsn;

        _logger.LogInformation(
            "Snapshot request from {NodeId}: followerLsn={FollowerLsn}, " +
            "earliestLogLsn={Earliest}, currentLsn={Current}",
            peer.NodeId, followerLsn, earliestLsn, currentLsn);

        if (followerLsn >= currentLsn)
        {
            _logger.LogInformation(
                "Follower {NodeId} is already up-to-date (lsn={Lsn})", peer.NodeId, followerLsn);
            return;
        }

        if (earliestLsn > 0 && followerLsn >= earliestLsn)
        {
            // 差距在 log 範圍內 → 增量同步
            _logger.LogInformation(
                "Sending incremental sync to {NodeId}: {Count} entries",
                peer.NodeId, currentLsn - followerLsn);
            await _replicationSender.SendIncrementalSync(peer, followerLsn);
            return;
        }

        // 差距過大 → 全量快照
        _logger.LogInformation("Starting full snapshot to {NodeId}", peer.NodeId);
        await SendFullSnapshot(peer);
    }

    /// <summary>
    /// 發送全量快照到指定 Follower
    /// </summary>
    private async Task SendFullSnapshot(PeerInfo peer)
    {
        TcpClient? client = null;

        try
        {
            // 建立專用連線（不與其他複製共用）
            client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(peer.Host, peer.Port);
            var stream = client.GetStream();

            // 取得當前快照 LSN（快照期間新寫入會有更高 LSN）
            var snapshotLsn = _replicationLog.CurrentLsn;
            var stats = _engine.CacheStats;
            int sentCount = 0;

            _logger.LogInformation(
                "Full snapshot: {Count} keys, snapshotLsn={Lsn}",
                _engine.Count, snapshotLsn);

            // 遍歷 BaseCache 中的所有 key-value
            // 透過 CacheEngine 的 Stats 取得鍵清單
            // 注意：遍歷期間新增/刪除的 key 會在後續增量同步中處理
            var allKeys = GetAllCacheKeys();

            foreach (var key in allKeys)
            {
                try
                {
                    // 讀取值
                    var getCmd = new CacheCommand
                    {
                        Id = CacheSerializer.NewRequestId(),
                        Key = key
                    };
                    var getResp = _engine.Get(getCmd);

                    if (!getResp.Ok || !getResp.Value.HasValue)
                        continue; // key 已被刪除或不存在

                    // 編碼為 REPLICATE frame
                    var entry = new ReplicationEntry
                    {
                        Lsn = snapshotLsn, // 使用快照 LSN
                        OpCode = OpCodes.SET,
                        Key = key,
                        Value = getResp.Value,
                        TtlMs = 0, // 快照不含 TTL（簡化）
                        TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    var payload = CacheSerializer.Serialize(entry);
                    var frame = FrameCodec.Encode(OpCodes.SNAPSHOT, payload);

                    await stream.WriteAsync(frame);
                    sentCount++;

                    // 每 100 筆 flush 一次
                    if (sentCount % 100 == 0)
                    {
                        await stream.FlushAsync();
                        _logger.LogDebug("Snapshot progress: {Sent} keys sent", sentCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to snapshot key {Key}: {Error}", key, ex.Message);
                }
            }

            // 發送 SNAPSHOT_DONE 標記
            var doneEntry = new ReplicationEntry
            {
                Lsn = snapshotLsn,
                OpCode = OpCodes.SET,
                Key = SnapshotDoneKey,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var donePayload = CacheSerializer.Serialize(doneEntry);
            var doneFrame = FrameCodec.Encode(OpCodes.SNAPSHOT, donePayload);
            await stream.WriteAsync(doneFrame);
            await stream.FlushAsync();

            _logger.LogInformation(
                "Full snapshot complete: sent {Count} keys to {NodeId} (snapshotLsn={Lsn})",
                sentCount, peer.NodeId, snapshotLsn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Full snapshot to {NodeId} failed", peer.NodeId);
        }
        finally
        {
            client?.Dispose();
        }
    }

    /// <summary>
    /// 處理收到的 SNAPSHOT frame（Follower 端）
    /// </summary>
    /// <param name="payload">SNAPSHOT frame payload</param>
    /// <returns>是否為 SNAPSHOT_DONE 標記</returns>
    public bool HandleSnapshotEntry(ReadOnlySpan<byte> payload)
    {
        ReplicationEntry? entry;
        try
        {
            entry = CacheSerializer.Deserialize<ReplicationEntry>(payload);
            if (entry == null)
            {
                _logger.LogWarning("Failed to deserialize snapshot entry");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot entry deserialization error");
            return false;
        }

        // 檢查是否為完成標記
        if (entry.Key == SnapshotDoneKey)
        {
            _replicationLog.SetLsn(entry.Lsn);
            _logger.LogInformation("Snapshot complete: set LSN to {Lsn}", entry.Lsn);
            return true; // 快照完成
        }

        // 套用快照條目
        try
        {
            if (entry.Value.HasValue)
            {
                var ttl = entry.TtlMs > 0 ? TimeSpan.FromMilliseconds(entry.TtlMs) : (TimeSpan?)null;
                _engine.DirectSet(entry.Key, entry.Value.Value, ttl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply snapshot entry: key={Key}", entry.Key);
        }

        return false; // 繼續接收
    }

    /// <summary>
    /// 請求 Leader 發送快照（Follower 啟動時呼叫）
    /// </summary>
    public async Task RequestSnapshotFromLeader(string leaderHost, int leaderPort)
    {
        _logger.LogInformation(
            "Requesting snapshot from Leader at {Host}:{Port} (currentLsn={Lsn})",
            leaderHost, leaderPort, _replicationLog.CurrentLsn);

        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(leaderHost, leaderPort);
            var stream = client.GetStream();

            // 發送 SNAPSHOT 請求（payload = { lsn: currentLsn, node_id: myNodeId }）
            var request = new
            {
                lsn = _replicationLog.CurrentLsn,
                node_id = _config.NodeId
            };

            var payload = CacheSerializer.Serialize(request);
            var frame = FrameCodec.Encode(OpCodes.SNAPSHOT, payload);
            await stream.WriteAsync(frame);
            await stream.FlushAsync();

            // 接收快照資料（SNAPSHOT frames，直到 SNAPSHOT_DONE）
            var buffer = new byte[65536]; // 64KB buffer
            int filled = 0;
            int entriesReceived = 0;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled));
                if (bytesRead == 0) break;
                filled += bytesRead;

                // 解析所有完整 frame
                int offset = 0;
                while (offset < filled)
                {
                    var parseResult = TryParseSnapshotFrame(buffer, offset, filled - offset);
                    if (!parseResult.Success)
                        break; // 不完整的 frame，等待更多資料

                    if (parseResult.Frame.OpCode == OpCodes.SNAPSHOT)
                    {
                        var done = HandleSnapshotEntry(parseResult.Frame.Payload.Span);
                        entriesReceived++;

                        if (done)
                        {
                            _logger.LogInformation(
                                "Snapshot transfer complete: received {Count} entries",
                                entriesReceived);
                            return;
                        }
                    }

                    offset += parseResult.Frame.TotalLength;
                }

                // 移動未處理的資料到 buffer 開頭
                if (offset > 0 && offset < filled)
                {
                    Buffer.BlockCopy(buffer, offset, buffer, 0, filled - offset);
                    filled -= offset;
                }
                else if (offset >= filled)
                {
                    filled = 0;
                }

                // 擴充 buffer
                if (filled == buffer.Length && buffer.Length < 1024 * 1024)
                {
                    var newBuffer = new byte[buffer.Length * 2];
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, filled);
                    buffer = newBuffer;
                }
            }

            _logger.LogWarning(
                "Snapshot stream ended before SNAPSHOT_DONE (received {Count} entries)",
                entriesReceived);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot request to {Host}:{Port} failed", leaderHost, leaderPort);
        }
    }

    /// <summary>解析 frame（同步方法避免 Span in async）</summary>
    private static SnapshotParseResult TryParseSnapshotFrame(byte[] buffer, int offset, int length)
    {
        try
        {
            var span = buffer.AsSpan(offset, length);
            if (FrameCodec.TryParse(span, out var frame))
                return new SnapshotParseResult(true, frame);
            return new SnapshotParseResult(false, default);
        }
        catch
        {
            return new SnapshotParseResult(false, default);
        }
    }

    private readonly record struct SnapshotParseResult(bool Success, FrameCodec.ParsedFrame Frame);

    /// <summary>
    /// 取得所有快取 key 清單
    /// </summary>
    private List<string> GetAllCacheKeys()
    {
        return _engine.GetAllKeys().ToList();
    }
}
