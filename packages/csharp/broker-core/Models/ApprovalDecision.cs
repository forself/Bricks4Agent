using BaseOrm;

namespace BrokerCore.Models;

[Table("approval_decisions")]
public class ApprovalDecision
{
    [Key(AutoIncrement = false)]
    [Column("decision_id")]
    public string DecisionId { get; set; } = string.Empty;

    [Column("approval_id")]
    [Required]
    public string ApprovalId { get; set; } = string.Empty;

    [Column("approver_id")]
    [Required]
    public string ApproverId { get; set; } = string.Empty;

    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    [Column("decision")]
    public int DecisionValue { get; set; }

    [Ignore]
    public ApprovalDecisionKind Decision
    {
        get => (ApprovalDecisionKind)DecisionValue;
        set => DecisionValue = (int)value;
    }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
