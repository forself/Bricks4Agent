namespace BrokerCore.Services;

/// <summary>
/// Capability 角色存取控制（ACL）。
///
/// PoolDispatcher 在派送 capability 前查一次：呼叫者的 role 是否被允許呼叫此 capability。
/// 不通過 → DISPATCH_DENIED 事件 + ExecutionResult.Fail。
///
/// 設計成 fail-open：role 為空 / "role_admin" / "system" → 永遠 allow。
/// 只有 explicit 非 admin role（例如 "role_user"）才會走白名單檢查。
/// 這讓既有大量「PrincipalId="system"」的 dashboard 直呼路徑不需要動就能保持原行為。
///
/// 多用戶 SaaS 階段：把 dashboard endpoint 從 hardcode "system" 改成從 cookie 讀真實
/// (principal_id, role)、就會自動受 ACL 約束。
/// </summary>
public interface ICapabilityAclService
{
    /// <summary>是否允許指定 role 呼叫指定 capability。</summary>
    bool IsAllowed(string? role, string capabilityId);

    /// <summary>取得目前 ACL 規則表（給 dashboard 顯示）。</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> GetRules();
}

/// <summary>
/// 預設實作——規則表寫死在 code 裡（之後可改成 DB-backed 由 admin 調整）。
///
/// Pattern 語法：
///   "*"        → 全部 capability
///   "trading.*" → trading 開頭的所有 capability
///   "trading.account" → 完全比對
///
/// 預設規則（KISS）：
///   role_admin / system → ["*"]                                  全部
///   role_user           → ["strategy.signal", "quote.*",          看訊號/行情/自己帳戶
///                          "trading.account",                     讀部位、不能下單
///                          "trading.perpetual"]                   perp 互動（trading-worker
///                                                                 內部還是 owner-filter）
///   其他/空              → fail-open（視為 admin）
///
/// trading.order 故意不給 user——多用戶 SaaS 階段不開放手動下單，只能透過 AutoTrader
/// （受 risk rule + portfolio circuit breaker 約束）。
/// </summary>
public sealed class CapabilityAclService : ICapabilityAclService
{
    private static readonly Dictionary<string, string[]> Rules =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["role_admin"] = new[] { "*" },
        ["system"]     = new[] { "*" },
        ["role_user"]  = new[]
        {
            "strategy.signal",
            "quote.*",
            "trading.account",
            "trading.perpetual",
        },
        ["role_guest"] = Array.Empty<string>(),
    };

    public bool IsAllowed(string? role, string capabilityId)
    {
        // fail-open：沒設 role 視為內部呼叫、放行
        if (string.IsNullOrEmpty(role)) return true;

        if (!Rules.TryGetValue(role, out var patterns)) return true;  // 不認識的 role → fail-open

        return patterns.Any(p => MatchPattern(p, capabilityId));
    }

    private static bool MatchPattern(string pattern, string capability)
    {
        if (pattern == "*") return true;
        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^2] + ".";
            return capability.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(pattern, capability, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetRules()
        => Rules.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.ToList());
}
