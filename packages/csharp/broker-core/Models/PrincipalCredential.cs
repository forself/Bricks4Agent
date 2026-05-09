using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Principal 的登入憑證——多用戶版（vs LocalAdminCredential 的 single-row local-only 版）。
/// 每個 PrincipalId（dashboard user）一筆。Phase A1 只記密碼 hash + role；
/// Phase A2 會增「最後登入 IP / 失敗鎖定」之類。
///
/// Role 語意（Phase C 混合模型）：
///   - "admin"：看全部 watches / positions / 所有 user 的 lab 結果
///   - "user" ：只看自己 user_id 標的（A2 補資料隔離）
///   - "viewer"：read-only 子集（之後）
/// </summary>
[Table("principal_credentials")]
public class PrincipalCredential
{
    [Key(AutoIncrement = false)]
    [Column("principal_id")]
    [MaxLength(80)]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("password_hash")]
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("password_salt")]
    [Required]
    public string PasswordSalt { get; set; } = string.Empty;

    [Column("hash_iterations")]
    public int HashIterations { get; set; } = 120000;

    [Column("role")]
    [MaxLength(20)]
    public string Role { get; set; } = "user";

    [Column("display_name")]
    [MaxLength(80)]
    public string? DisplayName { get; set; }

    [Column("must_change_password")]
    public bool MustChangePassword { get; set; }

    [Column("disabled")]
    public bool Disabled { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_password_change_at")]
    public DateTime? LastPasswordChangeAt { get; set; }

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }
}
