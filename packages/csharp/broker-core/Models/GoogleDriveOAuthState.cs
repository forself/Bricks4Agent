using BaseOrm;

namespace BrokerCore.Models;

[Table("google_drive_oauth_states")]
public class GoogleDriveOAuthState
{
    [Key(AutoIncrement = false)]
    [Column("state_id")]
    public string StateId { get; set; } = string.Empty;

    [Column("channel")]
    [MaxLength(50)]
    public string Channel { get; set; } = "line";

    [Column("user_id")]
    [MaxLength(200)]
    public string UserId { get; set; } = string.Empty;

    [Column("redirect_uri")]
    [MaxLength(500)]
    public string RedirectUri { get; set; } = string.Empty;

    [Column("state_token")]
    [MaxLength(200)]
    public string StateToken { get; set; } = string.Empty;

    [Column("oauth_state")]
    [MaxLength(50)]
    public string OAuthState { get; set; } = "pending";

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
