using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace BrowserWorker;

/// <summary>
/// Manages Playwright browser lifecycle and provides page operations.
/// One browser instance shared across requests; pages are created per-request.
/// </summary>
public sealed class PlaywrightBrowserService : IAsyncDisposable
{
    private readonly BrowserWorkerOptions _options;
    private readonly ILogger<PlaywrightBrowserService> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _pageSemaphore;

    public PlaywrightBrowserService(BrowserWorkerOptions options, ILogger<PlaywrightBrowserService> logger)
    {
        _options = options;
        _logger = logger;
        _pageSemaphore = new SemaphoreSlim(options.MaxConcurrentPages, options.MaxConcurrentPages);
    }

    public async Task EnsureInitializedAsync()
    {
        if (_browser is { IsConnected: true })
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
                return;

            _logger.LogInformation("Initializing Playwright browser (headless={Headless})", _options.Headless);

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _options.Headless
            });

            _logger.LogInformation("Playwright browser initialized: {Version}", _browser.Version);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Navigate to a URL and extract page content.
    /// Returns title, text content, final URL, and optional screenshot.
    /// </summary>
    public async Task<BrowserPageResult> FetchPageAsync(
        string url,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        if (_browser == null)
            return BrowserPageResult.Fail("browser_not_initialized");

        await _pageSemaphore.WaitAsync(cancellationToken);
        try
        {
            var contextOptions = new BrowserNewContextOptions
            {
                UserAgent = userAgent ?? _options.UserAgent
            };

            await using var context = await _browser.NewContextAsync(contextOptions);
            var page = await context.NewPageAsync();

            page.SetDefaultTimeout(_options.DefaultTimeoutMs);
            page.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);

            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            if (response == null)
                return BrowserPageResult.Fail("browser_navigation_failed");

            var statusCode = response.Status;
            if (statusCode >= 400)
                return BrowserPageResult.Fail($"browser_http_{statusCode}");

            var finalUrl = page.Url;
            var title = await page.TitleAsync();

            // Extract visible text content via JavaScript
            var textContent = await page.EvaluateAsync<string>("""
                (() => {
                    const body = document.body;
                    if (!body) return '';

                    // Remove script, style, nav, header, footer, noscript elements
                    const removes = body.querySelectorAll('script, style, nav, header, footer, noscript, svg, [aria-hidden="true"]');
                    removes.forEach(el => el.remove());

                    // Get visible text
                    const text = body.innerText || body.textContent || '';
                    return text.replace(/\s+/g, ' ').trim();
                })()
            """);

            // Extract meta description
            var description = await page.EvaluateAsync<string>("""
                (() => {
                    const meta = document.querySelector('meta[name="description"]');
                    return meta ? (meta.getAttribute('content') || '') : '';
                })()
            """);

            // Truncate content
            var maxLen = _options.MaxContentLength;
            if (textContent.Length > maxLen)
                textContent = textContent[..maxLen];

            // Optional screenshot
            byte[]? screenshot = null;
            if (_options.ScreenshotOnEvidence)
            {
                screenshot = await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Type = ScreenshotType.Png,
                    FullPage = false
                });
            }

            return new BrowserPageResult
            {
                Success = true,
                StatusCode = statusCode,
                FinalUrl = finalUrl,
                Title = title,
                Description = description,
                TextContent = textContent,
                Screenshot = screenshot,
                FetchedAt = DateTimeOffset.UtcNow
            };
        }
        catch (PlaywrightException ex)
        {
            _logger.LogWarning(ex, "Playwright error fetching {Url}", url);
            return BrowserPageResult.Fail($"browser_playwright_error: {ex.Message}");
        }
        catch (TimeoutException)
        {
            return BrowserPageResult.Fail("browser_navigation_timeout");
        }
        finally
        {
            _pageSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;

        _initLock.Dispose();
        _pageSemaphore.Dispose();
    }
}

public sealed class BrowserPageResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int StatusCode { get; set; }
    public string FinalUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public byte[]? Screenshot { get; set; }
    public DateTimeOffset FetchedAt { get; set; }

    public static BrowserPageResult Fail(string error)
        => new() { Success = false, Error = error };
}

public sealed class BrowserWorkerOptions
{
    public bool Headless { get; set; } = true;
    public float DefaultTimeoutMs { get; set; } = 30000;
    public float NavigationTimeoutMs { get; set; } = 30000;
    public int MaxConcurrentPages { get; set; } = 3;
    public string UserAgent { get; set; } = "Bricks4Agent-BrowserWorker/1.0";
    public int MaxContentLength { get; set; } = 8000;
    public bool ScreenshotOnEvidence { get; set; }
}
