using Microsoft.Extensions.Logging;
using QuoteWorker.Queue;

namespace QuoteWorker.Storage;

/// <summary>
/// 背景迴圈：定期從 QuoteJobQueue 讀取最新報價並寫入 SQLite。
/// 避免修改 QuoteJobQueue 本身。
/// </summary>
public static class SnapshotPersistenceLoop
{
    public static async Task RunAsync(
        QuoteJobQueue queue,
        QuoteDbStorage db,
        ILogger logger,
        int intervalSeconds = 60,
        CancellationToken ct = default)
    {
        // 等 queue 先跑一輪
        await Task.Delay(TimeSpan.FromSeconds(15), ct).ContinueWith(_ => { });

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        try
        {
            // 先存一次
            SaveLatest(queue, db, logger);

            while (await timer.WaitForNextTickAsync(ct))
                SaveLatest(queue, db, logger);
        }
        catch (OperationCanceledException) { }
    }

    private static void SaveLatest(QuoteJobQueue queue, QuoteDbStorage db, ILogger logger)
    {
        var prices = queue.GetLatestPrices();
        if (prices.Count == 0) return;

        try
        {
            db.SaveSnapshots(prices);
            logger.LogDebug("Persisted {Count} quote snapshots to DB", prices.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist quote snapshots");
        }
    }
}
