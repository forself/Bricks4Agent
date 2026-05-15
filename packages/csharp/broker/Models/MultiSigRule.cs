using BaseOrm;

namespace Broker.Models;

/// <summary>
/// I1（minimal）— Multi-sig 規則：指定某 capability 需要 N 個 admin 簽核才放行。
///
/// 例：trading.order min_approvers=2 → 任一 admin 按 approve 不放行、要 ≥2 個 admin 都按才會
/// 真的把 approval 標 approved（dispatch 才會放行）。
///
/// 為什麼需要：單人決策的內部威脅 / 失誤代價在真錢額度上太高、
/// 兩人覆核制是金融業標準（也是 Benson 治理框架可長出來的方向）。
///
/// 不影響原 ACL：
/// - 沒有 rule 或 min_approvers ≤ 1 → 走原 ApprovalService.Approve（一個 admin 即可放行）
/// - 有 rule → 中間累積 ApprovalDecisionRecord、達門檻才呼叫 inner.Approve、
///   任一 reject 立刻呼叫 inner.Reject
/// </summary>
[Table("multi_sig_rules")]
public class MultiSigRule
{
    [Key(AutoIncrement = false), MaxLength(80)]
    [Column("capability_id")]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>需要的最少簽核人數（&gt;= 1）。1 = 等同單人決策（rule 形同虛設、但允許 admin 顯式記錄）</summary>
    [Column("min_approvers")]
    public int MinApprovers { get; set; } = 2;

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("description"), MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Column("created_by"), MaxLength(64)]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
