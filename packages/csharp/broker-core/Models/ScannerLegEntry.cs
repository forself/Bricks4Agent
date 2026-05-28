using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// Portfolio Scanner Hybrid - scanner 腿定義(2026-05-27 Phase 1)。
///
/// 解決什麼:核心 6 腿 (`auto_trade_watchlist`) 是固定 (策略, 幣) 配對,適合 t-stat 顯著的長期主力;
/// 但有些策略(harm_prz_scan10_widepz / harm_prz_scan10 / ts_momentum)在多檔幣上都有 edge、
/// 但「在哪一檔開倉」會視當下訊號質量輪動。這張表存「掃描器定義」:給策略 + 候選幣池 + 預算上限、
/// AutoTraderService 每 cycle 在 universe 內挑訊號最強的 N 個開,實際開的位置記在 [[ScannerActiveLegEntry]]。
///
/// 跟核心腿關係:scanner 不會挑核心腿已占用的幣(避讓);核心腿是「契約綁定」、scanner 是「機會主義」。
///
/// 設計來源:[docs/designs/portfolio-scanner-hybrid.md](../../../../docs/designs/portfolio-scanner-hybrid.md)
/// </summary>
[Table("scanner_legs")]
public class ScannerLegEntry
{
    [Key(AutoIncrement = false)]
    [Column("id")]
    [MaxLength(60)]
    public string Id { get; set; } = string.Empty;

    /// <summary>顯示名稱、UI 顯示用、unique。e.g. "widepz_scanner"、"tsmom_scanner"。</summary>
    [Column("name")]
    [Required]
    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    /// <summary>策略名(對齊 strategy registry)。e.g. "harm_prz_scan10_widepz"、"ts_momentum"。</summary>
    [Column("strategy")]
    [Required]
    [MaxLength(60)]
    public string Strategy { get; set; } = string.Empty;

    /// <summary>
    /// 候選幣池 JSON array。e.g. `["OPUSDT","ADAUSDT","INJUSDT","NEARUSDT","LTCUSDT"]`。
    /// AutoTrader sweep 在這 universe 內挑訊號最強的 N 個開。
    /// </summary>
    [Column("universe")]
    [Required]
    public string Universe { get; set; } = "[]";

    /// <summary>本 scanner 總預算(USDT)上限。已開 active legs notional 加總不可超過。</summary>
    [Column("budget_total")]
    public decimal BudgetTotal { get; set; }

    /// <summary>同時可開的最大腿數。e.g. 3 = 最多同時 3 個 active legs。</summary>
    [Column("max_concurrent")]
    public int MaxConcurrent { get; set; } = 3;

    /// <summary>單腿名目上限(USDT)。BudgetTotal / MaxConcurrent 大致對齊、可手動細調。</summary>
    [Column("per_leg_cap")]
    public decimal PerLegCap { get; set; }

    /// <summary>
    /// 交易模式(對齊 [[AutoTradeWatchEntry]].Mode)。
    ///   - "spot"           — IExchangeClient
    ///   - "perp_long_only" — IPerpetualClient,只開多
    ///   - "perp_both"      — IPerpetualClient,雙向
    /// </summary>
    [Column("mode")]
    [Required]
    [MaxLength(20)]
    public string Mode { get; set; } = "spot";

    /// <summary>交易所:"binance"(預設、crypto)/ "bingx" / "alpaca"(美股 paper)。
    /// 2026-05-29 加:scanner 原硬編 binance,加此欄支援多市場(美股 paper-shadow 治理 demo)。</summary>
    [Column("exchange")]
    [MaxLength(20)]
    public string Exchange { get; set; } = "binance";

    /// <summary>K 線時框、e.g. "1d"。harm_prz_scan10* 跨時框驗證只在 12h+1d 有 edge。</summary>
    [Column("interval")]
    [Required]
    [MaxLength(10)]
    public string Interval { get; set; } = "1d";

    /// <summary>Perpetual 模式槓桿倍數。spot 模式忽略。預設 5x、對齊 [[AutoTradeWatchEntry]]。</summary>
    [Column("leverage")]
    public int Leverage { get; set; } = 5;

    /// <summary>
    /// Shadow 模式:評估訊號但只記錄、不下真單。對齊 [[AutoTradeWatchEntry]].Shadow。
    /// 新 scanner 建議先 shadow 4 週、過 pool t-stat 驗證才升 live。
    /// </summary>
    [Column("shadow")]
    public bool Shadow { get; set; } = true;

    /// <summary>啟用旗標。enabled=0 時 AutoTrader sweep 跳過。</summary>
    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>擁有者(對齊 [[AutoTradeWatchEntry]].OwnerPrincipalId)。預設 admin。</summary>
    [Column("owner_principal_id")]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = "prn_dashboard";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
