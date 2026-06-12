using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace BrowserWorker;

/// <summary>
/// Manages Playwright browser lifecycle and provides page operations.
/// One browser instance shared across requests; pages are created per-request.
/// </summary>
public sealed class PlaywrightBrowserService : IAsyncDisposable, IBrowserPageFetcher
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

            return await ExtractPageAsync(page, statusCode);
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

    /// <summary>
    /// Navigate level: load the start URL, then follow up to (maxSteps) in-scope
    /// internal links, extracting each step. Read-only — no clicking of buttons,
    /// no form interaction. Links are followed only within the same origin (or an
    /// allowed-host suffix), so navigate cannot wander off the governed site class.
    /// </summary>
    public async Task<BrowserNavigationResult> NavigateAsync(
        string startUrl,
        int maxSteps,
        IReadOnlyCollection<string>? allowedHostSuffixes = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        if (_browser == null)
            return BrowserNavigationResult.Fail("browser_not_initialized");

        await _pageSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = userAgent ?? _options.UserAgent
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(_options.DefaultTimeoutMs);
            page.SetDefaultNavigationTimeout(_options.NavigationTimeoutMs);

            var steps = new List<BrowserPageResult>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = startUrl;

            for (var step = 0; step <= Math.Max(0, maxSteps); step++)
            {
                var response = await page.GotoAsync(current, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                if (response == null)
                    return steps.Count == 0 ? BrowserNavigationResult.Fail("browser_navigation_failed") : BrowserNavigationResult.Ok(steps);
                if (response.Status >= 400)
                    return steps.Count == 0 ? BrowserNavigationResult.Fail($"browser_http_{response.Status}") : BrowserNavigationResult.Ok(steps);

                visited.Add(page.Url);
                steps.Add(await ExtractPageAsync(page, response.Status));

                if (step == maxSteps)
                    break;

                var next = await FindNextInScopeLinkAsync(page, allowedHostSuffixes, visited);
                if (next == null)
                    break;
                current = next;
            }

            return BrowserNavigationResult.Ok(steps);
        }
        catch (PlaywrightException ex)
        {
            return BrowserNavigationResult.Fail($"browser_playwright_error: {ex.Message}");
        }
        catch (TimeoutException)
        {
            return BrowserNavigationResult.Fail("browser_navigation_timeout");
        }
        finally
        {
            _pageSemaphore.Release();
        }
    }

    private async Task<BrowserPageResult> ExtractPageAsync(IPage page, int statusCode)
    {
        var finalUrl = page.Url;
        var title = await page.TitleAsync();

        var textContent = await page.EvaluateAsync<string>("""
            (() => {
                const body = document.body;
                if (!body) return '';
                const removes = body.querySelectorAll('script, style, nav, header, footer, noscript, svg, [aria-hidden="true"]');
                removes.forEach(el => el.remove());
                const text = body.innerText || body.textContent || '';
                return text.replace(/\s+/g, ' ').trim();
            })()
        """);

        var description = await page.EvaluateAsync<string>("""
            (() => {
                const meta = document.querySelector('meta[name="description"]');
                return meta ? (meta.getAttribute('content') || '') : '';
            })()
        """);

        var maxLen = _options.MaxContentLength;
        if (textContent.Length > maxLen)
            textContent = textContent[..maxLen];

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

    /// <summary>Find the first in-scope, unvisited internal link href on the current page.</summary>
    private static async Task<string?> FindNextInScopeLinkAsync(
        IPage page,
        IReadOnlyCollection<string>? allowedHostSuffixes,
        HashSet<string> visited)
    {
        var hrefs = await page.EvaluateAsync<string[]>("""
            (() => Array.from(document.querySelectorAll('a[href]'))
                .map(a => a.href)
                .filter(h => h.startsWith('http')))()
        """);

        var currentHost = new Uri(page.Url).Host;
        foreach (var href in hrefs)
        {
            if (!Uri.TryCreate(href, UriKind.Absolute, out var candidate))
                continue;
            var clean = new UriBuilder(candidate) { Fragment = string.Empty }.Uri.ToString();
            if (visited.Contains(clean))
                continue;
            if (IsHostInScope(candidate.Host, currentHost, allowedHostSuffixes))
                return clean;
        }
        return null;
    }

    private static bool IsHostInScope(string host, string currentHost, IReadOnlyCollection<string>? allowedHostSuffixes)
    {
        if (string.Equals(host, currentHost, StringComparison.OrdinalIgnoreCase))
            return true;
        if (allowedHostSuffixes == null)
            return false;
        foreach (var suffix in allowedHostSuffixes)
        {
            var s = suffix.Trim('.').ToLowerInvariant();
            if (s.Length == 0)
                continue;
            if (string.Equals(host, s, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
