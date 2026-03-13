using BrokerCore.Contracts;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 政策裁決引擎 —— 確定性規則（Phase 1: 7 條規則）
/// ML 僅用於建議性風險評分（Phase 3），絕不用於授權決策
/// </summary>
public interface IPolicyEngine
{
    /// <summary>
    /// 評估執行請求
    /// </summary>
    /// <param name="request">執行請求</param>
    /// <param name="capability">能力定義</param>
    /// <param name="grant">能力授予</param>
    /// <param name="task">關聯任務</param>
    /// <param name="currentEpoch">當前系統紀元</param>
    /// <param name="tokenEpoch">Token 發行時的紀元</param>
    PolicyResult Evaluate(
        ExecutionRequest request,
        Capability capability,
        CapabilityGrant grant,
        BrokerTask task,
        int currentEpoch,
        int tokenEpoch);
}
