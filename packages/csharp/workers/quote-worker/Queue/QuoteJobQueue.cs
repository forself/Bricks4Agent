using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuoteWorker.Fetcher;
using QuoteWorker.Models;

namespace QuoteWorker.Queue;

/// <summary>
/// 金融報價 Job Queue。
///
/// 職責：
/// 1. PeriodicTimer 定時將 fetch job 加入佇列
/// 2. 背景 worker 依序執行佇列中的 job
/// 3. 維護最近 50 筆 job 歷程
/// 4. 維護各 symbol 的最新報價 dict
/// </summary>
public class QuoteJobQueue
{
    private readonly QuoteFetcher _fetcher;
    private readonly ILogger<QuoteJobQueue> _logger;
    private readonly int _fetchIntervalMinutes;

    private readonly ConcurrentQueue<QuoteFetchJob> _pending = new();
    private readonly object _lock = new();
    private readonly List<QuoteFetchJob>         _history      = new(); // newest first
    private readonly Dictionary<string, QuoteResult> _latest  = new();

    private const int MaxHistory   = 50;
    private const int MaxQueueSize = 5;

    public QuoteJobQueue(
        QuoteFetcher fetcher,
        ILogger<QuoteJobQueue> logger,
        int fetchIntervalMinutes)
    {
        _fetcher              = fetcher;
        _logger               = logger;
        _fetchIntervalMinutes = fetchIntervalMinutes;
    }

    // ── 外部觸發（立即抓取）────────────────────────────────────────
    public bool EnqueueNow()
    {
        if (_pending.Count >= MaxQueueSize)
        {
            _logger.LogWarning("Queue full ({Max}), skipping enqueue", MaxQueueSize);
            return false;
        }
        _pending.Enqueue(new QuoteFetchJob());
        _logger.LogInformation("Fetch job enqueued (queue size: {Size})", _pending.Count);
        return true;
    }

    // ── 背景主迴圈 ─────────────────────────────────────────────────
    public async Task RunAsync(CancellationToken ct)
    {
        // 啟動後 5 秒先抓一次
        await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { });
        EnqueueNow();

        using var timer   = new PeriodicTimer(TimeSpan.FromMinutes(_fetchIntervalMinutes));
        var timerTask     = RunTimerAsync(timer, ct);
        var workerTask    = ProcessQueueAsync(ct);

        await Task.WhenAll(timerTask, workerTask);
    }

    private async Task RunTimerAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                EnqueueNow();
        }
        catch (OperationCanceledException) { }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_pending.TryDequeue(out var job))
                await ExecuteJobAsync(job, ct);
            else
                await Task.Delay(1000, ct).ContinueWith(_ => { });
        }
    }

    private async Task ExecuteJobAsync(QuoteFetchJob job, CancellationToken ct)
    {
        job.Status    = "running";
        job.StartedAt = DateTime.UtcNow;
        _logger.LogInformation("[Job {Id}] started", job.JobId);

        try
        {
            var (results, errors) = await _fetcher.FetchAllAsync(ct);

            job.Results      = results;
            job.Errors       = errors;
            job.FetchedCount = results.Count;
            job.ErrorCount   = errors.Count;
            job.TotalSymbols = results.Count + errors.Count;
            job.Status       = errors.Count == 0 ? "success" : (results.Count == 0 ? "failed" : "partial");

            lock (_lock)
            {
                foreach (var r in results)
                    _latest[r.Symbol] = r;
            }

            _logger.LogInformation("[Job {Id}] done: {Fetched} ok, {Err} errors, {Dur:F1}s",
                job.JobId, results.Count, errors.Count,
                (DateTime.UtcNow - job.StartedAt.Value).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            job.Status     = "failed";
            job.FatalError = "Cancelled";
        }
        catch (Exception ex)
        {
            job.Status     = "failed";
            job.FatalError = ex.Message;
            job.ErrorCount++;
            _logger.LogError(ex, "[Job {Id}] fatal error", job.JobId);
        }
        finally
        {
            job.CompletedAt = DateTime.UtcNow;
            AddHistory(job);
        }
    }

    private void AddHistory(QuoteFetchJob job)
    {
        lock (_lock)
        {
            _history.Insert(0, job);
            if (_history.Count > MaxHistory)
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
        }
    }

    // ── 查詢 API ───────────────────────────────────────────────────
    public List<QuoteFetchJob> GetHistory(int take = 20)
    {
        lock (_lock)
            return _history.Take(take).ToList();
    }

    public List<QuoteResult> GetLatestPrices()
    {
        lock (_lock)
            return _latest.Values
                .OrderBy(r => r.Type)
                .ThenBy(r => r.Symbol)
                .ToList();
    }

    public int PendingCount => _pending.Count;
}
