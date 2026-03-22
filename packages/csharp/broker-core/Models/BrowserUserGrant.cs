using BaseOrm;

namespace BrokerCore.Models;

[Table("browser_user_grants")]
public class BrowserUserGrant
{
    [Key(AutoIncrement = false)]
    [Column("user_grant_id")]
    public string UserGrantId { get; set; } = string.Empty;

    [Column("principal_id")]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("site_binding_id")]
    public string? SiteBindingId { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "active";

    [Column("consent_ref")]
    [MaxLength(200)]
    public string ConsentRef { get; set; } = string.Empty;

    [Column("scopes_json")]
    public string ScopesJson { get; set; } = "{}";

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
