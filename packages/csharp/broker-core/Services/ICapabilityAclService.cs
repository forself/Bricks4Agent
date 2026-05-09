using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Capability 角色存取控制（ACL）+ 個別 principal 覆寫。
///
/// PoolDispatcher 在派送 capability 前查一次。決策順序：
///   1. principal-specific deny override → 拒絕（最強、覆蓋 role 允許）
///   2. principal-specific allow override → 允許（覆蓋 role 拒絕）
///   3. role-based whitelist → 走 fail-open by design（empty/admin/system → allow）
///
/// 不通過 → DISPATCH_DENIED 事件 + ExecutionResult.Fail。
/// </summary>
public interface ICapabilityAclService
{
    /// <summary>是否允許指定 (principal, role) 呼叫指定 capability。</summary>
    bool IsAllowed(string? principalId, string? role, string capabilityId);

    /// <summary>取得目前 ACL 角色規則表（給 dashboard 顯示）。</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> GetRules();

    /// <summary>列指定 principal 的覆寫規則。null = 全部。</summary>
    List<PrincipalCapabilityOverride> ListOverrides(string? principalId = null);

    /// <summary>新增 / 更新 override（同 principal_id + capability_pattern 視為 upsert）。</summary>
    PrincipalCapabilityOverride AddOverride(
        string principalId, string capabilityPattern, string action,
        string createdBy, string? reason = null);

    /// <summary>刪除 override。</summary>
    bool RemoveOverride(string overrideId);
}

/// <summary>
/// 預設實作：role 規則寫死、principal override 走 BrokerDb 動態管理。
/// </summary>
public sealed class CapabilityAclService : ICapabilityAclService
{
    private readonly BrokerDb _db;

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

    public CapabilityAclService(BrokerDb db) { _db = db; }

    public bool IsAllowed(string? principalId, string? role, string capabilityId)
    {
        // 1. principal-specific override 先查
        if (!string.IsNullOrEmpty(principalId))
        {
            var overrides = ListOverrides(principalId);
            // deny 優先（最強）
            if (overrides.Any(o => o.Action == "deny" && MatchPattern(o.CapabilityPattern, capabilityId)))
                return false;
            // allow override 直接放行（不再查 role）
            if (overrides.Any(o => o.Action == "allow" && MatchPattern(o.CapabilityPattern, capabilityId)))
                return true;
        }

        // 2. fail-open：沒設 role 視為內部呼叫、放行
        if (string.IsNullOrEmpty(role)) return true;

        // 3. 不認識的 role → fail-open
        if (!Rules.TryGetValue(role, out var patterns)) return true;

        // 4. 走 role 白名單
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

    public List<PrincipalCapabilityOverride> ListOverrides(string? principalId = null)
    {
        if (string.IsNullOrEmpty(principalId))
            return _db.Query<PrincipalCapabilityOverride>(
                "SELECT * FROM principal_capability_overrides ORDER BY created_at DESC");
        return _db.Query<PrincipalCapabilityOverride>(
            "SELECT * FROM principal_capability_overrides WHERE principal_id = @pid ORDER BY created_at DESC",
            new { pid = principalId });
    }

    public PrincipalCapabilityOverride AddOverride(
        string principalId, string capabilityPattern, string action,
        string createdBy, string? reason = null)
    {
        if (action != "allow" && action != "deny")
            throw new ArgumentException("action must be 'allow' or 'deny'");

        // upsert：同 (principal_id, capability_pattern) 視為更新
        var existing = _db.QueryFirst<PrincipalCapabilityOverride>(
            "SELECT * FROM principal_capability_overrides WHERE principal_id = @pid AND capability_pattern = @pat LIMIT 1",
            new { pid = principalId, pat = capabilityPattern });

        if (existing != null)
        {
            existing.Action = action;
            existing.CreatedAt = DateTime.UtcNow;
            existing.CreatedBy = createdBy;
            existing.Reason = reason;
            _db.Update(existing);
            return existing;
        }

        var entry = new PrincipalCapabilityOverride
        {
            OverrideId = IdGen.New("ovr"),
            PrincipalId = principalId,
            CapabilityPattern = capabilityPattern,
            Action = action,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            Reason = reason,
        };
        _db.Insert(entry);
        return entry;
    }

    public bool RemoveOverride(string overrideId)
    {
        var existing = _db.QueryFirst<PrincipalCapabilityOverride>(
            "SELECT * FROM principal_capability_overrides WHERE override_id = @id",
            new { id = overrideId });
        if (existing == null) return false;
        _db.Delete<PrincipalCapabilityOverride>(existing);
        return true;
    }
}
