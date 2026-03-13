using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 計畫 —— DAG 容器
///
/// 一個 Plan 綁定一個 BrokerTask，包含多個 PlanNode（節點）和 PlanEdge（邊）。
/// PlanEngine 按拓撲序逐節點裁決 + 執行。
/// </summary>
[Table("plans")]
public class Plan
{
    [Key(AutoIncrement = false)]
    [Column("plan_id")]
    public string PlanId { get; set; } = string.Empty;

    /// <summary>關聯的 BrokerTask</summary>
    [Column("task_id")]
    [Required]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>提交者 principal_id</summary>
    [Column("submitted_by")]
    [Required]
    public string SubmittedBy { get; set; } = string.Empty;

    /// <summary>計畫標題</summary>
    [Column("title")]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>計畫描述</summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>計畫狀態（int 存儲）</summary>
    [Column("state")]
    public int StateValue { get; set; }

    [Ignore]
    public PlanState State
    {
        get => (PlanState)StateValue;
        set => StateValue = (int)value;
    }

    /// <summary>節點總數</summary>
    [Column("total_nodes")]
    public int TotalNodes { get; set; }

    /// <summary>已完成節點數</summary>
    [Column("completed_nodes")]
    public int CompletedNodes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
