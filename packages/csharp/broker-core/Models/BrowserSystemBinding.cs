using BaseOrm;

namespace BrokerCore.Models;

[Table("browser_system_bindings")]
public class BrowserSystemBinding
{
    [Key(AutoIncrement = false)]
    [Column("system_binding_id")]
    public string SystemBindingId { get; set; } = string.Empty;

    [Column("display_name")]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Column("site_binding_id")]
    public string? SiteBindingId { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "active";

    [Column("secret_ref")]
    [MaxLength(200)]
    public string SecretRef { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
