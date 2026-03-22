using BaseOrm;

namespace BrokerCore.Models;

[Table("browser_site_bindings")]
public class BrowserSiteBinding
{
    [Key(AutoIncrement = false)]
    [Column("site_binding_id")]
    public string SiteBindingId { get; set; } = string.Empty;

    [Column("display_name")]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Column("identity_mode")]
    [MaxLength(50)]
    public string IdentityMode { get; set; } = string.Empty;

    [Column("site_class")]
    [MaxLength(100)]
    public string SiteClass { get; set; } = string.Empty;

    [Column("origin")]
    [MaxLength(500)]
    public string Origin { get; set; } = string.Empty;

    [Column("principal_id")]
    public string? PrincipalId { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "active";

    [Column("metadata_json")]
    public string MetadataJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
