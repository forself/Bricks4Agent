using BaseOrm;

namespace Broker.Models;

/// <summary>
/// H3 — 預先批准規則：命中規則的 approval request 直接自動 approve、不入 pending 清單。
///
/// 為什麼：W14 P3 把 risk hint 加到 approval 通知、admin 看金額再決定按不按。
/// 但同類重複請求（每天 spot BTC ≤ 0.01）還是要按 N 次、會「approval 疲勞」、
/// 然後變成全按 approve = approval gate 失效。
///
/// 解法：admin 預先批一條「BTC spot 0.01 以下自動放行」的 template、
/// broker 看到符合條件的 request 直接 approve+dispatch、不彈窗。
///
/// 風險控制：
/// - max_uses_per_day > 0 限當日命中次數（超過後該 template 自動失效到隔天 UTC）
/// - 每次命中寫 audit event `AUTO_APPROVED_BY_TEMPLATE`、可追溯
/// - 任一條 payload_match 沒過 → 不自動 approve、回 pending 流程（保留人工覆核）
/// - template enable=false 立刻停用
///
/// payload_match 格式（JSON object）：
///   { "args.symbol": "BTC-USDT", "args.quantity": {"$lte": 0.01}, "args.side": {"$in":["buy","BUY"]} }
/// 支援 operator：直接字串 = equality / $lte / $gte / $lt / $gt / $in / $eq
/// </summary>
[Table("approval_templates")]
public class ApprovalTemplate
{
    [Key(AutoIncrement = false), MaxLength(64)]
    [Column("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [Column("capability_id"), MaxLength(80), Required]
    public string CapabilityId { get; set; } = string.Empty;

    /// <summary>route 精確匹配；空字串 = 任何 route 都套用</summary>
    [Column("route"), MaxLength(80)]
    public string Route { get; set; } = string.Empty;

    /// <summary>payload 條件 JSON（見類別說明）</summary>
    [Column("payload_match")]
    public string PayloadMatch { get; set; } = "{}";

    /// <summary>0 = 不限；&gt;0 = 當日最多命中次數（UTC 重置）</summary>
    [Column("max_uses_per_day")]
    public int MaxUsesPerDay { get; set; } = 0;

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("description"), MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Column("created_by"), MaxLength(64)]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
