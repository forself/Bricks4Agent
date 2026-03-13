using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 能力定義 —— 能力目錄中的白名單項目
/// 含 JSON Schema 驗證、風險等級、配額、TTL
/// </summary>
[Table("capabilities")]
public class Capability
{
    [Key(AutoIncrement = false)]
    [Column("capability_id")]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>對應的路由/工具名稱</summary>
    [Column("route")]
    [MaxLength(500)]
    public string Route { get; set; } = string.Empty;

    [Column("action_type")]
    public int ActionTypeValue { get; set; }

    [Ignore]
    public ActionType ActionType
    {
        get => (ActionType)ActionTypeValue;
        set => ActionTypeValue = (int)value;
    }

    /// <summary>資源類型（file, command, context 等）</summary>
    [Column("resource_type")]
    [MaxLength(100)]
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>參數 JSON Schema（用於驗證 payload）</summary>
    [Column("param_schema")]
    public string ParamSchema { get; set; } = "{}";

    [Column("risk_level")]
    public int RiskLevelValue { get; set; }

    [Ignore]
    public RiskLevel RiskLevel
    {
        get => (RiskLevel)RiskLevelValue;
        set => RiskLevelValue = (int)value;
    }

    /// <summary>審批政策（auto / require_approval / deny）</summary>
    [Column("approval_policy")]
    [MaxLength(50)]
    public string ApprovalPolicy { get; set; } = "auto";

    /// <summary>TTL 秒數（授予的預設有效期）</summary>
    [Column("ttl_seconds")]
    public int TtlSeconds { get; set; } = 900;

    /// <summary>配額（JSON，例如 {"max_calls": 100, "per_window_seconds": 3600}）</summary>
    [Column("quota")]
    public string Quota { get; set; } = "{}";

    /// <summary>稽核等級（full / summary / none）</summary>
    [Column("audit_level")]
    [MaxLength(20)]
    public string AuditLevel { get; set; } = "full";

    [Column("version")]
    public int Version { get; set; } = 1;
}
