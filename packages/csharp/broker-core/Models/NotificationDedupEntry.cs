using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 通知推播 dedup 紀錄（防 broker restart 後 in-memory state 清空、同樣 alert 連環重推）。
///
/// 用法：
///   1. NotificationDedupRepo.MarkSent(signature, channel) 推完後寫一筆
///   2. 下次推前 NotificationDedupRepo.IsSentWithin(signature, channel, window) check
///   3. broker 重啟也記得、不會再重推
///
/// Signature 格式建議：`{action}|{symbol}|{message_prefix_60_chars}` — 跟訊息內容綁定、
/// 不同錯誤訊息會分屬不同 signature。
///
/// Channel：`discord` / `line`、各 channel 獨立 dedup（同 alert 可同時推兩條通道）。
/// </summary>
[Table("notification_dedup")]
public class NotificationDedupEntry
{
    /// <summary>合 channel+signature 當 key — `{channel}::{signature}`</summary>
    [Key]
    [Column("dedup_key")]
    [MaxLength(200)]
    public string DedupKey { get; set; } = "";

    [Column("channel")]
    [MaxLength(20)]
    public string Channel { get; set; } = "";

    [Column("signature")]
    [MaxLength(180)]
    public string Signature { get; set; } = "";

    [Column("last_sent_at")]
    public DateTime LastSentAt { get; set; } = DateTime.UtcNow;
}
