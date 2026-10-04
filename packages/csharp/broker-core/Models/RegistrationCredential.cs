using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Session 註冊憑證：每組 principal＋task 的高熵密鑰，broker 只保存其 SHA-256 雜湊。
/// 可重複使用直到到期或撤銷（容器重啟、compose 再次 up 都要能再註冊）；每一把都有到期時間。
/// 明文只在簽發當下交給持有者一次，不寫入資料庫、log 或稽核。
/// </summary>
[Table("registration_credentials")]
public class RegistrationCredential
{
    [Key(AutoIncrement = false)]
    [Column("credential_id")]
    public string CredentialId { get; set; } = string.Empty;

    [Column("principal_id")]
    [Required]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("task_id")]
    [Required]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>密鑰（UTF-8）的 SHA-256，小寫十六進位</summary>
    [Column("secret_hash")]
    [Required]
    public string SecretHash { get; set; } = string.Empty;

    /// <summary>來源：development_seed、dashboard_seed、agent_spawn、admin_issue</summary>
    [Column("source")]
    public string Source { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [Column("revoked_by")]
    public string RevokedBy { get; set; } = string.Empty;

    [Column("revoke_reason")]
    public string RevokeReason { get; set; } = string.Empty;

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }

    [Column("use_count")]
    public int UseCount { get; set; }
}
