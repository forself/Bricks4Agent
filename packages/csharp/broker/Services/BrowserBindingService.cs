using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class BrowserBindingService
{
    private readonly BrokerDb _db;

    public BrowserBindingService(BrokerDb db)
    {
        _db = db;
    }

    public IReadOnlyList<BrowserSiteBinding> ListSiteBindings(string? identityMode = null, string? principalId = null)
    {
        var sql = "SELECT * FROM browser_site_bindings WHERE 1=1";
        var args = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(identityMode))
        {
            sql += " AND identity_mode = @identityMode";
            args["identityMode"] = identityMode;
        }

        if (!string.IsNullOrWhiteSpace(principalId))
        {
            sql += " AND principal_id = @principalId";
            args["principalId"] = principalId;
        }

        sql += " ORDER BY created_at DESC";
        return _db.Query<BrowserSiteBinding>(sql, args);
    }

    public BrowserSiteBinding? GetSiteBinding(string siteBindingId)
        => _db.Get<BrowserSiteBinding>(siteBindingId);

    public BrowserSiteBinding UpsertSiteBinding(BrowserSiteBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.SiteBindingId))
        {
            binding.SiteBindingId = BrokerCore.IdGen.New("bsb");
            binding.CreatedAt = DateTime.UtcNow;
            _db.Insert(binding);
            return binding;
        }

        var existing = _db.Get<BrowserSiteBinding>(binding.SiteBindingId);
        if (existing == null)
        {
            binding.CreatedAt = binding.CreatedAt == default ? DateTime.UtcNow : binding.CreatedAt;
            _db.Insert(binding);
            return binding;
        }

        existing.DisplayName = binding.DisplayName;
        existing.IdentityMode = binding.IdentityMode;
        existing.SiteClass = binding.SiteClass;
        existing.Origin = binding.Origin;
        existing.PrincipalId = binding.PrincipalId;
        existing.Status = binding.Status;
        existing.MetadataJson = string.IsNullOrWhiteSpace(binding.MetadataJson) ? "{}" : binding.MetadataJson;
        _db.Update(existing);
        return existing;
    }

    public IReadOnlyList<BrowserUserGrant> ListUserGrants(string? principalId = null)
    {
        var sql = "SELECT * FROM browser_user_grants WHERE 1=1";
        var args = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(principalId))
        {
            sql += " AND principal_id = @principalId";
            args["principalId"] = principalId;
        }

        sql += " ORDER BY created_at DESC";
        return _db.Query<BrowserUserGrant>(sql, args);
    }

    public BrowserUserGrant? GetUserGrant(string userGrantId)
        => _db.Get<BrowserUserGrant>(userGrantId);

    public BrowserUserGrant UpsertUserGrant(BrowserUserGrant grant)
    {
        if (string.IsNullOrWhiteSpace(grant.UserGrantId))
        {
            grant.UserGrantId = BrokerCore.IdGen.New("bug");
            grant.CreatedAt = DateTime.UtcNow;
            _db.Insert(grant);
            return grant;
        }

        var existing = _db.Get<BrowserUserGrant>(grant.UserGrantId);
        if (existing == null)
        {
            grant.CreatedAt = grant.CreatedAt == default ? DateTime.UtcNow : grant.CreatedAt;
            _db.Insert(grant);
            return grant;
        }

        existing.PrincipalId = grant.PrincipalId;
        existing.SiteBindingId = grant.SiteBindingId;
        existing.Status = grant.Status;
        existing.ConsentRef = grant.ConsentRef;
        existing.ScopesJson = string.IsNullOrWhiteSpace(grant.ScopesJson) ? "{}" : grant.ScopesJson;
        existing.ExpiresAt = grant.ExpiresAt;
        _db.Update(existing);
        return existing;
    }

    public IReadOnlyList<BrowserSystemBinding> ListSystemBindings()
        => _db.Query<BrowserSystemBinding>("SELECT * FROM browser_system_bindings ORDER BY created_at DESC");

    public BrowserSystemBinding? GetSystemBinding(string systemBindingId)
        => _db.Get<BrowserSystemBinding>(systemBindingId);

    public BrowserSystemBinding UpsertSystemBinding(BrowserSystemBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.SystemBindingId))
        {
            binding.SystemBindingId = BrokerCore.IdGen.New("bsys");
            binding.CreatedAt = DateTime.UtcNow;
            _db.Insert(binding);
            return binding;
        }

        var existing = _db.Get<BrowserSystemBinding>(binding.SystemBindingId);
        if (existing == null)
        {
            binding.CreatedAt = binding.CreatedAt == default ? DateTime.UtcNow : binding.CreatedAt;
            _db.Insert(binding);
            return binding;
        }

        existing.DisplayName = binding.DisplayName;
        existing.SiteBindingId = binding.SiteBindingId;
        existing.Status = binding.Status;
        existing.SecretRef = binding.SecretRef;
        _db.Update(existing);
        return existing;
    }

    public IReadOnlyList<BrowserSessionLease> ListSessionLeases(string? principalId = null)
    {
        var sql = "SELECT * FROM browser_session_leases WHERE 1=1";
        var args = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(principalId))
        {
            sql += " AND principal_id = @principalId";
            args["principalId"] = principalId;
        }

        sql += " ORDER BY created_at DESC";
        return _db.Query<BrowserSessionLease>(sql, args);
    }

    public BrowserSessionLease? GetSessionLease(string sessionLeaseId)
        => _db.Get<BrowserSessionLease>(sessionLeaseId);

    public BrowserSessionLease IssueSessionLease(
        string toolId,
        string principalId,
        string identityMode,
        DateTime expiresAt,
        string? siteBindingId = null)
    {
        var lease = new BrowserSessionLease
        {
            SessionLeaseId = BrokerCore.IdGen.New("bls"),
            ToolId = toolId,
            SiteBindingId = siteBindingId,
            PrincipalId = principalId,
            IdentityMode = identityMode,
            LeaseState = "active",
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };
        _db.Insert(lease);
        return lease;
    }

    public BrowserSessionLease? RevokeSessionLease(string sessionLeaseId)
    {
        var existing = _db.Get<BrowserSessionLease>(sessionLeaseId);
        if (existing == null)
            return null;

        existing.LeaseState = "revoked";
        existing.LastUsedAt = DateTime.UtcNow;
        _db.Update(existing);
        return existing;
    }
}
