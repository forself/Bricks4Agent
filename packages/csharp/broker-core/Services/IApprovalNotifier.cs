using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 審批通知 seam(§18.2-C2)—— User 層審批建立時,通知該使用者(經 LINE 送簽章連結)。
/// broker-core 定義介面;Broker 層以 LINE 通知佇列 + 簽章連結實作。
/// BrokerService 以可選相依注入,缺省則不通知(降級)。
/// </summary>
public interface IApprovalNotifier
{
    /// <summary>User 層待審建立後呼叫:把可看內容的審批連結送給擁有者。</summary>
    void NotifyUserApprovalCreated(ApprovalRequest approval);
}
