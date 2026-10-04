using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Session 生命週期管理
///
/// 職責：
/// - 註冊 session（含加密的 session_key 儲存）
/// - 心跳續期（以 <see cref="SessionLifetimeOptions"/> 的 TTL 與最長存活時間為上限）
/// - 優雅關閉 / 撤銷
///
/// 叢集化：
/// - 所有狀態持久化於 DB（encrypted_session_key、last_seen_seq）
/// - 無 in-memory 快取
/// </summary>
public class SessionService : ISessionService
{
    private readonly BrokerDb _db;
    private readonly SessionLifetimeOptions _lifetime;

    public SessionService(BrokerDb db)
        : this(db, new SessionLifetimeOptions())
    {
    }

    public SessionService(BrokerDb db, SessionLifetimeOptions lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        lifetime.Validate();
        _db = db;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public ContainerSession RegisterSession(
        string taskId, string principalId, string roleId,
        string tokenJti, int currentEpoch, string encryptedSessionKey)
    {
        var now = DateTime.UtcNow;
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
            RegisteredAt = now,
            LastHeartbeat = now,
            ExpiresAt = now + _lifetime.Ttl // 可透過 heartbeat 續期，最長到註冊時間 + MaxLifetime
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
    public DateTime? Heartbeat(string sessionId, string? newTokenJti = null)
    {
        var session = GetSession(sessionId);
        var now = DateTime.UtcNow;
        if (session == null || session.Status != SessionStatus.Active || session.ExpiresAt <= now)
        {
            return null;
        }

        var newExpiry = now + _lifetime.Ttl;
        var lifetimeEnd = session.RegisteredAt + _lifetime.MaxLifetime;
        if (newExpiry > lifetimeEnd)
        {
            newExpiry = lifetimeEnd;
        }

        if (newExpiry < session.ExpiresAt)
        {
            newExpiry = session.ExpiresAt;
        }

        // 資料庫讀回的時間沒有時區標記；所有時間都以 UTC 儲存，回傳前標記為 UTC。
        newExpiry = DateTime.SpecifyKind(newExpiry, DateTimeKind.Utc);

        // 條件更新：同時要求仍為 Active 且尚未過期，與上面的讀取之間若被關閉或撤銷也不會被延長。
        var affected = string.IsNullOrEmpty(newTokenJti)
            ? _db.Execute(
                @"UPDATE container_sessions SET last_heartbeat = @now, expires_at = @newExpiry
                  WHERE session_id = @sid AND status = 0 AND expires_at > @now",
                new { now, newExpiry, sid = sessionId })
            : _db.Execute(
                @"UPDATE container_sessions SET last_heartbeat = @now, expires_at = @newExpiry, token_jti = @jti
                  WHERE session_id = @sid AND status = 0 AND expires_at > @now",
                new { now, newExpiry, jti = newTokenJti, sid = sessionId });

        return affected > 0 ? newExpiry : null;
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
