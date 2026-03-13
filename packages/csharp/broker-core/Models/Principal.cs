using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 統一主體（人類 / AI / 系統）
/// actor_type 僅紀錄不判權 —— 遵循統一主體原則
/// </summary>
[Table("principals")]
public class Principal
{
    [Key(AutoIncrement = false)]
    [Column("principal_id")]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("actor_type")]
    public int ActorTypeValue { get; set; }

    [Ignore]
    public ActorType ActorType
    {
        get => (ActorType)ActorTypeValue;
        set => ActorTypeValue = (int)value;
    }

    [Column("display_name")]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>主體的 ECDH 公鑰（Base64 DER，用於 E2E 加密）</summary>
    [Column("public_key")]
    public string? PublicKey { get; set; }

    [Column("status")]
    public int StatusValue { get; set; }

    [Ignore]
    public EntityStatus Status
    {
        get => (EntityStatus)StatusValue;
        set => StatusValue = (int)value;
    }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
