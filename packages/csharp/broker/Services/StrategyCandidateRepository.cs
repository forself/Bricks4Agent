using System.Collections.Concurrent;

namespace Broker.Services;

/// <summary>
/// 研究候選策略的 in-memory 倉儲（效仿 BacktestHistoryService 的 pattern）。
///
/// 單條血緣 (lineage) = 一條「research run」，內含多個世代 (generation) 的候選者。
/// 每個候選有：parameters、fitness、rationale、parent 編號。
///
/// 不持久化：重啟就清掉——研究是 exploratory，不該污染長期資料。
/// 需要永久紀錄的結果由使用者用既有的 /api/v1/backtest-history/ 保存。
/// </summary>
public class StrategyCandidateRepository
{
    private readonly ConcurrentDictionary<string, ResearchRun> _runs = new();
    private const int MaxRuns = 50;

    public ResearchRun StartRun(string symbol, string family, int targetGenerations)
    {
        var run = new ResearchRun
        {
            RunId = $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..28],
            Symbol = symbol,
            Family = family,
            StartedAt = DateTime.UtcNow,
            TargetGenerations = targetGenerations,
            Status = "running",
        };
        _runs[run.RunId] = run;

        // LRU 清理最舊的 run
        if (_runs.Count > MaxRuns)
        {
            var oldest = _runs.Values.OrderBy(r => r.StartedAt).First();
            _runs.TryRemove(oldest.RunId, out _);
        }
        return run;
    }

    public void AddCandidate(string runId, StrategyCandidate candidate)
    {
        if (!_runs.TryGetValue(runId, out var run)) return;
        candidate.Index = run.Candidates.Count;
        run.Candidates.Add(candidate);
    }

    public void CompleteRun(string runId, string status, string? error = null)
    {
        if (!_runs.TryGetValue(runId, out var run)) return;
        run.Status = status;
        run.CompletedAt = DateTime.UtcNow;
        run.Error = error;
    }

    public ResearchRun? Get(string runId) => _runs.TryGetValue(runId, out var r) ? r : null;

    public IEnumerable<ResearchRun> GetAll()
        => _runs.Values.OrderByDescending(r => r.StartedAt).ToList();
}

// ── DTO ──────────────────────────────────────────────────────────────

public class ResearchRun
{
    public string RunId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Family { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TargetGenerations { get; set; }
    public string Status { get; set; } = "running";  // running | completed | failed
    public string? Error { get; set; }
    public List<StrategyCandidate> Candidates { get; set; } = new();

    public StrategyCandidate? Best =>
        Candidates
            .Where(c => c.BacktestSuccess)
            .OrderByDescending(c => c.OutOfSampleSharpe)
            .FirstOrDefault();
}

public class StrategyCandidate
{
    public int Index { get; set; }          // 在 run 裡的序號，同時是世代編號
    public int? ParentIndex { get; set; }    // 哪個前輩是這個的靈感來源；第 0 個沒有
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 來自 LLM
    public string Family { get; set; } = "";
    public Dictionary<string, int> Parameters { get; set; } = new();
    public string Rationale { get; set; } = "";
    public string LlmModel { get; set; } = "";

    // 來自 Walk-Forward 評估
    public bool BacktestSuccess { get; set; }
    public string? BacktestError { get; set; }

    // 每個 rolling window 的結果（walk-forward 核心輸出）
    public List<WalkForwardWindow> Windows { get; set; } = new();
    public int WindowCount => Windows.Count;

    // 聚合的 In-sample 指標（多個訓練窗口平均）
    public decimal InSampleSharpe { get; set; }
    public decimal InSampleReturnPct { get; set; }
    public decimal InSampleMaxDrawdownPct { get; set; }
    public decimal InSampleWinRate { get; set; }
    public int InSampleTrades { get; set; }

    // 聚合的 Out-of-sample 指標（多個測試窗口平均 → 這是 fitness）
    public decimal OutOfSampleSharpe { get; set; }
    public decimal ReturnPct { get; set; }           // 複利串接 OOS 報酬
    public decimal MaxDrawdownPct { get; set; }      // 所有 window 中最大 OOS MaxDD
    public decimal WinRate { get; set; }
    public int Trades { get; set; }
    public decimal DegradationRatio { get; set; }    // avg OOS / avg IS Sharpe

    // Fitness = OOS Sharpe 是主要目標，其他是輔助資訊
    public decimal Fitness => OutOfSampleSharpe;
}

/// <summary>
/// 單一 walk-forward 窗口的指標。
/// </summary>
public class WalkForwardWindow
{
    public int Index { get; set; }
    public int TrainFrom { get; set; }
    public int TrainTo { get; set; }
    public int TestFrom { get; set; }
    public int TestTo { get; set; }
    public DateTime TestStartDate { get; set; }
    public DateTime TestEndDate { get; set; }

    public decimal InSampleSharpe { get; set; }
    public decimal InSampleReturnPct { get; set; }
    public decimal OutOfSampleSharpe { get; set; }
    public decimal OutOfSampleReturnPct { get; set; }
    public decimal OutOfSampleMaxDrawdownPct { get; set; }
    public int OutOfSampleTrades { get; set; }
}
