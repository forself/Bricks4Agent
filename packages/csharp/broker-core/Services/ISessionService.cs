using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>Session 生命週期管理</summary>
public interface ISessionService
{
    /// <summary>註冊新 session；<paramref name="registrationCredentialId"/> 記錄註冊時使用的註冊憑證。</summary>
    ContainerSession RegisterSession(string taskId, string principalId, string roleId,
        string tokenJti, int currentEpoch, string encryptedSessionKey, string? registrationCredentialId = null);

    /// <summary>取得 session</summary>
    ContainerSession? GetSession(string sessionId);

    /// <summary>
    /// 心跳：只延長仍為 Active 且尚未過期的 session（不會讓已過期的 session 復活），
    /// 新的到期時間為「現在 + TTL」與「註冊時間 + 最長存活時間」兩者較早者，且不會比目前的到期時間早。
    /// 有提供 <paramref name="newTokenJti"/> 時一併記錄為這個 session 最新的 token。
    /// 回傳新的到期時間；session 不存在、非 Active 或已過期時回傳 null。
    /// </summary>
    DateTime? Heartbeat(string sessionId, string? newTokenJti = null);

    /// <summary>優雅關閉</summary>
    bool CloseSession(string sessionId, string reason);

    /// <summary>撤銷 session</summary>
    bool RevokeSession(string sessionId, string reason, string revokedBy);

    /// <summary>撤銷某任務下的所有 session</summary>
    int RevokeSessionsByTask(string taskId, string reason, string revokedBy);

    /// <summary>撤銷以指定註冊憑證註冊、仍為 Active 的 session，回傳被撤銷的 session id。</summary>
    IReadOnlyList<string> RevokeSessionsByCredential(string registrationCredentialId, string reason, string revokedBy);

    /// <summary>撤銷這組 principal＋task 仍為 Active 的 session，回傳被撤銷的 session id。</summary>
    IReadOnlyList<string> RevokeSessionsBySubject(string principalId, string taskId, string reason, string revokedBy);
}
