namespace BrokerCore.Models;

/// <summary>
/// 每個 principal 的推播目標（多用戶:朋友收自己的告警 / 每日彙整到自己的頻道）。
/// target（Discord webhook URL / LINE token）視為 secret、用 AtRestSecretCrypto 加密存。
/// </summary>
public class NotificationChannel
{
    /// <summary>{owner}:{type}:{guid} — 主鍵。</summary>
    public string EntryId { get; set; } = string.Empty;

    /// <summary>這個推播頻道屬於哪個 principal。</summary>
    public string OwnerPrincipalId { get; set; } = string.Empty;

    /// <summary>discord（webhook URL）/ line（messaging push token；MVP 先支援 discord 路由）。</summary>
    public string ChannelType { get; set; } = "discord";

    /// <summary>自己取的辨識名。</summary>
    public string? Label { get; set; }

    /// <summary>加密的推播目標:Discord webhook URL / LINE token。base64(nonce|ct|tag)。</summary>
    public string TargetEnc { get; set; } = string.Empty;

    /// <summary>暫停（保留設定不刪）。</summary>
    public bool Disabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
