using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 一次「掃所有 watched symbols × 多 timeframe × 多策略」的批次執行記錄。
/// 每天 ScheduledBacktestService 醒來跑一輪、產生一個 run、底下接一堆 BacktestResultEntry。
/// </summary>
[Table("backtest_runs")]
public class BacktestRunEntry
{
    [Key(AutoIncrement = false)]
    [Column("run_id")]
    [MaxLength(40)]
    public string RunId { get; set; } = string.Empty;     // run_20260509_030000

    [Column("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [Column("finished_at")]
    public DateTime? FinishedAt { get; set; }

    [Column("run_type")]
    [MaxLength(20)]
    public string RunType { get; set; } = "scheduled";    // scheduled | manual

    [Column("duration_ms")]
    public long DurationMs { get; set; }

    [Column("symbols_count")]
    public int SymbolsCount { get; set; }

    [Column("results_count")]
    public int ResultsCount { get; set; }

    [Column("error_count")]
    public int ErrorCount { get; set; }

    [Column("notes")]
    [MaxLength(500)]
    public string? Notes { get; set; }
}
