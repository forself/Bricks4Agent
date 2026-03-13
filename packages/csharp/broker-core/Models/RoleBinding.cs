using BaseOrm;

namespace BrokerCore.Models;

/// <summary>主體—角色綁定（時效性）</summary>
[Table("role_bindings")]
public class RoleBinding
{
    [Key(AutoIncrement = false)]
    [Column("binding_id")]
    public string BindingId { get; set; } = string.Empty;

    [Column("principal_id")]
    [Required]
    public string PrincipalId { get; set; } = string.Empty;

    [Column("role_id")]
    [Required]
    public string RoleId { get; set; } = string.Empty;

    [Column("granted_by")]
    public string GrantedBy { get; set; } = string.Empty;

    [Column("granted_at")]
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("status")]
    public int StatusValue { get; set; }

    [Ignore]
    public EntityStatus Status
    {
        get => (EntityStatus)StatusValue;
        set => StatusValue = (int)value;
    }
}
