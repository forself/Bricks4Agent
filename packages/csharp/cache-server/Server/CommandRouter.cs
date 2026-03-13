using CacheProtocol;
using CacheServer.Cluster;
using CacheServer.Engine;
using CacheServer.PubSub;
using Microsoft.Extensions.Logging;

namespace CacheServer.Server;

/// <summary>
/// OpCode 路由器 — 將 parsed frame 分派到對應的 CacheEngine 操作
///
/// 職責：
/// 1. 解析 frame payload → CacheCommand
/// 2. 根據 OpCode 呼叫對應的 CacheEngine 方法
/// 3. 打包 CacheResponse → response frame bytes
/// 4. 寫入操作時可選擇 REDIRECT（Follower 拒絕寫入）
/// 5. 叢集操作：複製、選舉、心跳、快照
///
/// 路由規則：
/// - 讀取操作（GET, EXISTS）→ 直接處理
/// - 寫入操作（SET, DEL, CAS, CAS_GT, DECR_POS, INCR, LOCK, UNLOCK, PUBLISH）
///   → Leader 處理（+ 複製到 Follower）；Follower 回 REDIRECT
/// - 叢集操作（REPLICATE, VOTE_*, LEADER_ANN, HEARTBEAT, SNAPSHOT）→ 交由叢集元件
/// </summary>
public class CommandRouter
{
    private readonly CacheEngine _engine;
    private readonly ILogger<CommandRouter> _logger;

    // 叢集狀態
    private volatile bool _isLeader = true; // 單節點預設為 Leader
    private volatile string _leaderHost = "";
    private volatile int _leaderPort = 0;

    // 叢集元件（叢集模式下注入）
    private ReplicationSender? _replicationSender;
    private ReplicationReceiver? _replicationReceiver;
    private LeaderElection? _leaderElection;
    private HealthMonitor? _healthMonitor;
    private SnapshotTransfer? _snapshotTransfer;
    private ClusterPubSub? _clusterPubSub;

    /// <summary>快取引擎（供 ClientSession 存取，例如 Pub/Sub 訂閱）</summary>
    public CacheEngine Engine => _engine;

    /// <summary>此節點是否為 Leader</summary>
    public bool IsLeader => _isLeader;

