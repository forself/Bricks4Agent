using BaseOrm;

namespace BrokerCore.Models;

/// <summary>撤權紀錄</summary>
[Table("revocations")]
public class Revocation
{
    [Key(AutoIncrement = false)]
    [Column("revocation_id")]
    public string RevocationId { get; set; } = string.Empty;

    [Column("target_type")]
    public int TargetTypeValue { get; set; }

    [Ignore]
    public RevocationTargetType TargetType
    {
        get => (RevocationTargetType)TargetTypeValue;
        set => TargetTypeValue = (int)value;
    }

    [Column("target_id")]
    [Required]
    public string TargetId { get; set; } = string.Empty;

    [Column("reason")]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Column("revoked_by")]
    [Required]
    public string RevokedBy { get; set; } = string.Empty;

    [Column("revoked_at")]
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;
}
