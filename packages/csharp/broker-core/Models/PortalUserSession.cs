using BaseOrm;

namespace BrokerCore.Models;

[Table("portal_user_sessions")]
public class PortalUserSession
{
    [Key(AutoIncrement = false)]
    [Column("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("token_hash")]
    public string TokenHash { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_seen_at")]
    public DateTime? LastSeenAt { get; set; }

    [Column("revoked_at")]
    public DateTime? RevokedAt { get; set; }
}
