using BaseOrm;

namespace BrokerCore.Models;

/// <summary>任務（收件、分類、指派、完成）</summary>
[Table("broker_tasks")]
public class BrokerTask
{
    [Key(AutoIncrement = false)]
    [Column("task_id")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>任務類型（query, analysis, code_gen, doc_gen 等）</summary>
    [Column("task_type")]
    [MaxLength(100)]
    public string TaskType { get; set; } = string.Empty;

    [Column("submitted_by")]
    [Required]
    public string SubmittedBy { get; set; } = string.Empty;

    [Column("risk_level")]
    public int RiskLevelValue { get; set; }

    [Ignore]
    public RiskLevel RiskLevel
    {
        get => (RiskLevel)RiskLevelValue;
        set => RiskLevelValue = (int)value;
    }

    [Column("state")]
    public int StateValue { get; set; }

    [Ignore]
    public TaskState State
    {
        get => (TaskState)StateValue;
        set => StateValue = (int)value;
    }

    /// <summary>任務範圍描述（JSON，例如 {"paths": [...], "routes": [...], "resources": [...]}）</summary>
    [Column("scope_descriptor")]
    public string ScopeDescriptor { get; set; } = "{}";

    [Column("assigned_principal_id")]
    public string? AssignedPrincipalId { get; set; }

    [Column("assigned_role_id")]
    public string? AssignedRoleId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
}