    public CommandRouter(CacheEngine engine, ILogger<CommandRouter> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    /// <summary>注入叢集元件（叢集模式啟用後呼叫）</summary>
    public void SetClusterComponents(
        ReplicationSender? replicationSender,
        ReplicationReceiver? replicationReceiver,
        LeaderElection? leaderElection,
        HealthMonitor? healthMonitor,
        SnapshotTransfer? snapshotTransfer,
        ClusterPubSub? clusterPubSub)
    {
        _replicationSender = replicationSender;
        _replicationReceiver = replicationReceiver;
        _leaderElection = leaderElection;
        _healthMonitor = healthMonitor;
        _snapshotTransfer = snapshotTransfer;
        _clusterPubSub = clusterPubSub;
    }

    /// <summary>
    /// 處理 frame 並回傳 response frame bytes
    /// 回傳 null 表示不需要回應（如 cluster 內部操作）
    /// </summary>
    public byte[]? Handle(FrameCodec.ParsedFrame frame)
    {
        // 叢集操作（不需要 CacheCommand 解析，直接處理原始 payload）
        if (IsClusterOpCode(frame.OpCode))
        {
            return HandleClusterOp(frame);
        }

        // 解析 payload
        CacheCommand? cmd;
        try
        {
            if (frame.Payload.Length == 0)
            {
                cmd = new CacheCommand { Id = CacheSerializer.NewRequestId() };
            }
            else
            {
                cmd = CacheSerializer.DeserializeCommand(frame.Payload.Span);
                if (cmd == null)
                    return CacheSerializer.EncodeError("", "Failed to deserialize command");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize command (OpCode=0x{Op:X2})", frame.OpCode);
            return CacheSerializer.EncodeError("", $"Deserialization error: {ex.Message}");
        }

        // 寫入操作：Follower → REDIRECT
        if (!_isLeader && OpCodes.IsWriteOp(frame.OpCode))
        {
            return CacheSerializer.EncodeRedirect(cmd.Id, _leaderHost, _leaderPort);
        }

        // 分派到對應處理器
        try
        {
            var response = frame.OpCode switch
            {
                // 讀取操作
                OpCodes.GET => _engine.Get(cmd),
                OpCodes.EXISTS => _engine.Exists(cmd),

                // 寫入操作（Leader 執行 + 異步複製）
                OpCodes.SET => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.Set(cmd)),
                OpCodes.DEL => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.Delete(cmd)),
                OpCodes.INCR => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.Increment(cmd)),
                OpCodes.EXPIRE => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.Expire(cmd)),

                // 原子 CAS 操作（Leader 執行 + 複製）
                OpCodes.CAS => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.CompareAndSwap(cmd)),
                OpCodes.CAS_GT => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.CasIfGreater(cmd)),
                OpCodes.DECR_POS => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.DecrIfPositive(cmd)),

                // 分散式鎖
                OpCodes.LOCK => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.Lock(cmd)),
                OpCodes.UNLOCK => HandleWriteWithReplication(cmd, frame.OpCode,
                    () => _engine.Unlock(cmd)),

                // Pub/Sub（PUBLISH 走此路由；SUBSCRIBE/UNSUBSCRIBE 在 ClientSession 處理）
                // Leader 本地投遞 + 複製 + 跨節點轉發（讓 Follower 的訂閱者也收到）
                OpCodes.PUBLISH => HandlePublishWithForwarding(cmd, frame.OpCode),

                _ => CacheResponse.Fail(cmd.Id, $"Unknown OpCode: 0x{frame.OpCode:X2}")
            };

            return CacheSerializer.EncodeOk(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling OpCode 0x{Op:X2}, key={Key}",
                frame.OpCode, cmd.Key ?? "(null)");
            return CacheSerializer.EncodeError(cmd.Id, $"Internal error: {ex.Message}");
        }
    }

    // ── 寫入 + 複製 ──

    /// <summary>
    /// 執行寫入操作並觸發非同步複製
    /// </summary>
    private CacheResponse HandleWriteWithReplication(
        CacheCommand cmd, byte opCode, Func<CacheResponse> execute)
    {
        // 1. Leader 本地執行
        var response = execute();

        // 2. 如果執行失敗，不需要複製
        if (!response.Ok)
            return response;

        // 3. 異步複製到 Follower（fire-and-forget，不阻塞 client）
        if (_replicationSender != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _replicationSender.ReplicateWriteAsync(
                        opCode,
                        cmd.Key ?? "",
                        cmd.Value,
                        cmd.TtlMs,
                        cmd.NewValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Replication failed for {Op} key={Key}",
                        OpCodes.GetName(opCode), cmd.Key);
                }
            });
        }

        return response;
    }

    // ── Pub/Sub 跨節點轉發 ──

    /// <summary>
    /// PUBLISH 專用：本地投遞 + 跨節點轉發
    ///
    /// PUBLISH 不是資料變更操作，不需要寫入 ReplicationLog。
    /// 而是透過 ClusterPubSub 直接轉發 PUBLISH frame 到所有 Follower，
    /// Follower 收到後執行本地投遞（CacheEngine.PublishMessage）。
    ///
    /// 流程：
    /// 1. Leader 本地 PublishMessage → 投遞給本地訂閱者
    /// 2. ClusterPubSub 轉發 PUBLISH frame 到所有 Follower → Follower 本地投遞
    /// </summary>
    private CacheResponse HandlePublishWithForwarding(CacheCommand cmd, byte opCode)
    {
        // 1. Leader 本地投遞
        var response = _engine.PublishMessage(cmd);

        if (!response.Ok)
            return response;

        // 2. 跨節點 Pub/Sub 轉發（讓 Follower 的訂閱者也收到）
        // 注意：不走 ReplicationLog（PUBLISH 不是狀態變更）
        if (_clusterPubSub != null && !string.IsNullOrEmpty(cmd.Channel))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _clusterPubSub.ForwardPublishToFollowersAsync(
                        cmd.Channel, cmd.Message ?? "");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Cluster pub/sub forwarding failed: channel={Channel}",
                        cmd.Channel);
                }
            });
        }

        return response;
    }

    // ── 叢集操作處理 ──

    private static bool IsClusterOpCode(byte opCode) => opCode switch
    {
        OpCodes.REPLICATE or OpCodes.REPLICATE_ACK
            or OpCodes.VOTE_REQ or OpCodes.VOTE_ACK
            or OpCodes.LEADER_ANN or OpCodes.CLUSTER_HEARTBEAT
            or OpCodes.SNAPSHOT => true,
        _ => false
    };

    private byte[]? HandleClusterOp(FrameCodec.ParsedFrame frame)
    {
        try
        {
            return frame.OpCode switch
            {
                OpCodes.REPLICATE => HandleReplicate(frame),
                OpCodes.VOTE_REQ => HandleVoteRequest(frame),
                OpCodes.LEADER_ANN => HandleLeaderAnnouncement(frame),
                OpCodes.CLUSTER_HEARTBEAT => HandleClusterHeartbeat(frame),
                OpCodes.SNAPSHOT => HandleSnapshot(frame),
                // REPLICATE_ACK 和 VOTE_ACK 是回應，不應出現在 server-side Handle 中
                OpCodes.REPLICATE_ACK or OpCodes.VOTE_ACK => null,
                _ => CacheSerializer.EncodeError("",
                    $"Unknown cluster OpCode: 0x{frame.OpCode:X2}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling cluster OpCode 0x{Op:X2}", frame.OpCode);
            return CacheSerializer.EncodeError("", $"Cluster error: {ex.Message}");
        }
    }

    /// <summary>處理 REPLICATE（Follower 端）</summary>
    private byte[]? HandleReplicate(FrameCodec.ParsedFrame frame)
    {
        if (_replicationReceiver == null)
        {
            return CacheSerializer.EncodeError("", "Replication receiver not configured");
        }

        return _replicationReceiver.HandleReplicate(frame.Payload.Span);
    }

    /// <summary>處理 VOTE_REQ</summary>
    private byte[]? HandleVoteRequest(FrameCodec.ParsedFrame frame)
    {
        if (_leaderElection == null)
        {
            return CacheSerializer.EncodeError("", "Leader election not configured");
        }

        var request = CacheSerializer.Deserialize<VoteRequest>(frame.Payload.Span);
        if (request == null)
        {
            return CacheSerializer.EncodeError("", "Invalid vote request");
        }

        var response = _leaderElection.HandleVoteRequest(request);
        var payload = CacheSerializer.Serialize(response);
        return FrameCodec.Encode(OpCodes.VOTE_ACK, payload);
    }

    /// <summary>處理 LEADER_ANN</summary>
    private byte[]? HandleLeaderAnnouncement(FrameCodec.ParsedFrame frame)
    {
        if (_leaderElection == null)
        {
            return null; // 非叢集模式，忽略
        }

        var announcement = CacheSerializer.Deserialize<LeaderAnnouncement>(frame.Payload.Span);
        if (announcement != null)
        {
            _leaderElection.HandleLeaderAnnouncement(announcement);
        }

        return null; // Leader announcement 不需要回應
    }

    /// <summary>處理 CLUSTER_HEARTBEAT（Follower 端）</summary>
    private byte[]? HandleClusterHeartbeat(FrameCodec.ParsedFrame frame)
    {
        if (_healthMonitor == null)
        {
            return null;
        }

        // 解析心跳：{ leader_id, term, timestamp }
        try
        {
            var heartbeat = CacheSerializer.Deserialize<HeartbeatPayload>(frame.Payload.Span);
            if (heartbeat != null)
            {
                _healthMonitor.RecordLeaderHeartbeat(heartbeat.LeaderId, heartbeat.Term);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse heartbeat");
        }

        return null; // 心跳不需要回應
    }

    /// <summary>處理 SNAPSHOT 請求（Leader 端接收 Follower 的快照請求）</summary>
    private byte[]? HandleSnapshot(FrameCodec.ParsedFrame frame)
    {
        if (_snapshotTransfer == null)
        {
            return CacheSerializer.EncodeError("", "Snapshot transfer not configured");
        }

        // Follower 端：接收快照資料
        if (!_isLeader)
        {
            var done = _snapshotTransfer.HandleSnapshotEntry(frame.Payload.Span);
            if (done)
            {
                _logger.LogInformation("Snapshot apply complete");
            }
            return null;
        }

        // Leader 端：處理快照請求
        // 解析 { lsn, node_id }
        try
        {
            var request = CacheSerializer.Deserialize<SnapshotRequest>(frame.Payload.Span);
            if (request != null)
            {
                var peer = new PeerInfo
                {
                    NodeId = request.NodeId,
                    Host = "", // 會從已知 peer 列表查找
                    Port = 0
                };

                // fire-and-forget：快照傳輸在背景進行
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _snapshotTransfer.HandleSnapshotRequest(request.Lsn, peer);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Snapshot transfer failed for {NodeId}", request.NodeId);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse snapshot request");
        }

        return null;
    }

    // ── 叢集狀態更新（供 LeaderElection / HealthMonitor 使用） ──

    /// <summary>設為 Leader</summary>
    public void BecomeLeader()
    {
        _isLeader = true;
        _logger.LogInformation("This node is now LEADER");
    }

    /// <summary>設為 Follower（指定 Leader 位址）</summary>
    public void BecomeFollower(string leaderHost, int leaderPort)
    {
        _isLeader = false;
        _leaderHost = leaderHost;
        _leaderPort = leaderPort;
        _logger.LogInformation("This node is now FOLLOWER (leader={Host}:{Port})", leaderHost, leaderPort);
    }
}

/// <summary>心跳 payload DTO</summary>
internal class HeartbeatPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("leader_id")]
    public string LeaderId { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("term")]
    public long Term { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>快照請求 payload DTO</summary>
internal class SnapshotRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("lsn")]
    public long Lsn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("node_id")]
    public string NodeId { get; set; } = "";
}
