using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>Session 生命週期管理</summary>
public interface ISessionService
{
    /// <summary>註冊新 session</summary>
    ContainerSession RegisterSession(string taskId, string principalId, string roleId,
        string tokenJti, int currentEpoch, string encryptedSessionKey);

    /// <summary>取得 session</summary>
    ContainerSession? GetSession(string sessionId);

    /// <summary>心跳（更新 last_heartbeat + 可選 Token 續期）</summary>
    bool Heartbeat(string sessionId);

    /// <summary>優雅關閉</summary>
    bool CloseSession(string sessionId, string reason);

    /// <summary>撤銷 session</summary>
    bool RevokeSession(string sessionId, string reason, string revokedBy);

    /// <summary>撤銷某任務下的所有 session</summary>
    int RevokeSessionsByTask(string taskId, string reason, string revokedBy);
}
