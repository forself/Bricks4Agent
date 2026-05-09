using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 單一 (run × symbol × timeframe × strategy) 組合的回測結果。
/// 大量產生（4 symbols × 3 timeframes × 8 strategies = 96 rows / day），
/// 用 entry_id auto-increment 簡單存。recommended 旗標讓 query 直接撈 top picks。
/// </summary>
[Table("backtest_results")]
public class BacktestResultEntry
{
    [Key(AutoIncrement = true)]
    [Column("entry_id")]
    public long EntryId { get; set; }

    [Column("run_id")]
    [Required]
    [MaxLength(40)]
    public string RunId { get; set; } = string.Empty;

    [Column("symbol")]
    [Required]
    [MaxLength(40)]
    public string Symbol { get; set; } = string.Empty;

    [Column("exchange")]
    [MaxLength(40)]
    public string Exchange { get; set; } = string.Empty;

    [Column("timeframe")]
    [MaxLength(8)]
    public string Timeframe { get; set; } = "1d";          // 1h / 4h / 1d

    [Column("strategy")]
    [Required]
    [MaxLength(60)]
    public string Strategy { get; set; } = string.Empty;

    [Column("bars_count")]
    public int BarsCount { get; set; }

    /// <summary>用了哪組參數（JSON）。null = 用了策略 default。</summary>
    [Column("params_json")]
    public string? ParamsJson { get; set; }

    /// <summary>regime 標籤（trending / ranging / volatile / squeeze、空 = 沒分類）</summary>
    [Column("regime")]
    [MaxLength(20)]
    public string? Regime { get; set; }

    [Column("total_return_pct")]
    public decimal TotalReturnPct { get; set; }

    [Column("sharpe")]
    public decimal Sharpe { get; set; }

    [Column("win_rate")]
    public decimal WinRate { get; set; }

    [Column("max_dd_pct")]
    public decimal MaxDdPct { get; set; }

    [Column("trades")]
    public int Trades { get; set; }

    [Column("score")]
    public decimal Score { get; set; }                     // composite ranking score

    /// <summary>排名後標記為當前 (symbol, timeframe) 的最佳策略。lab/recommendations 直接 filter 這個。</summary>
    [Column("recommended")]
    public bool Recommended { get; set; }

    [Column("error")]
    [MaxLength(500)]
    public string? Error { get; set; }

    [Column("finished_at")]
    public DateTime FinishedAt { get; set; } = DateTime.UtcNow;

    // ── B3 walk-forward 補資料：跑完普通 backtest 後再切 train/test rolling 視窗、量過擬合 ──
    /// <summary>過去 walk-forward windows OOS 平均 return%（NaN/0 = 沒跑成功）。</summary>
    [Column("oos_return_pct")]
    public decimal OosReturnPct { get; set; }

    /// <summary>OOS 平均 Sharpe</summary>
    [Column("oos_sharpe")]
    public decimal OosSharpe { get; set; }

    /// <summary>OOS 平均勝率</summary>
    [Column("oos_win_rate")]
    public decimal OosWinRate { get; set; }

    /// <summary>
    /// IS-OOS gap：(is_return - oos_return) / |is_return|。
    /// 越接近 0 越穩、≥0.5 表示 OOS 嚴重縮水（過擬合 red flag）。
    /// score 算分時這條會給扣分；&gt; 0.7 直接排除 recommended。
    /// </summary>
    [Column("is_oos_gap")]
    public decimal IsOosGap { get; set; }

    /// <summary>walk-forward 切了幾個 fold（&gt;0 表示跑成功、=0 表示沒跑或 bars 不夠）。</summary>
    [Column("wf_folds")]
    public int WfFolds { get; set; }

    /// <summary>
    /// 這條結果歸屬於哪個 user 的 watch。Phase A2：lab/recommendations 按這個過濾。
    /// 跟 BacktestRunEntry.RunId 加總可以還原「這次 run 為誰跑了哪些 symbol」。
    /// </summary>
    [Column("owner_principal_id")]
    [MaxLength(80)]
    public string OwnerPrincipalId { get; set; } = "prn_dashboard";
}
