using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 計畫節點 —— DAG 中的一個執行單元
///
/// 每個 PlanNode 對應一個 ExecutionRequest（執行後填入 RequestId）。
/// Ordinal 由 ValidateDag() 的拓撲排序計算。
/// OutputContextKey 指定此節點的結果寫入 SharedContext 時使用的 key。
/// </summary>
[Table("plan_nodes")]
public class PlanNode
{
    [Key(AutoIncrement = false)]
    [Column("node_id")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>所屬計畫</summary>
    [Column("plan_id")]
    [Required]
    public string PlanId { get; set; } = string.Empty;

    /// <summary>拓撲排序序號（ValidateDag 計算）</summary>
    [Column("ordinal")]
    public int Ordinal { get; set; }

    /// <summary>要執行的能力 ID</summary>
    [Column("capability_id")]
    [Required]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>意圖描述（人可讀）</summary>
    [Column("intent")]
    [MaxLength(500)]
    public string Intent { get; set; } = string.Empty;

    /// <summary>請求 payload（JSON）</summary>
    [Column("request_payload")]
    public string RequestPayload { get; set; } = "{}";

    /// <summary>節點狀態（int 存儲）</summary>
    [Column("state")]
    public int StateValue { get; set; }

    [Ignore]
    public NodeState State
    {
        get => (NodeState)StateValue;
        set => StateValue = (int)value;
    }

    /// <summary>執行後關聯的 ExecutionRequest ID</summary>
    [Column("request_id")]
    public string? RequestId { get; set; }

    /// <summary>輸出寫入 SharedContext 的 key（null = 不寫出）</summary>
    [Column("output_context_key")]
    public string? OutputContextKey { get; set; }

    /// <summary>已重試次數</summary>
    [Column("retry_count")]
    public int RetryCount { get; set; }

    /// <summary>最大重試次數</summary>
    [Column("max_retries")]
    public int MaxRetries { get; set; } = 1;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
