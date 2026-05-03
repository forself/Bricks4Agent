using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public interface IVisualPageRenderer : IAsyncDisposable
{
    Task<VisualPageRenderResult> CaptureAsync(Uri uri, CancellationToken ct);
}

public sealed class VisualPageRenderResult
{
    public bool Success { get; init; }

    public Uri Uri { get; init; } = new("about:blank");

    public Uri FinalUri { get; init; } = new("about:blank");

    public int StatusCode { get; init; }

    public string RenderedHtml { get; init; } = string.Empty;

    public VisualPageSnapshot Snapshot { get; init; } = new();

    public string FailureReason { get; init; } = string.Empty;

    public static VisualPageRenderResult Ok(
        Uri finalUri,
        int statusCode,
        string renderedHtml,
        VisualPageSnapshot snapshot)
    {
        return new VisualPageRenderResult
        {
            Success = true,
            Uri = finalUri,
            FinalUri = finalUri,
            StatusCode = statusCode,
            RenderedHtml = renderedHtml,
            Snapshot = snapshot,
        };
    }

    public static VisualPageRenderResult Fail(Uri uri, string failureReason)
    {
        return new VisualPageRenderResult
        {
            Uri = uri,
            FinalUri = uri,
            FailureReason = failureReason,
        };
    }
}

