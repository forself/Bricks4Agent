using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 申報資金錨持久化（per exchange）+ deposit/withdraw 偵測 cursor。
///
/// BalanceAnchorService 每 N 分鐘 check：
///   live_balance（從 trading.account/perp get_account 拉）
///   - last_seen_balance（上次 cycle 的 balance）
///   - sum(realized_pnl) since last_check_at（trades 表）
///   = unexplained_delta
///
/// |unexplained_delta| > threshold 視為 user 主動劃轉/充值/提領、自動更新 anchor。
/// 不過 threshold 由 service 端 hard-code，這張表只負責記 cursor + anchor 值。
///
/// 為什麼跟 AutoTrader._declaredCapitalByExchange 並存而不替代：
///   - in-memory dict 是 hot path、AutoTrader 每筆 sweep 都讀
///   - DB 只在 BalanceAnchorService cycle / broker 啟動時讀寫
///   - 啟動時 read DB → push 到 in-memory dict、之後 service 用 SetAnchor 同步寫兩邊
/// </summary>
[Table("risk_anchor_state")]
public class RiskAnchorState
{
    [Key(AutoIncrement = false)]
    [Column("exchange")]
    [MaxLength(32)]
    public string Exchange { get; set; } = "";

    /// <summary>當前 anchor 值（給 AutoTrader sizing 用）。</summary>
    [Column("current_anchor")]
    public decimal CurrentAnchor { get; set; }

    /// <summary>上次 cycle 看到的 live balance。</summary>
    [Column("last_seen_balance")]
    public decimal LastSeenBalance { get; set; }

    /// <summary>上次 cycle 的 UTC 時間，用來算 since-last realized_pnl 區間。</summary>
    [Column("last_check_at")]
    public DateTime LastCheckAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次 anchor 變動原因（"deposit" / "withdraw" / "manual" / "init"）。</summary>
    [Column("last_change_reason")]
    [MaxLength(32)]
    public string LastChangeReason { get; set; } = "init";

    /// <summary>最近一次 anchor 變動時間（debug + dashboard 用）。</summary>
    [Column("last_change_at")]
    public DateTime LastChangeAt { get; set; } = DateTime.UtcNow;
}
