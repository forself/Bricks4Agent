using BaseOrm;

namespace BrokerCore.Models;

[Table("browser_session_leases")]
public class BrowserSessionLease
{
    [Key(AutoIncrement = false)]
    [Column("session_lease_id")]
    public string SessionLeaseId { get; set; } = string.Empty;

    [Column("tool_id")]
    [MaxLength(200)]
    public string ToolId { get; set; } = string.Empty;

    [Column("site_binding_id")]
    public string? SiteBindingId { get; set; }

    [Column("principal_id")]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("identity_mode")]
    [MaxLength(50)]
    public string IdentityMode { get; set; } = string.Empty;

    [Column("lease_state")]
    [MaxLength(50)]
    public string LeaseState { get; set; } = "active";

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
