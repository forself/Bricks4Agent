using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 計畫邊 —— DAG 中的因果依賴
///
/// 三種邊類型：
/// - DataFlow：上游 output → SharedContext → 下游 input（透過 ContextKey 傳遞）
/// - ControlFlow：純序列依賴（無資料傳遞，僅保證順序）
/// - ApprovalGate：需人工審批才放行（Phase 5）
/// </summary>
[Table("plan_edges")]
public class PlanEdge
{
    [Key(AutoIncrement = false)]
    [Column("edge_id")]
    public string EdgeId { get; set; } = string.Empty;

    /// <summary>所屬計畫</summary>
    [Column("plan_id")]
    [Required]
    public string PlanId { get; set; } = string.Empty;

    /// <summary>來源節點 ID</summary>
    [Column("from_node_id")]
    [Required]
    public string FromNodeId { get; set; } = string.Empty;

    /// <summary>目標節點 ID</summary>
    [Column("to_node_id")]
    [Required]
    public string ToNodeId { get; set; } = string.Empty;

    /// <summary>邊類型（int 存儲）</summary>
    [Column("edge_type")]
    public int EdgeTypeValue { get; set; }

    [Ignore]
    public EdgeType EdgeType
    {
        get => (EdgeType)EdgeTypeValue;
        set => EdgeTypeValue = (int)value;
    }

    /// <summary>DataFlow 邊：傳遞的 SharedContext key</summary>
    [Column("context_key")]
    public string? ContextKey { get; set; }

    /// <summary>條件表達式（JSON，Phase 5 擴充）</summary>
    [Column("condition")]
    public string? Condition { get; set; }
}
