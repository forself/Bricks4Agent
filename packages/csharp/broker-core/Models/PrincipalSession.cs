using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Principal 登入後的 cookie session。Cookie 裡只放 sessionId.token、token hash 存這裡、
/// 比對時間 constant-time 防 timing attack。預設 12h 過期、logout 寫 RevokedAt。
/// </summary>
[Table("principal_sessions")]
public class PrincipalSession
{
    [Key(AutoIncrement = false)]
    [Column("session_id")]
    [MaxLength(40)]
    public string SessionId { get; set; } = string.Empty;

    [Column("principal_id")]
    [Required]
    [MaxLength(80)]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("role")]
    [MaxLength(20)]
    public string Role { get; set; } = "user";

    [Column("token_hash")]
    [Required]
    public string TokenHash { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    [Column("revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [Column("ip_address")]
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    [MaxLength(200)]
    public string? UserAgent { get; set; }
}
