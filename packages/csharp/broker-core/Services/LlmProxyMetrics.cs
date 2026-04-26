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

    /// <summary>
    /// 把 ring buffer 內的呼叫按時間 bucket 分組，給儀表板畫趨勢圖。
    /// 預設 10 分鐘一格，向前回溯 N 格（N×10 分鐘 = 視窗長度）。
    /// 即使該格沒任何呼叫也會回一筆 0 計數，方便前端畫連續長條。
    /// </summary>
    public List<LlmTrendBucket> Trend(int bucketMinutes = 10, int bucketCount = 24)
    {
        bucketMinutes = Math.Clamp(bucketMinutes, 1, 60);
        bucketCount   = Math.Clamp(bucketCount, 1, 144);  // max 24h with 10m bucket

        var bucketSpan = TimeSpan.FromMinutes(bucketMinutes);
        var nowUtc     = DateTime.UtcNow;

        // 把 now 對齊到 bucket 邊界（向下）
        var nowTicks = nowUtc.Ticks;
        var spanTicks = bucketSpan.Ticks;
        var alignedNow = new DateTime((nowTicks / spanTicks) * spanTicks, DateTimeKind.Utc);

        var buckets = new List<LlmTrendBucket>(bucketCount);
        var bucketStarts = new DateTime[bucketCount];
        for (int i = 0; i < bucketCount; i++)
        {
            // i=0 是最舊、i=bucketCount-1 是最新（含現在）
            bucketStarts[i] = alignedNow - TimeSpan.FromMinutes((bucketCount - 1 - i) * bucketMinutes);
            buckets.Add(new LlmTrendBucket
            {
                BucketStart = bucketStarts[i],
                BucketMinutes = bucketMinutes,
            });
        }

        var oldestVisible = bucketStarts[0];
        var newestVisible = bucketStarts[^1] + bucketSpan;

        List<LlmCallRecord> snapshot;
        lock (_lock) { snapshot = _recent.ToList(); }

        foreach (var r in snapshot)
        {
            if (r.Ts < oldestVisible || r.Ts >= newestVisible) continue;
            int idx = (int)((r.Ts - oldestVisible).Ticks / spanTicks);
            if (idx < 0 || idx >= bucketCount) continue;
            var b = buckets[idx];
            b.TotalCalls++;
            if (r.Success) b.SuccessCalls++;
            else b.FailureCalls++;
            b.LatencySumMs += r.LatencyMs;
            b.EvalTokens   += r.EvalTokens;
        }
        foreach (var b in buckets)
            b.AvgLatencyMs = b.TotalCalls > 0 ? b.LatencySumMs / b.TotalCalls : 0;

        return buckets;
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

public class LlmTrendBucket
{
    public DateTime BucketStart   { get; set; }
    public int      BucketMinutes { get; set; }
    public int      TotalCalls    { get; set; }
    public int      SuccessCalls  { get; set; }
    public int      FailureCalls  { get; set; }
    public long     LatencySumMs  { get; set; }
    public long     AvgLatencyMs  { get; set; }
    public long     EvalTokens    { get; set; }
}
