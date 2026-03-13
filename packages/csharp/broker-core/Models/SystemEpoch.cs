using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 全域紀元 —— Kill Switch 核心
/// 只有一行資料。epoch 遞增 → 所有舊 token 即時失效（O(1) 檢查）
/// 叢集化：DB 為權威來源，各 instance 短快取(5s TTL)
/// </summary>
[Table("system_epoch")]
public class SystemEpoch
{
    [Key(AutoIncrement = false)]
    [Column("epoch_id")]
    public int EpochId { get; set; } = 1;

    /// <summary>當前紀元（kill switch 遞增此值）</summary>
    [Column("current_epoch")]
    public int CurrentEpoch { get; set; } = 1;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_by")]
    public string UpdatedBy { get; set; } = "system";
}
