using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 每個 principal 的推播目標（多用戶:朋友收自己的告警 / 每日彙整到自己的頻道）。
/// target（Discord webhook URL / LINE token）視為 secret、用 AtRestSecretCrypto 加密存、
/// AAD 綁 entry_id + channel_type。
/// </summary>
[Table("notification_channels")]
public class NotificationChannel
{
    [Key(AutoIncrement = false)]
    [Column("entry_id")]
    [MaxLength(64)]
    public string EntryId { get; set; } = string.Empty;       // {owner}:{type}:{guid}

    [Column("owner_principal_id")]
    [Required]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = string.Empty;

    [Column("channel_type")]
    [Required]
    [MaxLength(20)]
    public string ChannelType { get; set; } = "discord";      // discord（webhook URL）/ line（token）

    [Column("label")]
    [MaxLength(80)]
    public string? Label { get; set; }

    [Column("target_enc")]
    [Required]
    public string TargetEnc { get; set; } = string.Empty;     // base64(nonce|ct|tag) — webhook URL / token

    [Column("disabled")]
    public bool Disabled { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
