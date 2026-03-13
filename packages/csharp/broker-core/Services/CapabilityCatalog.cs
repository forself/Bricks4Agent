using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 能力目錄 —— DB 驅動的白名單
///
/// 所有 Agent 可執行的工具/動作必須先在此目錄中註冊，
/// 並附帶 JSON Schema、風險等級、配額限制。
/// 未註冊的能力一律拒絕。
/// </summary>
public class CapabilityCatalog : ICapabilityCatalog
{
    private readonly BrokerDb _db;

    public CapabilityCatalog(BrokerDb db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Capability? GetCapability(string capabilityId)
    {
        return _db.Get<Capability>(capabilityId);
    }

    /// <inheritdoc />
    public List<Capability> ListCapabilities(string? filter = null)
    {
        if (string.IsNullOrEmpty(filter))
            return _db.GetAll<Capability>();

        return _db.Query<Capability>(
            "SELECT * FROM capabilities WHERE capability_id LIKE @pattern OR route LIKE @pattern",
            new { pattern = $"%{filter}%" });
    }

    /// <inheritdoc />
    public CapabilityGrant? GetActiveGrant(
        string principalId, string taskId, string sessionId, string capabilityId)
    {
        return _db.QueryFirst<CapabilityGrant>(
            @"SELECT * FROM capability_grants
              WHERE principal_id = @principalId
                AND task_id = @taskId
                AND session_id = @sessionId
                AND capability_id = @capabilityId
                AND status = 0
                AND expires_at > @now",
            new { principalId, taskId, sessionId, capabilityId, now = DateTime.UtcNow });
    }

    /// <inheritdoc />
    public CapabilityGrant CreateGrant(
        string taskId, string sessionId, string principalId,
        string capabilityId, string scopeOverride, int quota, DateTime expiresAt)
    {
        var grant = new CapabilityGrant
        {
            GrantId = IdGen.New("grt"),
            TaskId = taskId,
            SessionId = sessionId,
            PrincipalId = principalId,
            CapabilityId = capabilityId,
            ScopeOverride = scopeOverride,
            RemainingQuota = quota,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            Status = GrantStatus.Active
        };

        _db.Insert(grant);
        return grant;
    }

    /// <inheritdoc />
    public bool ConsumeQuota(string grantId)
    {
        // -1 = 無限配額
        var grant = _db.Get<CapabilityGrant>(grantId);
        if (grant == null || grant.Status != GrantStatus.Active)
            return false;

        if (grant.RemainingQuota == -1)
            return true; // 無限制

        // 原子消耗：remaining_quota > 0 才更新
        var affected = _db.Execute(
            "UPDATE capability_grants SET remaining_quota = remaining_quota - 1 WHERE grant_id = @gid AND remaining_quota > 0 AND status = 0",
            new { gid = grantId });

        if (affected > 0)
        {
            // 檢查是否耗盡
            var remaining = _db.Scalar<int>(
                "SELECT remaining_quota FROM capability_grants WHERE grant_id = @gid",
                new { gid = grantId });

            if (remaining <= 0)
            {
                _db.Execute(
                    "UPDATE capability_grants SET status = @exhausted WHERE grant_id = @gid",
                    new { exhausted = (int)GrantStatus.Exhausted, gid = grantId });
            }
        }

        return affected > 0;
    }
}
