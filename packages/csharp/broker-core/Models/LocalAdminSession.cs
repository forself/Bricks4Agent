using BaseOrm;

namespace BrokerCore.Models;

[Table("local_admin_sessions")]
public class LocalAdminSession
{
    [Key(AutoIncrement = false)]
    [Column("session_id")]
    public string SessionId { get; set; } = string.Empty;

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
