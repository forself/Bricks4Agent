using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>能力目錄（資料庫驅動的白名單）</summary>
public interface ICapabilityCatalog
{
    /// <summary>取得能力定義</summary>
    Capability? GetCapability(string capabilityId);

    /// <summary>列出所有能力</summary>
    List<Capability> ListCapabilities(string? filter = null);

    /// <summary>檢查主體在指定 task/session 是否有此能力的授予</summary>
    CapabilityGrant? GetActiveGrant(string principalId, string taskId, string sessionId, string capabilityId);

    /// <summary>建立能力授予</summary>
    CapabilityGrant CreateGrant(string taskId, string sessionId, string principalId,
        string capabilityId, string scopeOverride, int quota, DateTime expiresAt);

    /// <summary>消耗配額（原子操作，回傳 true = 成功）</summary>
    bool ConsumeQuota(string grantId);

    /// <summary>
    /// 把 session 仍有效（Active 且未過期）的授予延長到 <paramref name="expiresAt"/>，與 session 的 heartbeat 同步；
    /// 已過期、已撤銷或已耗盡的授予不會被恢復。回傳延長的筆數。
    /// </summary>
    int ExtendSessionGrants(string sessionId, DateTime expiresAt);

    /// <summary>
    /// 撤銷 session 仍為 Active 的授予（剩餘配額不能再被消耗，審批中的請求也拿不到授予）。回傳撤銷的筆數。
    /// </summary>
    int RevokeSessionGrants(string sessionId);
}
