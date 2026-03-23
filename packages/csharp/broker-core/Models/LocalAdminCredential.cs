using BaseOrm;

namespace BrokerCore.Models;

[Table("local_admin_credentials")]
public class LocalAdminCredential
{
    [Key(AutoIncrement = false)]
    [Column("credential_id")]
    public string CredentialId { get; set; } = "local_admin";

    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("password_salt")]
    public string PasswordSalt { get; set; } = string.Empty;

    [Column("hash_iterations")]
    public int HashIterations { get; set; } = 120000;

    [Column("must_change_password")]
    public bool MustChangePassword { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_password_change_at")]
    public DateTime? LastPasswordChangeAt { get; set; }
}
