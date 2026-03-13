using BaseOrm;

namespace BrokerCore.Models;

/// <summary>角色定義</summary>
[Table("roles")]
public class Role
{
    [Key(AutoIncrement = false)]
    [Column("role_id")]
    public string RoleId { get; set; } = string.Empty;

    [Column("display_name")]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>允許的任務類型（JSON 陣列，例如 ["query","analysis"]）</summary>
    [Column("allowed_task_types")]
    public string AllowedTaskTypes { get; set; } = "[]";

    /// <summary>預設能力 ID 清單（JSON 陣列）</summary>
    [Column("default_capability_ids")]
    public string DefaultCapabilityIds { get; set; } = "[]";

    [Column("version")]
    public int Version { get; set; } = 1;

    [Column("status")]
    public int StatusValue { get; set; }

    [Ignore]
    public EntityStatus Status
    {
        get => (EntityStatus)StatusValue;
        set => StatusValue = (int)value;
    }
}
