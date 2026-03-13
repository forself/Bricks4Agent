using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 代理容器 Session —— 短時效、可撤銷
/// 叢集化：所有狀態外部化於 DB（encrypted_session_key、last_seen_seq）
/// </summary>
[Table("container_sessions")]
public class ContainerSession
{
    [Key(AutoIncrement = false)]
    [Column("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [Column("task_id")]
    [Required]
    public string TaskId { get; set; } = string.Empty;

    [Column("principal_id")]
    [Required]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("role_id")]
    [Required]
    public string RoleId { get; set; } = string.Empty;

    /// <summary>Scoped Token 的 JTI（用於撤銷追蹤）</summary>
    [Column("token_jti")]
    [MaxLength(200)]
    public string TokenJti { get; set; } = string.Empty;

    /// <summary>發行時的 system epoch（kill switch 檢查用）</summary>
    [Column("epoch_at_issue")]
    public int EpochAtIssue { get; set; }

    /// <summary>
    /// 加密後的 session_key（AES-256 用 broker 主金鑰加密）
    /// 所有 broker instance 共享同一主金鑰，故可在任意節點解密
    /// </summary>
    [Column("encrypted_session_key")]
    public string EncryptedSessionKey { get; set; } = string.Empty;

    /// <summary>
    /// 最後確認的訊息序號（防 replay）
    /// DB 原子更新：UPDATE ... SET last_seen_seq = @new WHERE last_seen_seq = @old
    /// </summary>
    [Column("last_seen_seq")]
    public int LastSeenSeq { get; set; } = 0;

    [Column("status")]
    public int StatusValue { get; set; }

    [Ignore]
    public SessionStatus Status
    {
        get => (SessionStatus)StatusValue;
        set => StatusValue = (int)value;
    }

    [Column("registered_at")]
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    [Column("last_heartbeat")]
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }
}
