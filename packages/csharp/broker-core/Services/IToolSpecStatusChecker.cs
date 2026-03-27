namespace BrokerCore.Services;

/// <summary>
/// 檢查 tool spec 狀態是否允許執行
///
/// BrokerService（broker-core）不直接依賴 IToolSpecRegistry（broker 層），
/// 透過此介面在 PEP Step 7 之後驗證 tool spec status。
///
/// 規則：
/// - 若 capabilityId 無對應 tool spec → 放行（允許 DB-seeded capability）
/// - 若 tool spec status 為 active / ready / beta → 放行
/// - 若 tool spec status 為 planned / draft 或其他 → 拒絕
/// </summary>
public interface IToolSpecStatusChecker
{
    /// <summary>
    /// 檢查指定 capability 的 tool spec 狀態是否允許執行
    /// </summary>
    /// <param name="capabilityId">能力 ID</param>
    /// <returns>
    /// (allowed, reason) — allowed=true 表示可執行；
    /// allowed=false 時 reason 包含拒絕原因
    /// </returns>
    (bool Allowed, string? Reason) CheckStatus(string capabilityId);
}
