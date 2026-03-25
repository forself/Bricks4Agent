using BaseOrm;

namespace BrokerCore.Models;

[Table("google_drive_delegated_credentials")]
public class GoogleDriveDelegatedCredential
{
    [Key(AutoIncrement = false)]
    [Column("credential_id")]
    public string CredentialId { get; set; } = string.Empty;

    [Column("channel")]
    [MaxLength(50)]
    public string Channel { get; set; } = "line";

    [Column("user_id")]
    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    [Column("google_email")]
    [MaxLength(300)]
    public string GoogleEmail { get; set; } = string.Empty;

    [Column("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [Column("scope")]
    [MaxLength(500)]
    public string Scope { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "active";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