public sealed class PlaywrightVisualPageRenderer : IVisualPageRenderer
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly VisualPageRendererOptions options;
    private readonly ILogger<PlaywrightVisualPageRenderer>? logger;
    private readonly SemaphoreSlim initLock = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;

    public PlaywrightVisualPageRenderer(VisualPageRendererOptions options, ILogger<PlaywrightVisualPageRenderer>? logger = null)
    {
        this.options = options;
        this.logger = logger;
    }

    public async Task<VisualPageRenderResult> CaptureAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            await EnsureInitializedAsync(ct);
            if (browser is null)
            {
                return VisualPageRenderResult.Fail(uri, "visual_browser_not_initialized");
            }

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = options.UserAgent,
                ViewportSize = new ViewportSize
                {
                    Width = options.ViewportWidth,
                    Height = options.ViewportHeight,
                },
                DeviceScaleFactor = options.DeviceScaleFactor,
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(options.DefaultTimeoutMs);
            page.SetDefaultNavigationTimeout(options.NavigationTimeoutMs);
            if (options.BlockHeavyResources)
            {
                await page.RouteAsync("**/*", async route =>
                {
                    var resourceType = route.Request.ResourceType;
                    if (resourceType is "image" or "media" or "font")
                    {
                        await route.AbortAsync();
                        return;
                    }

                    await route.ContinueAsync();
                });
            }

            var response = await page.GotoAsync(uri.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = options.NavigationTimeoutMs,
            });
            if (response is null)
            {
                return VisualPageRenderResult.Fail(uri, "visual_navigation_failed");
            }

            if (options.PostNavigationSettleMs > 0)
            {
                await page.WaitForTimeoutAsync(options.PostNavigationSettleMs);
            }

            if (options.NetworkIdleTimeoutMs > 0)
            {
                try
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                    {
                        Timeout = options.NetworkIdleTimeoutMs,
                    });
                }
                catch (TimeoutException)
                {
                    logger?.LogDebug("Network idle timeout while capturing {Url}; continuing with current render.", uri);
                }
                catch (PlaywrightException ex)
                {
                    logger?.LogDebug(ex, "Network idle wait failed while capturing {Url}; continuing with current render.", uri);
                }
            }

            var statusCode = response.Status;
            if (statusCode >= 400)
            {
                return VisualPageRenderResult.Fail(uri, $"visual_http_{statusCode}");
            }

            var renderedHtml = await page.ContentAsync();
            var snapshotJson = await page.EvaluateAsync<string>(BuildCaptureScript(options));
            var snapshot = JsonSerializer.Deserialize<VisualPageSnapshot>(snapshotJson, SnapshotJsonOptions) ?? new VisualPageSnapshot();

            return new VisualPageRenderResult
            {
                Success = true,
                Uri = uri,
                FinalUri = new Uri(page.Url),
                StatusCode = statusCode,
                RenderedHtml = renderedHtml,
                Snapshot = snapshot,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return VisualPageRenderResult.Fail(uri, "visual_navigation_timeout");
        }
        catch (PlaywrightException ex)
        {
            logger?.LogWarning(ex, "Playwright visual capture failed for {Url}", uri);
            return VisualPageRenderResult.Fail(uri, $"visual_playwright_error: {ex.Message}");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (browser is { IsConnected: true })
        {
            return;
        }

        await initLock.WaitAsync(ct);
        try
        {
            if (browser is { IsConnected: true })
            {
                return;
            }

            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless,
            });
        }
        finally
        {
            initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.CloseAsync();
            browser = null;
        }

        playwright?.Dispose();
        playwright = null;
        initLock.Dispose();
    }

    private static string BuildCaptureScript(VisualPageRendererOptions options)
    {
        var maxRegions = options.MaxRegions;
        var maxItemsPerRegion = options.MaxItemsPerRegion;
        var maxTextLength = options.MaxRegionTextLength;
        return $$"""
            (() => {
              const maxRegions = {{maxRegions}};
              const maxItemsPerRegion = {{maxItemsPerRegion}};
              const maxTextLength = {{maxTextLength}};
              const viewport = {
                width: window.innerWidth || document.documentElement.clientWidth || 0,
                height: window.innerHeight || document.documentElement.clientHeight || 0,
                device_scale_factor: window.devicePixelRatio || 1
              };

              const clean = value => (value || '').replace(/\s+/g, ' ').trim();
              const truncate = value => {
                const text = clean(value);
                return text.length > maxTextLength ? text.slice(0, maxTextLength).trim() : text;
              };
              const absUrl = value => {
                if (!value) return '';
                try { return new URL(value, document.baseURI).href.split('#')[0]; } catch { return ''; }
              };
              const rectOf = el => {
                const rect = el.getBoundingClientRect();
                return {
                  x: Math.round((rect.left + window.scrollX) * 100) / 100,
                  y: Math.round((rect.top + window.scrollY) * 100) / 100,
                  width: Math.round(rect.width * 100) / 100,
                  height: Math.round(rect.height * 100) / 100
                };
              };
              const visible = el => {
                if (!el || el.nodeType !== Node.ELEMENT_NODE) return false;
                const style = getComputedStyle(el);
                if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) return false;
                const rect = el.getBoundingClientRect();
                return rect.width >= 2 && rect.height >= 2;
              };
              const intersectsViewportX = el => {
                const rect = el.getBoundingClientRect();
                return rect.right > 0 && rect.left < viewport.width;
              };
              const selectorOf = el => {
                const tag = el.tagName.toLowerCase();
                if (el.id) return `${tag}#${CSS.escape(el.id)}`;
                const firstClass = [...el.classList].find(Boolean);
                if (firstClass) return `${tag}.${CSS.escape(firstClass)}`;
                const parent = el.parentElement;
                if (!parent) return tag;
                const index = [...parent.children].filter(child => child.tagName === el.tagName).indexOf(el) + 1;
                return `${tag}:nth-of-type(${Math.max(index, 1)})`;
              };
              const styleOf = el => {
                const style = getComputedStyle(el);
                return {
                  display: style.display,
                  position: style.position,
                  background_color: style.backgroundColor,
                  color: style.color,
                  font_family: style.fontFamily,
                  font_size: style.fontSize,
                  font_weight: style.fontWeight
                };
              };
              const backgroundUrlsOf = el => {
                const value = getComputedStyle(el).backgroundImage || '';
                if (!value || value === 'none') return [];
                const urls = [];
                const pattern = /url\((['"]?)(.*?)\1\)/g;
                let match;
                while ((match = pattern.exec(value)) !== null) {
                  const url = absUrl(match[2]);
                  if (url && /^https?:\/\//i.test(url)) urls.push(url);
                }
                return urls;
              };
              const backgroundCandidatesOf = root => [root, ...root.querySelectorAll('section, div, article, li, a, figure, span')]
                .filter(visible)
                .slice(0, 80);
              const firstBackgroundUrl = root => {
                for (const el of backgroundCandidatesOf(root)) {
                  const urls = backgroundUrlsOf(el);
                  if (urls.length > 0) return urls[0];
                }
                return '';
              };
              const tokensOf = el => `${el.tagName} ${el.id || ''} ${el.className || ''} ${el.getAttribute('role') || ''} ${el.getAttribute('aria-label') || ''}`.toLowerCase();
              const tokenListOf = el => tokensOf(el).split(/[^a-zA-Z0-9]+/).filter(Boolean);
              const hasAny = (tokens, values) => tokens.some(token => values.includes(token));
              const hasSearchControls = el => {
                const controls = [...el.querySelectorAll('input, button')];
                if (controls.length === 0) return false;
                return controls.some(control => {
                  const text = `${control.getAttribute('type') || ''} ${control.getAttribute('name') || ''} ${control.id || ''} ${control.value || ''} ${control.innerText || ''}`.toLowerCase();
                  return /\b(search|query|keyword|keywords)\b/.test(text);
                });
              };
              const roleOf = el => {
                const tag = el.tagName.toLowerCase();
                const tokens = tokensOf(el);
                const tokenList = tokenListOf(el);
                const rect = el.getBoundingClientRect();
                const explicitRole = (el.getAttribute('role') || '').toLowerCase();
                const hasSiteHeaderToken = explicitRole === 'banner' || /siteheader|global-header|globalheader|main-header|masthead|usa-banner/.test(tokens);
                const hasSiteFooterToken = /sitefooter|global-footer|globalfooter|main-footer|contentinfo/.test(tokens);
                const hasHeaderNavigation = el.querySelectorAll('a[href], nav, button').length >= 2;
                const hasFooterNotice = /copyright|all rights reserved|privacy|terms/.test((el.innerText || '').toLowerCase());
                if (tokens.includes('carousel') || tokens.includes('swiper') || tokens.includes('slick') || tokens.includes('slider') || tokens.includes('owl') || (tokens.includes('banner') && explicitRole !== 'banner')) return 'carousel';
                if (hasAny(tokenList, ['filter', 'filters', 'facet', 'facets', 'refine', 'refinement'])) {
                  return hasAny(tokenList, ['dashboard', 'report', 'reports', 'analytics']) ? 'filter_bar' : 'filters';
                }
                if (hasAny(tokenList, ['result', 'results', 'resultlist', 'listing', 'listings', 'searchresults'])) return 'results';
                if (hasAny(tokenList, ['pagination', 'pager', 'pages', 'pagenav'])) return 'pagination';
                if (hasAny(tokenList, ['search', 'searchbox', 'searchform', 'query', 'keyword', 'keywords']) || hasSearchControls(el)) return 'search';
                if (hasAny(tokenList, ['stats', 'stat', 'metrics', 'metric', 'numbers', 'counter', 'kpi', 'scorecard', 'indicator', 'indicators'])) return 'metrics';
                if (hasAny(tokenList, ['chart', 'charts', 'graph', 'graphs', 'visualization', 'visualisation', 'viz'])) return 'chart';
                if (hasAny(tokenList, ['table', 'datatable', 'dataset', 'data', 'spreadsheet']) || tag === 'table') return 'data_table';
                if (hasAny(tokenList, ['step', 'steps', 'wizard', 'progress', 'stepper', 'flow'])) return 'steps';
                if (hasAny(tokenList, ['validation', 'error', 'errors', 'alert', 'required', 'notice'])) return 'validation';
                if (hasAny(tokenList, ['actions', 'actionbar', 'buttons', 'submit', 'continue', 'controls'])) return 'action_bar';
                if (hasAny(tokenList, ['showcase', 'producthero'])) return 'product_hero';
                if (hasAny(tokenList, ['proof', 'trust', 'testimonial', 'testimonials', 'logos', 'customers'])) return 'proof';
                if (hasAny(tokenList, ['pricing', 'price', 'prices', 'billing', 'plans', 'plan'])) return 'pricing';
                if (hasAny(tokenList, ['cta', 'calltoaction', 'signup', 'start', 'contactsales'])) return 'cta';
                if (hasAny(tokenList, ['product', 'products', 'offer', 'offers', 'solution', 'solutions'])) return 'products';
                if (rect.top < 180 && hasHeaderNavigation && (el.querySelector('img') || hasAny(tokenList, ['head', 'header', 'topnav', 'mainmenu', 'mlogo']))) return 'header';
                if ((tag === 'header' && (hasSiteHeaderToken || (rect.top < 220 && hasHeaderNavigation))) || hasSiteHeaderToken) return 'header';
                if ((tag === 'footer' && (hasSiteFooterToken || hasFooterNotice)) || hasSiteFooterToken) return 'footer';
                if ((tag === 'nav' && rect.top < 260) || tokens.includes('navbar') || tokens.includes('navigation') || hasAny(tokenList, ['topnav', 'mainmenu'])) return 'nav';
                if (tag === 'form' || el.querySelector('input, textarea, select')) return 'form';
                if (tokens.includes('news') || tokens.includes('announcement') || tokens.includes('latest')) return 'news';
                if (tokens.includes('gallery') || tokens.includes('spotlight') || tokens.includes('photo')) return 'carousel';
                if ((tokens.includes('hero') || el.querySelector('h1')) && rect.top < viewport.height * 1.1) return 'hero';
                if (el.querySelectorAll('article, li, .card, .item, .d-item, .mbox, .listBS > *').length >= 2) return 'card_grid';
                return 'content';
              };
              const mediaOf = root => {
                const imageMedia = [...(root.matches('img') ? [root] : []), ...root.querySelectorAll('img, picture img')]
                .filter(visible)
                .map(img => ({
                  kind: 'image',
                  url: absUrl(img.currentSrc || img.src || img.getAttribute('data-src') || img.getAttribute('data-original')),
                  alt: clean(img.alt || '')
                }))
                .filter(media => media.url);
                const backgroundMedia = backgroundCandidatesOf(root)
                  .flatMap(el => backgroundUrlsOf(el).map(url => ({
                    kind: 'image',
                    url,
                    alt: clean(el.getAttribute('aria-label') || el.getAttribute('title') || headlineOf(el) || '')
                  })));
                const seenMedia = new Set();
                return [...imageMedia, ...backgroundMedia]
                  .filter(media => {
                    if (seenMedia.has(media.url)) return false;
                    seenMedia.add(media.url);
                    return true;
                  })
                  .slice(0, 8);
              };
              const actionsOf = root => [...root.querySelectorAll('a[href], button')]
                .filter(visible)
                .slice(0, 16)
                .map(action => ({
                  label: clean(action.innerText || action.getAttribute('aria-label') || action.getAttribute('title') || ''),
                  url: action.tagName.toLowerCase() === 'a' ? absUrl(action.getAttribute('href')) : '',
                  kind: /primary|apply|cta|btn-primary/i.test(`${action.className || ''} ${action.id || ''}`) ? 'primary' : 'secondary'
                }))
                .filter(action => action.label || action.url);
              const itemOf = item => {
                const links = [...item.querySelectorAll('a[href]')];
                const textLink = links.find(link => clean(link.innerText || link.getAttribute('aria-label') || link.getAttribute('title') || ''));
                const firstLink = textLink || links[0];
                const firstImage = item.querySelector('img');
                const backgroundUrl = firstImage ? '' : firstBackgroundUrl(item);
                const heading = item.querySelector('h1,h2,h3,h4,h5,h6');
                let title = clean(heading?.innerText || textLink?.innerText || firstImage?.alt || '');
                let body = truncate(item.innerText || '');
                if (title && body.startsWith(title)) body = clean(body.slice(title.length));
                return {
                  title,
                  body,
                  url: firstLink ? absUrl(firstLink.getAttribute('href')) : '',
                  media_url: firstImage ? absUrl(firstImage.currentSrc || firstImage.src || firstImage.getAttribute('data-src') || firstImage.getAttribute('data-original')) : backgroundUrl,
                  media_alt: firstImage ? clean(firstImage.alt || '') : title
                };
              };
              const itemsOf = root => {
                const candidates = [...root.querySelectorAll('article, li, .card, .item, .d-item, .mbox, .slide, .swiper-slide, .slick-slide, .listBS > *')]
                  .filter(visible)
                  .filter(intersectsViewportX)
                  .filter(item => clean(item.innerText || '') || item.querySelector('img') || firstBackgroundUrl(item));
                return candidates.slice(0, maxItemsPerRegion).map(itemOf).filter(item => item.title || item.body || item.media_url);
              };
              const headlineOf = root => clean(root.querySelector('h1,h2,h3,h4,h5,h6')?.innerText || '');
              const regionSelector = [
                'header',
                'nav',
                'main > section',
                'main > div',
                'main [id^="Dyn_"]',
                'main .module',
                'main section.mb',
                'main .row.listBS',
                'main .d-item',
                'main .mbox',
                'main header.mt',
                'main nav',
                '[class~="head" i]',
                '[class~="mlogo" i]',
                'body > section',
                'body > main',
                'body > div',
                'article',
                'form',
                'footer',
                '[id*="banner" i]',
                '[class*="banner" i]',
                '[class*="carousel" i]',
                '[class*="slider" i]',
                '[class*="swiper" i]',
                '[class*="slick" i]',
                '[class*="owl-carousel" i]',
                '[class*="topnav" i]',
                '[class*="mainmenu" i]',
                '[class*="footer" i]'
              ].join(',');
              const candidates = [...document.querySelectorAll(regionSelector)]
                .filter(visible)
                .filter(intersectsViewportX)
                .filter(el => !el.closest('script, style, noscript, template'))
                .filter(el => {
                  const text = clean(el.innerText || '');
                  return text || el.querySelector('img, a[href], input, textarea, select, button');
                });
              const seen = new Set();
              const regions = [];
              for (const el of candidates) {
                if (regions.length >= maxRegions) break;
                const selector = selectorOf(el);
                const bounds = rectOf(el);
                const key = `${Math.round(bounds.x)}:${Math.round(bounds.y)}:${Math.round(bounds.width)}:${Math.round(bounds.height)}:${selector}`;
                if (seen.has(key)) continue;
                seen.add(key);
                const role = roleOf(el);
                const region = {
                  id: el.id || `visual-${regions.length + 1}`,
                  role,
                  selector,
                  tag: el.tagName.toLowerCase(),
                  bounds,
                  style: styleOf(el),
                  headline: headlineOf(el),
                  text: truncate(el.innerText || ''),
                  media: mediaOf(el),
                  actions: actionsOf(el),
                  items: itemsOf(el)
                };
                regions.push(region);
              }
              const links = [...document.querySelectorAll('a[href]')]
                .map(anchor => absUrl(anchor.getAttribute('href')))
                .filter(url => /^https?:\/\//i.test(url))
                .filter((url, index, all) => all.indexOf(url) === index);
              return JSON.stringify({
                capture_mode: 'browser_render',
                viewport,
                regions,
                links,
                forms: [],
                rendered_text: truncate(document.body?.innerText || '')
              });
            })()
            """;
    }
}

public sealed class VisualPageRendererOptions
{
    public bool Headless { get; set; } = true;

    public int ViewportWidth { get; set; } = 1366;

    public int ViewportHeight { get; set; } = 900;

    public float DeviceScaleFactor { get; set; } = 1;

    public float DefaultTimeoutMs { get; set; } = 30000;

    public float NavigationTimeoutMs { get; set; } = 30000;

    public float PostNavigationSettleMs { get; set; } = 150;

    public float NetworkIdleTimeoutMs { get; set; } = 0;

    public string UserAgent { get; set; } = "Bricks4Agent-VisualRenderer/1.0";

    public bool BlockHeavyResources { get; set; }

    public int MaxRegions { get; set; } = 80;

    public int MaxItemsPerRegion { get; set; } = 24;

    public int MaxRegionTextLength { get; set; } = 2000;
}
