using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 平台整體健康分數的歷史 snapshot。
///
/// 由 HealthScoreSnapshotService 每 5 min 寫一筆。
/// 給 dashboard 顯示「過去 N 小時的健康趨勢」+ 報告附錄做時序圖用。
///
/// 沒做 per-worker 細節 snapshot（避免暴漲 row 數）：當下細節由 /health/score
/// 即時計算、歷史只記 overall + 三類計數。需要回看「某 worker 在 X 時間是 critical」
/// 可以用 audit_events 中該時段的 DISPATCH_FAILED 事件反推。
/// </summary>
[Table("health_score_snapshots")]
public class HealthScoreSnapshot
{
    [Key, MaxLength(64)]
    [Column("snapshot_id")]
    public string SnapshotId { get; set; } = string.Empty;

    [Column("captured_at")]
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    [Column("overall_score")]
    public int OverallScore { get; set; }

    [Column("overall_status")]
    [MaxLength(20)]
    public string OverallStatus { get; set; } = "";

    [Column("worker_count")]
    public int WorkerCount { get; set; }

    [Column("healthy_count")]
    public int HealthyCount { get; set; }

    [Column("degraded_count")]
    public int DegradedCount { get; set; }

    [Column("critical_count")]
    public int CriticalCount { get; set; }
}
