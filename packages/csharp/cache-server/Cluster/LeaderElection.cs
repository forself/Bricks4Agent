using System.Net.Sockets;
using CacheProtocol;
using Microsoft.Extensions.Logging;

namespace CacheServer.Cluster;

/// <summary>
/// 簡化優先權選舉
///
/// 選舉協議：
/// 1. Follower 偵測 Leader 心跳超時（HealthMonitor.OnLeaderTimeout）
/// 2. 等待 300ms + priority * 100ms + random(0-150ms)（優先權越高等越短）
/// 3. 發送 VOTE_REQ（含 lastAppliedLsn、priority、term）
/// 4. 投票規則：LSN 最大者優先，同分按 priority
/// 5. 取得多數票 → 成為 Leader → 發送 LEADER_ANN
/// 6. Client 收到 REDIRECT → 自動切換
///
/// 選舉安全：
/// - 每個 term 最多投一票（防重複投票）
/// - term 必須 > 當前 term（拒絕舊 term）
/// - 候選人必須有最新 LSN（防資料落後的節點成為 Leader）
/// </summary>
public class LeaderElection
{
    private readonly ClusterConfig _config;
    private readonly ReplicationLog _replicationLog;
    private readonly HealthMonitor _healthMonitor;
    private readonly ILogger<LeaderElection> _logger;

    // 選舉狀態
    private volatile NodeRole _role = NodeRole.Follower;
    private long _currentTerm;
    private string? _votedFor; // 此 term 已投票給誰
    private readonly object _electionLock = new();

    // 事件
    public event Action<string, int>? OnBecomeLeader;  // (nodeId, port)
    public event Action<string, string, int>? OnBecomeFollower;  // (nodeId, leaderHost, leaderPort)

    public NodeRole Role => _role;
    public long CurrentTerm => Interlocked.Read(ref _currentTerm);

    public LeaderElection(
        ClusterConfig config,
        ReplicationLog replicationLog,
        HealthMonitor healthMonitor,
        ILogger<LeaderElection> logger)
    {
        _config = config;
        _replicationLog = replicationLog;
        _healthMonitor = healthMonitor;
        _logger = logger;

        // 訂閱 Leader 超時事件
        _healthMonitor.OnLeaderTimeout += OnLeaderTimeoutTriggered;
    }

    /// <summary>啟動時初始化（單節點 → Leader；叢集 → Follower 等待選舉）</summary>
    public void Initialize()
    {
        if (!_config.ClusterEnabled || _config.Peers.Count == 0)
        {
            // 單節點模式 → 直接成為 Leader
            _role = NodeRole.Leader;
            _healthMonitor.CurrentRole = NodeRole.Leader;
            OnBecomeLeader?.Invoke(_config.NodeId, _config.Port);
            _logger.LogInformation("Single node mode → Leader");
        }
        else
        {
            // 叢集模式 → 初始為 Follower
            _role = NodeRole.Follower;
            _healthMonitor.CurrentRole = NodeRole.Follower;
            _logger.LogInformation("Cluster mode → Follower (waiting for leader heartbeat or election)");
        }
    }

    /// <summary>Leader 超時 → 發起選舉</summary>
    private async void OnLeaderTimeoutTriggered()
    {
        if (_role == NodeRole.Leader) return; // 自己是 Leader 不需要選舉

        try
        {
            await StartElection();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Election failed");
            // 退回 Follower
            _role = NodeRole.Follower;
            _healthMonitor.CurrentRole = NodeRole.Follower;
        }
    }

    /// <summary>發起選舉</summary>
    public async Task StartElection()
    {
        lock (_electionLock)
        {
            if (_role == NodeRole.Leader) return;
            _role = NodeRole.Candidate;
            _healthMonitor.CurrentRole = NodeRole.Candidate;

            Interlocked.Increment(ref _currentTerm);
            _votedFor = _config.NodeId; // 投票給自己
        }

        var term = CurrentTerm;
        _logger.LogInformation("Starting election for term {Term}", term);

        // 等待優先權延遲
        var delay = 300 + (_config.Priority * 100) + Random.Shared.Next(0, 150);
        await Task.Delay(delay);

        // 如果等待期間已經有新 Leader → 取消
        if (_role != NodeRole.Candidate || CurrentTerm != term)
            return;

        // 發送 VOTE_REQ 到所有 peer
        var peers = _config.GetPeers();
        int votesReceived = 1; // 自投一票
        var quorum = _config.QuorumSize;

        var voteRequest = new VoteRequest
        {
            CandidateId = _config.NodeId,
            LastAppliedLsn = _replicationLog.CurrentLsn,
            Priority = _config.Priority,
            Term = term
        };

        var payload = CacheSerializer.Serialize(voteRequest);
        var frame = FrameCodec.Encode(OpCodes.VOTE_REQ, payload);

        var voteTasks = peers.Select(peer => RequestVoteFromPeer(peer, frame, term));
        var results = await Task.WhenAll(voteTasks);

        foreach (var granted in results)
        {
            if (granted)
                Interlocked.Increment(ref votesReceived);
        }

        _logger.LogInformation("Election term {Term}: got {Votes}/{Quorum} votes",
            term, votesReceived, quorum);

        // 取得多數票 → 成為 Leader
        if (votesReceived >= quorum && _role == NodeRole.Candidate && CurrentTerm == term)
        {
            BecomeLeader(term);
        }
        else
        {
            // 選舉失敗 → 退回 Follower
            _role = NodeRole.Follower;
            _healthMonitor.CurrentRole = NodeRole.Follower;
            _healthMonitor.ResetLeaderTimeout();
        }
    }

