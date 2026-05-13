using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// bot-node `claude --print` subprocess 在執行 tool_call 前推送的 LLM reasoning 紀錄。
///
/// 對應 Ch 6.3.5 灰區二（W13）：LLM 在 client 端 (bot-node) 跑、broker 原本看不到 LLM
/// reasoning、僅看到 dispatched tool_call。本表把 reasoning 落 broker 端、補齊 audit trail。
///
/// hybrid 設計：LLM 仍跑在 client side（保留 Max 訂閱成本優勢），只多一次 ~kb 級 POST 把
/// 完整 LLM response（含 reasoning + tool_call JSON）推 broker、broker 端落表 + 可從
/// dashboard / `/api/v1/audit/llm-reasoning?since=...` 反查。
///
/// **僅供 audit、不參與 dispatch 決策**——broker 仍照 capability + ACL + ApprovalGate
/// gate dispatch，這張表是 forensics、不是 control flow。
/// </summary>
[Table("llm_reasoning_audit")]
public class LlmReasoningAuditEntry
{
    [Key]
    [Column("entry_id")]
    public long EntryId { get; set; }

    /// <summary>事件時間（UTC ISO）。</summary>
    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>來源 bot（discord / line / 之後 telegram）。</summary>
    [Column("source")]
    [MaxLength(16)]
    public string Source { get; set; } = "discord";

    /// <summary>觸發 user（平台 user ID，例如 discord user_id）。</summary>
    [Column("user_id")]
    [MaxLength(64)]
    public string UserId { get; set; } = "";

    /// <summary>對話 channel ID（Discord channel / LINE userId 等）。</summary>
    [Column("channel_id")]
    [MaxLength(64)]
    public string ChannelId { get; set; } = "";

    /// <summary>multi-turn 內第幾 turn（0-indexed）。</summary>
    [Column("turn")]
    public int Turn { get; set; }

    /// <summary>LLM 完整 response text（含 reasoning + tool_call JSON 區塊）。</summary>
    [Column("llm_reasoning")]
    public string LlmReasoning { get; set; } = "";

    /// <summary>解析出的 tool capability name（例如 trading.perpetual/place_order）。</summary>
    [Column("tool_name")]
    [MaxLength(128)]
    public string ToolName { get; set; } = "";

    /// <summary>解析出的 tool 參數（JSON）。</summary>
    [Column("tool_args")]
    public string ToolArgs { get; set; } = "{}";

    /// <summary>該 user 對該 tool 是否有 ACL allowance（bot-node 端 isPrivileged check）。</summary>
    [Column("acl_allowed")]
    public bool AclAllowed { get; set; }

    /// <summary>tool dispatch 之後的結果摘要（事後補寫、initial insert 為空）。</summary>
    [Column("dispatch_result")]
    [MaxLength(32)]
    public string DispatchResult { get; set; } = "pending";
}
