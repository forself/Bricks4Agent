using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 檢查點 —— 節點完成後的快照
///
/// 用途：
/// - 因果鏈可追溯（每個節點完成時記錄快照）
/// - 未來支援 rollback（Phase 5+）
/// </summary>
[Table("checkpoints")]
public class Checkpoint
{
    [Key(AutoIncrement = false)]
    [Column("checkpoint_id")]
    public string CheckpointId { get; set; } = string.Empty;

    /// <summary>所屬計畫</summary>
    [Column("plan_id")]
    [Required]
    public string PlanId { get; set; } = string.Empty;

    /// <summary>對應節點</summary>
    [Column("node_id")]
    [Required]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>檢查點狀態（int 存儲）</summary>
    [Column("state")]
    public int StateValue { get; set; }

    [Ignore]
    public CheckpointState State
    {
        get => (CheckpointState)StateValue;
        set => StateValue = (int)value;
    }

    /// <summary>節點完成時的狀態快照（JSON）</summary>
    [Column("snapshot_ref")]
    public string SnapshotRef { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>L-3 修復：Phase 5 需要狀態更新追蹤（Captured → Verified → RolledBack）</summary>
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
