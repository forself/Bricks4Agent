using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Auto-trader 監控清單持久化（2026-05-02 補完）。
///
/// 解決什麼：原本 AutoTraderService._watchList 是 ConcurrentDictionary 純記憶體，
/// broker 一重啟所有監控的 symbol、策略、quantity 全消失，使用者得手動重設。
/// 現在每筆 watch 進這張表，broker 啟動時 reload 重建記憶體 dict。
///
/// EntryKey 是 "{exchange}:{symbol}" — 跟記憶體 dict 的 key 同格式，遷移無痛。
/// 同 (exchange, symbol) 同時間只能有一筆 watch（同 PK 覆寫）。
///
/// LastSignal / LastConfidence / LastCheck 也存——重啟後 dashboard 還能看到「上次評估
/// 結果」直到下一輪 poll 覆蓋；不存的話 UI 會閃一下 N/A，體驗差。
/// </summary>
[Table("auto_trade_watchlist")]
public class AutoTradeWatchEntry
{
    [Key(AutoIncrement = false)]
    [Column("entry_key")]
    [MaxLength(120)]
    public string EntryKey { get; set; } = string.Empty;

    [Column("symbol")]
    [Required]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;

    [Column("exchange")]
    [Required]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;

    [Column("strategy")]
    [MaxLength(60)]
    public string Strategy { get; set; } = "composite";

    [Column("quantity")]
    public decimal Quantity { get; set; } = 1;

    [Column("active")]
    public bool Active { get; set; } = true;

    /// <summary>
    /// 交易模式（Phase 3：BingX perpetual）。預設 "spot" 保持既有行為。
    ///   - "spot"           — 走 IExchangeClient（Alpaca / Binance spot），buy/sell 直接打交易所
    ///   - "perp_long_only" — 走 IPerpetualClient，只開多：buy=open_long（如果無倉）/sell=close_long
    ///   - "perp_both"      — 走 IPerpetualClient，雙向：buy=open_long 或 close_short / sell=open_short 或 close_long
    /// </summary>
    [Column("mode")]
    [MaxLength(20)]
    public string Mode { get; set; } = "spot";

    /// <summary>perpetual 模式才用、開倉時的槓桿倍數。spot 模式忽略此欄位。預設 5x。</summary>
    [Column("leverage")]
    public int Leverage { get; set; } = 5;

    /// <summary>
    /// HTF（higher timeframe）大週期確認週期文字、例如 "4h"、"1d"、"1w"。
    /// 設定後：AutoTrader sweep 會額外 fetch 這個級別的 K 線、傳給 strategy.signal 做大週期方向確認。
    /// 預設 null = 不做 HTF 確認、保持單一週期判斷的既有行為。
    /// 目前只有 HarmonicStrategy 用、其他策略忽略。
    /// </summary>
    [Column("htf_interval")]
    [MaxLength(10)]
    public string? HtfInterval { get; set; }

    /// <summary>
    /// Shadow（影子）模式:AutoTrader 照常評估訊號、跑風控、記 log,但「絕不下真單」。
    /// 用途:新策略(如 SMC 日線)上線前先 shadow 跑幾週,對帳「實盤訊號 vs 回測」再決定真錢。
    /// 預設 false = 真交易(既有 watch 全部不變)。安全關鍵:此欄位必須持久化,否則重啟會變回真交易。
    /// </summary>
    [Column("shadow")]
    public bool Shadow { get; set; } = false;

    [Column("last_signal")]
    [MaxLength(20)]
    public string? LastSignal { get; set; }

    [Column("last_confidence")]
    public decimal LastConfidence { get; set; }

    [Column("last_check")]
    public DateTime? LastCheck { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 擁有者 principal_id（Phase A2 新增）。資料隔離：admin 看全部、user 看自己這個欄位 == 自己 principal 的列。
    /// 既有資料 migration 時都標 'prn_dashboard'（admin user）。
    /// </summary>
    [Column("owner_principal_id")]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = "prn_dashboard";
}
