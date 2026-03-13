using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>撤權 + Epoch 服務</summary>
public interface IRevocationService
{
    /// <summary>記錄撤權</summary>
    Revocation Revoke(RevocationTargetType targetType, string targetId, string reason, string revokedBy);

    /// <summary>檢查目標是否已被撤權</summary>
    bool IsRevoked(string targetId);

    /// <summary>取得當前 system epoch</summary>
    int GetCurrentEpoch();

    /// <summary>
    /// Kill Switch：遞增 system epoch（即時失效所有舊 token）
    /// 回傳新的 epoch 值
    /// </summary>
    int IncrementEpoch(string triggeredBy, string reason);
}
