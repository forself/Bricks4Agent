using BaseOrm;

namespace BrokerCore.Models;

[Table("portal_user_credentials")]
public class PortalUserCredential
{
    [Key(AutoIncrement = false)]
    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("password_salt")]
    public string PasswordSalt { get; set; } = string.Empty;

    [Column("hash_iterations")]
    public int HashIterations { get; set; } = 120000;

    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("disabled")]
    public bool Disabled { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }
}
