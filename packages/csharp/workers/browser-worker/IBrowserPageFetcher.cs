namespace BrowserWorker;

/// <summary>
/// 瀏覽器唯讀頁面操作抽象。讓 governed handler 的分級閘控與路由邏輯
/// 可在不啟動真實 Playwright/Chromium 的情況下單元測試。
/// </summary>
public interface IBrowserPageFetcher
{
    Task<BrowserPageResult> FetchPageAsync(
        string url,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    Task<BrowserNavigationResult> NavigateAsync(
        string startUrl,
        int maxSteps,
        IReadOnlyCollection<string>? allowedHostSuffixes = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);
}

/// <summary>navigate level 的多步導覽結果。</summary>
public sealed class BrowserNavigationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<BrowserPageResult> Steps { get; set; } = new();

    public static BrowserNavigationResult Ok(List<BrowserPageResult> steps)
        => new() { Success = true, Steps = steps };

    public static BrowserNavigationResult Fail(string error)
        => new() { Success = false, Error = error };
}
