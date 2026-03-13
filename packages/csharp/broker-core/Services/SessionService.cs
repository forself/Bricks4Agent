using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Session 生命週期管理
///
/// 職責：
/// - 註冊 session（含加密的 session_key 儲存）
/// - 心跳續期
/// - 優雅關閉 / 撤銷
///
/// 叢集化：
/// - 所有狀態持久化於 DB（encrypted_session_key、last_seen_seq）
/// - 無 in-memory 快取
/// </summary>
public class SessionService : ISessionService
{
    private readonly BrokerDb _db;

    public SessionService(BrokerDb db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public ContainerSession RegisterSession(
        string taskId, string principalId, string roleId,
        string tokenJti, int currentEpoch, string encryptedSessionKey)
    {
        var session = new ContainerSession
        {
            SessionId = IdGen.New("ses"),
            TaskId = taskId,
            PrincipalId = principalId,
            RoleId = roleId,
            TokenJti = tokenJti,
            EpochAtIssue = currentEpoch,
            EncryptedSessionKey = encryptedSessionKey,
            LastSeenSeq = 0,
            Status = SessionStatus.Active,
            RegisteredAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1) // 預設 1 小時，可透過 heartbeat 續期
        };

        _db.Insert(session);
        return session;
    }

    /// <inheritdoc />
    public ContainerSession? GetSession(string sessionId)
    {
        return _db.Get<ContainerSession>(sessionId);
    }

    /// <inheritdoc />
    public bool Heartbeat(string sessionId)
    {
        var now = DateTime.UtcNow;
        var newExpiry = now.AddHours(1);

        var affected = _db.Execute(
            "UPDATE container_sessions SET last_heartbeat = @now, expires_at = @newExpiry WHERE session_id = @sid AND status = 0",
            new { now, newExpiry, sid = sessionId });

        return affected > 0;
    }

    /// <inheritdoc />
    public bool CloseSession(string sessionId, string reason)
    {
        var affected = _db.Execute(
            "UPDATE container_sessions SET status = @closed, encrypted_session_key = '' WHERE session_id = @sid AND status = 0",
            new { closed = (int)SessionStatus.Closed, sid = sessionId });

        return affected > 0;
    }

    /// <inheritdoc />
    public bool RevokeSession(string sessionId, string reason, string revokedBy)
    {
        var affected = _db.Execute(
            "UPDATE container_sessions SET status = @revoked, encrypted_session_key = '' WHERE session_id = @sid AND status = 0",
            new { revoked = (int)SessionStatus.Revoked, sid = sessionId });

        return affected > 0;
    }

    /// <inheritdoc />
    public int RevokeSessionsByTask(string taskId, string reason, string revokedBy)
    {
        var affected = _db.Execute(
            "UPDATE container_sessions SET status = @revoked, encrypted_session_key = '' WHERE task_id = @tid AND status = 0",
            new { revoked = (int)SessionStatus.Revoked, tid = taskId });

        return affected;
    }
}
