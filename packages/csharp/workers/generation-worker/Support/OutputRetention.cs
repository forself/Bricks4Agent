using Microsoft.Extensions.Logging;

namespace GenerationWorker.Support;

/// <summary>
/// 產物的保留期限清理（設定 <c>Generation:RetentionHours</c>）。
///
/// generate 成功後 <c>{OutputRoot}/{output_slot}/{requestId}/</c> 留著 zip 與 result.json：broker 會把 zip 複製到使用者的文件區，
/// 但被拒收、逾時後才產出或代理重試留下的套件不會被取走。zip 內含使用者的需求內容，不能無限期留在使用者工作區之外，
/// 所以由唯一能寫入 OutputRoot 的 worker 在啟動時、每次 generate 之前，以及執行期間定期（<see cref="RunPeriodicAsync"/>）
/// 刪除超過期限的請求目錄。worker 沒有執行時不會清理。
///
/// 只處理本 worker 建立的結構：名稱符合 <see cref="GenerationRequest.IsSafeName"/> 的 slot 目錄與其下的請求目錄。
/// 符號連結與 junction 一律略過（不跟隨、不刪除），其他名稱的項目也不動。OutputRoot 本身是連結時無法清理：
/// worker 啟動時的設定檢查拒絕這種根目錄，清理時遇到也記錄警告。
/// </summary>
public static class OutputRetention
{
    /// <summary>
    /// 每隔 <paramref name="interval"/> 執行一次 <paramref name="sweep"/>，直到 <paramref name="cancellationToken"/> 取消。
    /// 讓閒置的 worker（沒有新的 generate、也沒有重啟）仍會刪除過期的產物。單次清理丟出的例外只記錄，不中斷之後的清理。
    /// </summary>
    public static async Task RunPeriodicAsync(
        Func<CancellationToken, Task<int>> sweep,
        TimeSpan interval,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "The sweep interval must be positive.");

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await sweep(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "The periodic generation output sweep failed; it runs again at the next interval.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // worker 停止
        }
    }

    /// <summary>
    /// 刪除最後修改時間早於 <paramref name="now"/> 減 <paramref name="retention"/> 的請求目錄，以及因此變空且同樣過期的 slot 目錄。
    /// 回傳刪除的請求目錄數。單一目錄刪不掉時記錄警告後繼續。
    /// </summary>
    public static int Sweep(string outputRoot, TimeSpan retention, DateTimeOffset now, ILogger logger)
    {
        if (retention <= TimeSpan.Zero || string.IsNullOrWhiteSpace(outputRoot))
            return 0;

        var root = new DirectoryInfo(Path.GetFullPath(outputRoot));
        if (!root.Exists)
            return 0;
        if (IsLink(root))
        {
            // 不跟隨連結，所以根目錄是連結時無法清理。啟動時的設定檢查會拒絕這種根目錄；這裡處理啟動後才被換成連結的情況。
            logger.LogWarning(
                "Generation:OutputRoot is a symbolic link or junction, so expired generation outputs are not removed. " +
                "Set Generation:OutputRoot to the real directory.");
            return 0;
        }

        var cutoff = now.UtcDateTime - retention;
        var removed = 0;
        foreach (var slot in SafeDirectories(root))
        {
            // 刪除請求目錄會更新 slot 目錄的修改時間，所以先記下刪除前的時間：最後一個請求也已過期時，空的 slot 一併移除。
            var slotExpired = slot.LastWriteTimeUtc <= cutoff;
            foreach (var request in SafeDirectories(slot))
            {
                if (request.LastWriteTimeUtc > cutoff)
                    continue;

                if (TryDelete(request, logger))
                    removed++;
            }

            try
            {
                slot.Refresh();
                if (slotExpired && slot.Exists && !slot.EnumerateFileSystemInfos().Any())
                    slot.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "An expired generation output slot could not be removed.");
            }
        }

        if (removed > 0)
            logger.LogInformation("Removed {Count} generation output directories older than {Hours} hours.", removed, retention.TotalHours);
        return removed;
    }

    /// <summary>名稱是安全名稱、而且不是連結的子目錄（其他項目不列出）。</summary>
    private static IEnumerable<DirectoryInfo> SafeDirectories(DirectoryInfo parent)
    {
        List<DirectoryInfo> children;
        try
        {
            children = parent.EnumerateDirectories().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<DirectoryInfo>();
        }

        return children.Where(child => GenerationRequest.IsSafeName(child.Name) && !IsLink(child));
    }

    /// <summary>
    /// 刪除一個請求目錄。目錄內有連結時整個略過：本 worker 不會建立連結，出現了就不是它留下的內容。
    /// </summary>
    private static bool TryDelete(DirectoryInfo directory, ILogger logger)
    {
        try
        {
            if (ContainsLink(directory))
            {
                logger.LogWarning("A generation output directory contains a link and was left in place.");
                return false;
            }

            directory.Delete(recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "An expired generation output directory could not be removed.");
            return false;
        }
    }

    private static bool ContainsLink(DirectoryInfo directory)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
            {
                if (IsLink(entry))
                    return true;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="outputRoot"/> 本身是否為符號連結或 junction（不存在時為 false）。保留期限清理不跟隨連結，
    /// 所以這種根目錄下的產物永遠不會被刪除；worker 的設定檢查以此拒絕啟動。
    /// </summary>
    public static bool IsLinkedRoot(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            return false;

        var root = new DirectoryInfo(Path.GetFullPath(outputRoot));
        return root.Exists && IsLink(root);
    }

    private static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
