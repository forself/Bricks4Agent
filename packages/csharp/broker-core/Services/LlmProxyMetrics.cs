using System.Collections.Concurrent;

namespace BrokerCore.Services;

/// <summary>
/// LLM 代理觀測層：記錄每一次 ChatAsync 呼叫的 metadata，給儀表板顯示。
///
/// 設計重點：
///   - 純記憶體 ring buffer + thread-safe counters（不寫 SQLite，broker 重啟歸零）
///   - decorator 模式：MeteredLlmProxyService 包住 LlmProxyService，這個類別只是 holder
///   - 不存 prompt / response 內容，只存 metadata（model / task_id / latency / token / error）
///     原因：避免敏感資訊外洩 + 大量 prompt 文字會吃光記憶體
///   - 容量上限 200 筆（最近 200 次呼叫滾動覆蓋）
/// </summary>
public class LlmProxyMetrics
{
    public const int MaxRecentEntries = 200;

    private readonly object _lock = new();
    private readonly Queue<LlmCallRecord> _recent = new();

    // 全期間累計（broker 啟動以來）
    private long _totalCalls;
    private long _successCalls;
    private long _failureCalls;
    private long _totalEvalTokens;
    private long _totalLatencyMs;

    // per-model 計數（用 ConcurrentDictionary 不需另外 lock）
    private readonly ConcurrentDictionary<string, ModelStat> _perModel = new();

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public void Record(LlmCallRecord r)
    {
        lock (_lock)
        {
            _recent.Enqueue(r);
            while (_recent.Count > MaxRecentEntries)
                _recent.Dequeue();
        }

        Interlocked.Increment(ref _totalCalls);
        if (r.Success) Interlocked.Increment(ref _successCalls);
        else Interlocked.Increment(ref _failureCalls);
        Interlocked.Add(ref _totalEvalTokens, r.EvalTokens);
        Interlocked.Add(ref _totalLatencyMs, r.LatencyMs);

        var modelKey = string.IsNullOrEmpty(r.Model) ? "(unknown)" : r.Model;
        _perModel.AddOrUpdate(modelKey,
            _ => new ModelStat { Calls = 1, SuccessCalls = r.Success ? 1 : 0, EvalTokens = r.EvalTokens, LatencyMs = r.LatencyMs, LastTs = r.Ts },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Calls++;
                    if (r.Success) existing.SuccessCalls++;
                    existing.EvalTokens += r.EvalTokens;
                    existing.LatencyMs += r.LatencyMs;
                    existing.LastTs = r.Ts;
                }
                return existing;
            });
    }

    public LlmMetricsSnapshot Snapshot()
    {
        long calls = Interlocked.Read(ref _totalCalls);
        long success = Interlocked.Read(ref _successCalls);
        long failures = Interlocked.Read(ref _failureCalls);
        long tokens = Interlocked.Read(ref _totalEvalTokens);
        long latency = Interlocked.Read(ref _totalLatencyMs);

        var perModel = _perModel
            .Select(kv => new ModelSummary
            {
                Model = kv.Key,
                Calls = kv.Value.Calls,
                SuccessCalls = kv.Value.SuccessCalls,
                EvalTokens = kv.Value.EvalTokens,
                AvgLatencyMs = kv.Value.Calls > 0 ? kv.Value.LatencyMs / kv.Value.Calls : 0,
                LastTs = kv.Value.LastTs,
            })
            .OrderByDescending(m => m.Calls)
            .ToList();

        return new LlmMetricsSnapshot
        {
            StartedAt = StartedAt,
            TotalCalls = calls,
            SuccessCalls = success,
            FailureCalls = failures,
            TotalEvalTokens = tokens,
            AvgLatencyMs = calls > 0 ? latency / calls : 0,
            SuccessRatePct = calls > 0 ? (success * 100.0) / calls : 0,
            PerModel = perModel,
        };
    }

    public IReadOnlyList<LlmCallRecord> Recent(int limit = 50)
    {
        lock (_lock)
        {
            // 最新在前
            return _recent.Reverse().Take(Math.Clamp(limit, 1, MaxRecentEntries)).ToList();
        }
    }

    private class ModelStat
    {
        public long Calls;
        public long SuccessCalls;
        public long EvalTokens;
        public long LatencyMs;
        public DateTime LastTs;
    }
}

public class LlmCallRecord
{
    public DateTime Ts          { get; set; }
    public string   Model       { get; set; } = "";
    public string   TaskId      { get; set; } = "";
    public string   TaskType    { get; set; } = "";
    public bool     Success     { get; set; }
    public long     LatencyMs   { get; set; }
    public int      EvalTokens  { get; set; }
    public string?  ErrorBrief  { get; set; }   // 截短的錯誤訊息，最多 240 字
}

public class LlmMetricsSnapshot
{
    public DateTime StartedAt        { get; set; }
    public long     TotalCalls       { get; set; }
    public long     SuccessCalls     { get; set; }
    public long     FailureCalls     { get; set; }
    public long     TotalEvalTokens  { get; set; }
    public long     AvgLatencyMs     { get; set; }
    public double   SuccessRatePct   { get; set; }
    public List<ModelSummary> PerModel { get; set; } = new();
}

public class ModelSummary
{
    public string   Model        { get; set; } = "";
    public long     Calls        { get; set; }
    public long     SuccessCalls { get; set; }
    public long     EvalTokens   { get; set; }
    public long     AvgLatencyMs { get; set; }
    public DateTime LastTs       { get; set; }
}