    /// <summary>向 peer 請求投票</summary>
    private async Task<bool> RequestVoteFromPeer(PeerInfo peer, byte[] frame, long expectedTerm)
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(peer.Host, peer.Port);

            var stream = client.GetStream();
            await stream.WriteAsync(frame);
            await stream.FlushAsync();

            // 讀取回應
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0) return false;

            var respResult = TryParseVoteResponse(buffer, bytesRead);
            if (respResult == null) return false;

            // 如果對方有更高 term → 退回 Follower
            if (respResult.Term > expectedTerm)
            {
                lock (_electionLock)
                {
                    if (respResult.Term > CurrentTerm)
                    {
                        Interlocked.Exchange(ref _currentTerm, respResult.Term);
                        _role = NodeRole.Follower;
                        _healthMonitor.CurrentRole = NodeRole.Follower;
                    }
                }
                return false;
            }

            return respResult.Granted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Vote request to {NodeId} failed: {Error}", peer.NodeId, ex.Message);
            return false;
        }
    }

    /// <summary>解析投票回應（同步方法避免 Span in async）</summary>
    private static VoteResponse? TryParseVoteResponse(byte[] buffer, int length)
    {
        try
        {
            var span = buffer.AsSpan(0, length);
            if (FrameCodec.TryParse(span, out var parsedFrame))
            {
                return CacheSerializer.Deserialize<VoteResponse>(parsedFrame.Payload.Span);
            }
        }
        catch { }
        return null;
    }

    /// <summary>成為 Leader</summary>
    private void BecomeLeader(long term)
    {
        lock (_electionLock)
        {
            _role = NodeRole.Leader;
            _healthMonitor.CurrentRole = NodeRole.Leader;
            _healthMonitor.CurrentLeaderId = _config.NodeId;
            _healthMonitor.CurrentTerm = term;
        }

        _logger.LogInformation("Became LEADER for term {Term}", term);
        OnBecomeLeader?.Invoke(_config.NodeId, _config.Port);

        // 廣播 LEADER_ANN 到所有 peer
        _ = AnnounceLeadership(term);
    }

    /// <summary>廣播 Leader 宣告</summary>
    private async Task AnnounceLeadership(long term)
    {
        var announcement = new LeaderAnnouncement
        {
            LeaderId = _config.NodeId,
            LeaderHost = _config.AdvertiseHost,
            LeaderPort = _config.Port,
            Term = term
        };

        var payload = CacheSerializer.Serialize(announcement);
        var frame = FrameCodec.Encode(OpCodes.LEADER_ANN, payload);

        var peers = _config.GetPeers();
        foreach (var peer in peers)
        {
            try
            {
                using var client = new TcpClient();
                client.NoDelay = true;
                await client.ConnectAsync(peer.Host, peer.Port);
                var stream = client.GetStream();
                await stream.WriteAsync(frame);
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to announce leadership to {NodeId}: {Error}",
                    peer.NodeId, ex.Message);
            }
        }
    }

    /// <summary>
    /// 處理投票請求（被其他候選人請求投票時呼叫）
    /// </summary>
    public VoteResponse HandleVoteRequest(VoteRequest request)
    {
        lock (_electionLock)
        {
            // 舊 term → 拒絕
            if (request.Term < CurrentTerm)
            {
                return new VoteResponse
                {
                    VoterId = _config.NodeId,
                    Granted = false,
                    Term = CurrentTerm
                };
            }

            // 更高 term → 更新並轉為 Follower
            if (request.Term > CurrentTerm)
            {
                Interlocked.Exchange(ref _currentTerm, request.Term);
                _role = NodeRole.Follower;
                _healthMonitor.CurrentRole = NodeRole.Follower;
                _votedFor = null;
            }

            // 已經投過票（且不是給同一候選人）→ 拒絕
            if (_votedFor != null && _votedFor != request.CandidateId)
            {
                return new VoteResponse
                {
                    VoterId = _config.NodeId,
                    Granted = false,
                    Term = CurrentTerm
                };
            }

            // 候選人的 LSN 必須 >= 我的 LSN（防止資料落後的節點成為 Leader）
            if (request.LastAppliedLsn < _replicationLog.CurrentLsn)
            {
                return new VoteResponse
                {
                    VoterId = _config.NodeId,
                    Granted = false,
                    Term = CurrentTerm
                };
            }

            // 投票
            _votedFor = request.CandidateId;
            _healthMonitor.ResetLeaderTimeout();

            _logger.LogInformation("Voted for {CandidateId} in term {Term}",
                request.CandidateId, request.Term);

            return new VoteResponse
            {
                VoterId = _config.NodeId,
                Granted = true,
                Term = CurrentTerm
            };
        }
    }

    /// <summary>處理 Leader 宣告（含 step-down 處理）</summary>
    public void HandleLeaderAnnouncement(LeaderAnnouncement announcement)
    {
        lock (_electionLock)
        {
            if (announcement.Term < CurrentTerm) return; // 舊 term 忽略

            Interlocked.Exchange(ref _currentTerm, announcement.Term);
            _votedFor = null;

            // 判斷是否為 step-down 通知（LeaderId 為空）
            if (string.IsNullOrEmpty(announcement.LeaderId))
            {
                // Leader 主動卸任 → 設為 Follower 但不重置心跳超時
                // → 心跳超時會立即觸發選舉（因為沒有新 Leader）
                _role = NodeRole.Follower;
                _healthMonitor.CurrentRole = NodeRole.Follower;
                _healthMonitor.CurrentLeaderId = null;
                _healthMonitor.CurrentTerm = announcement.Term;
                // 不呼叫 ResetLeaderTimeout → 讓 CheckLeaderAlive 盡快觸發選舉

                _logger.LogInformation(
                    "Leader stepped down at term {Term}. Preparing for election.",
                    announcement.Term);
                return;
            }

            _role = NodeRole.Follower;
            _healthMonitor.CurrentRole = NodeRole.Follower;
            _healthMonitor.CurrentLeaderId = announcement.LeaderId;
            _healthMonitor.CurrentTerm = announcement.Term;
            _healthMonitor.ResetLeaderTimeout();
        }

        _logger.LogInformation("Accepted leader {LeaderId} for term {Term}",
            announcement.LeaderId, announcement.Term);

        OnBecomeFollower?.Invoke(
            _config.NodeId, announcement.LeaderHost, announcement.LeaderPort);
    }

    /// <summary>
    /// Leader 優雅卸任（graceful step-down）
    ///
    /// 呼叫時機：Leader 即將關閉（Ctrl+C 或維護），主動通知
    /// 所有 Follower 開始選舉，而非等待 5 秒心跳超時。
    ///
    /// 步驟：
    /// 1. 遞增 term（確保新 Leader 的 term 比當前大）
    /// 2. 轉為 Follower
    /// 3. 廣播 LEADER_ANN with term+1, leaderId="" → Follower 解讀為
    ///    「Leader 已下線」→ 立即觸發選舉
    /// </summary>
    public async Task StepDownAsync()
    {
        if (_role != NodeRole.Leader) return;

        long newTerm;
        lock (_electionLock)
        {
            Interlocked.Increment(ref _currentTerm);
            newTerm = CurrentTerm;
            _role = NodeRole.Follower;
            _healthMonitor.CurrentRole = NodeRole.Follower;
            _votedFor = null;
        }

        _logger.LogInformation("Leader stepping down at term {Term}", newTerm);

        // 廣播 step-down announcement（leaderId="" 表示 Leader 主動卸任）
        var announcement = new LeaderAnnouncement
        {
            LeaderId = "", // 空 = step-down（Follower 收到後觸發選舉）
            LeaderHost = "",
            LeaderPort = 0,
            Term = newTerm
        };

        var payload = CacheSerializer.Serialize(announcement);
        var frame = FrameCodec.Encode(OpCodes.LEADER_ANN, payload);

        var peers = _config.GetPeers();
        foreach (var peer in peers)
        {
            try
            {
                using var client = new TcpClient();
                client.NoDelay = true;
                await client.ConnectAsync(peer.Host, peer.Port);
                var stream = client.GetStream();
                await stream.WriteAsync(frame);
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to send step-down to {NodeId}: {Error}",
                    peer.NodeId, ex.Message);
            }
        }
    }
}
