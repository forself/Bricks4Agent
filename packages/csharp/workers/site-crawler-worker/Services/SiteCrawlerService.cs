using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class SiteCrawlerService
{
    private readonly IPageFetcher pageFetcher;
    private readonly DeterministicSiteExtractor extractor;
    private readonly IVisualPageRenderer? visualRenderer;
    private readonly ILogger<SiteCrawlerService>? logger;

    public SiteCrawlerService(IPageFetcher pageFetcher, DeterministicSiteExtractor extractor)
        : this(pageFetcher, extractor, null, null)
    {
    }

    public SiteCrawlerService(
        IPageFetcher pageFetcher,
        DeterministicSiteExtractor extractor,
        ILogger<SiteCrawlerService>? logger)
        : this(pageFetcher, extractor, null, logger)
    {
    }

    public SiteCrawlerService(
        IPageFetcher pageFetcher,
        DeterministicSiteExtractor extractor,
        IVisualPageRenderer visualRenderer)
        : this(pageFetcher, extractor, visualRenderer, null)
    {
    }

    public SiteCrawlerService(
        IPageFetcher pageFetcher,
        DeterministicSiteExtractor extractor,
        IVisualPageRenderer? visualRenderer,
        ILogger<SiteCrawlerService>? logger)
    {
        this.pageFetcher = pageFetcher;
        this.extractor = extractor;
        this.visualRenderer = visualRenderer;
        this.logger = logger;
    }

    public async Task<SiteCrawlResult> CrawlAsync(SiteCrawlRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        logger?.LogDebug("Starting site crawl for {StartUrl}", request.StartUrl);

        var startValidation = SafeUrlPolicy.Validate(request.StartUrl);
        if (!startValidation.IsAllowed || startValidation.Uri is null)
        {
            throw new InvalidOperationException($"Invalid start URL: {startValidation.Reason}");
        }

        var scopeOptions = request.Scope ?? new SiteCrawlScope();
        var captureOptions = request.Capture ?? new SiteCrawlCaptureOptions();
        var budgets = request.Budgets ?? new SiteCrawlBudgets();
        var useLinkDepth = IsLinkDepthScope(scopeOptions);
        var maxLinkDepth = Math.Max(0, scopeOptions.MaxDepth);
        var scope = PathDepthScope.Create(
            startValidation.Uri,
            useLinkDepth ? CreateBoundaryScope(scopeOptions) : scopeOptions);
        var maxPages = Math.Max(0, budgets.MaxPages);
        var maxTotalBytes = Math.Max(0, budgets.MaxTotalBytes);
        var stopwatch = Stopwatch.StartNew();

        var result = new SiteCrawlResult
        {
            CrawlRunId = request.RequestId,
            Root = new SiteCrawlRoot
            {
                StartUrl = request.StartUrl,
                NormalizedStartUrl = startValidation.Uri.ToString(),
                Origin = scope.Origin,
                PathPrefix = scope.PathPrefix,
            },
        };

        if (IsWallClockBudgetExpired(stopwatch, budgets.WallClockTimeoutSeconds))
        {
            result.Limits.Truncated = true;
            return result;
        }

        if (maxPages == 0)
        {
            result.Limits.PageLimitHit = true;
            result.Limits.Truncated = true;
            return result;
        }

        var pending = new List<CrawlQueueItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var crawled = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;

        pending.Add(new CrawlQueueItem(startValidation.Uri, 0));
        seen.Add(BuildCrawlKey(startValidation.Uri));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (result.Pages.Count >= maxPages)
            {
                result.Limits.PageLimitHit = true;
                result.Limits.Truncated = true;
                break;
            }

            if (IsWallClockBudgetExpired(stopwatch, budgets.WallClockTimeoutSeconds))
            {
                result.Limits.Truncated = true;
                break;
            }

            var current = DequeueNext(pending, useLinkDepth);
            if (crawled.Contains(BuildCrawlKey(current.Uri)))
            {
                continue;
            }

            var remainingBytes = maxTotalBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                result.Limits.ByteLimitHit = true;
                result.Limits.Truncated = true;
                break;
            }

            PageFetchResult fetch;
            try
            {
                using var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                fetchCts.CancelAfter(GetRemainingWallClock(stopwatch, budgets.WallClockTimeoutSeconds));
                fetch = await pageFetcher.FetchAsync(current.Uri, remainingBytes, fetchCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                result.Limits.Truncated = true;
                break;
            }
            catch (Exception exception) when (IsRecoverableFetchException(exception))
            {
                logger?.LogDebug(exception, "Fetch failed for {Url}; excluding page and continuing crawl.", current.Uri);
                AddExcluded(result, excluded, current.Uri.ToString(), BuildRecoverableFetchFailureReason(exception));
                continue;
            }

            if (IsWallClockBudgetExpired(stopwatch, budgets.WallClockTimeoutSeconds))
            {
                result.Limits.Truncated = true;
                break;
            }

            if (!fetch.IsSuccess)
            {
                if (fetch.FailureReason == "response_too_large")
                {
                    result.Limits.ByteLimitHit = true;
                    result.Limits.Truncated = true;
                    break;
                }

                if (fetch.RedirectUri is not null)
                {
                    EnqueueCandidate(
                        fetch.RedirectUri,
                        current.Depth,
                        scope,
                        useLinkDepth,
                        maxLinkDepth,
                        result,
                        excluded,
                        seen,
                        pending);
                    continue;
                }

                AddExcluded(result, excluded, fetch.FinalUri.ToString(), fetch.FailureReason);
                continue;
            }

            var finalUri = RemoveFragment(fetch.FinalUri);
            var finalValidation = SafeUrlPolicy.Validate(finalUri.ToString());
            if (!finalValidation.IsAllowed || finalValidation.Uri is null)
            {
                AddExcluded(result, excluded, finalUri.ToString(), finalValidation.Reason);
                continue;
            }

            var finalScope = scope.Evaluate(finalValidation.Uri);
            if (!finalScope.IsAllowed)
            {
                AddExcluded(result, excluded, finalValidation.Uri.ToString(), finalScope.Reason);
                continue;
            }

            var effectiveUri = finalValidation.Uri;
            var effectiveScope = finalScope;
            var effectiveStatusCode = fetch.StatusCode;
            var effectiveHtml = fetch.Html;
            VisualPageSnapshot? visualSnapshot = null;
            if (visualRenderer is not null && captureOptions.RenderedDom)
            {
                var visualResult = await visualRenderer.CaptureAsync(finalValidation.Uri, ct);
                if (visualResult.Success)
                {
                    var visualFinalUri = RemoveFragment(visualResult.FinalUri);
                    var visualValidation = SafeUrlPolicy.Validate(visualFinalUri.ToString());
                    if (visualValidation.IsAllowed && visualValidation.Uri is not null)
                    {
                        var visualScope = scope.Evaluate(visualValidation.Uri);
                        if (visualScope.IsAllowed)
                        {
                            effectiveUri = visualValidation.Uri;
                            effectiveScope = visualScope;
                            effectiveStatusCode = visualResult.StatusCode == 0 ? fetch.StatusCode : visualResult.StatusCode;
                            effectiveHtml = visualResult.RenderedHtml;
                            visualSnapshot = visualResult.Snapshot;
                        }
                    }
                }
                else
                {
                    logger?.LogDebug(
                        "Visual render failed for {Url}: {Reason}; falling back to fetched HTML.",
                        finalValidation.Uri,
                        visualResult.FailureReason);
                }
            }

            var pageBytes = Encoding.UTF8.GetByteCount(effectiveHtml);
            if (totalBytes + pageBytes > maxTotalBytes)
            {
                result.Limits.ByteLimitHit = true;
                result.Limits.Truncated = true;
                break;
            }

            totalBytes += pageBytes;

            var finalKey = BuildCrawlKey(effectiveUri);
            if (!string.Equals(finalKey, BuildCrawlKey(current.Uri), StringComparison.Ordinal))
            {
                if (!seen.Add(finalKey) && crawled.Contains(finalKey))
                {
                    continue;
                }
            }

            if (!crawled.Add(finalKey))
            {
                continue;
            }

            var pageExtraction = extractor.ExtractPage(effectiveUri, effectiveHtml);
            var pageLinks = MergeLinks(pageExtraction.Links, visualSnapshot?.Links);
            var pageForms = visualSnapshot?.Forms.Count > 0 ? visualSnapshot.Forms : pageExtraction.Forms;
            result.Pages.Add(new SiteCrawlPage
            {
                Url = current.Uri.ToString(),
                FinalUrl = effectiveUri.ToString(),
                Depth = useLinkDepth ? current.Depth : effectiveScope.Depth,
                StatusCode = effectiveStatusCode,
                Title = pageExtraction.Title,
                Html = captureOptions.Html ? effectiveHtml : string.Empty,
                TextExcerpt = pageExtraction.TextExcerpt,
                Links = pageLinks,
                Forms = pageForms,
                VisualSnapshot = visualSnapshot,
            });
            result.ExtractedModel.Pages.Add(pageExtraction.Model);
            MergeThemeTokens(result.ExtractedModel.ThemeTokens, pageExtraction.ThemeTokens);

            foreach (var link in pageLinks)
            {
                EnqueueCandidate(
                    new Uri(link),
                    current.Depth + 1,
                    scope,
                    useLinkDepth,
                    maxLinkDepth,
                    result,
                    excluded,
                    seen,
                    pending);
            }
        }

        PopulateRouteGraph(result, scope);
        return result;
    }

    private static bool IsWallClockBudgetExpired(Stopwatch stopwatch, int wallClockTimeoutSeconds)
    {
        return wallClockTimeoutSeconds <= 0 ||
            stopwatch.Elapsed >= TimeSpan.FromSeconds(wallClockTimeoutSeconds);
    }

    private static TimeSpan GetRemainingWallClock(Stopwatch stopwatch, int wallClockTimeoutSeconds)
    {
        var remaining = TimeSpan.FromSeconds(wallClockTimeoutSeconds) - stopwatch.Elapsed;
        return remaining <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : remaining;
    }

    private static List<string> MergeLinks(IEnumerable<string> primary, IEnumerable<string>? secondary)
    {
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in primary.Concat(secondary ?? []))
        {
            if (seen.Add(link))
            {
                links.Add(link);
            }
        }

        return links;
    }

    private static bool IsRecoverableFetchException(Exception exception)
    {
        return exception is HttpRequestException or IOException or SocketException;
    }

    private static string BuildRecoverableFetchFailureReason(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => "fetch_http_request_failed",
            IOException => "fetch_io_failed",
            SocketException => "fetch_socket_failed",
            _ => "fetch_failed",
        };
    }

    private static bool IsLinkDepthScope(SiteCrawlScope scope)
    {
        return string.Equals(scope.Kind, "link_depth", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope.Kind, "crawl_depth", StringComparison.OrdinalIgnoreCase);
    }

    private static SiteCrawlScope CreateBoundaryScope(SiteCrawlScope scope)
    {
        return new SiteCrawlScope
        {
            Kind = scope.Kind,
            MaxDepth = int.MaxValue,
            SameOriginOnly = scope.SameOriginOnly,
            PathPrefixLock = scope.PathPrefixLock,
        };
    }

    private static CrawlQueueItem DequeueNext(List<CrawlQueueItem> pending, bool useLinkDepth)
    {
        var selectedIndex = 0;
        if (useLinkDepth)
        {
            // Site reconstruction needs vertical samples; wide top-level menus should not consume the whole page budget.
            for (var index = 1; index < pending.Count; index++)
            {
                if (pending[index].Depth > pending[selectedIndex].Depth)
                {
                    selectedIndex = index;
                }
            }
        }

        var selected = pending[selectedIndex];
        pending.RemoveAt(selectedIndex);
        return selected;
    }

    private static void EnqueueCandidate(
        Uri candidateUri,
        int candidateDepth,
        PathDepthScope scope,
        bool useLinkDepth,
        int maxLinkDepth,
        SiteCrawlResult result,
        ISet<string> excluded,
        ISet<string> seen,
        List<CrawlQueueItem> pending)
    {
        var candidateValidation = SafeUrlPolicy.Validate(candidateUri.ToString());
        if (!candidateValidation.IsAllowed || candidateValidation.Uri is null)
        {
            AddExcluded(result, excluded, candidateUri.ToString(), candidateValidation.Reason);
            return;
        }

        var candidateScope = scope.Evaluate(candidateValidation.Uri);
        if (!candidateScope.IsAllowed)
        {
            AddExcluded(result, excluded, candidateValidation.Uri.ToString(), candidateScope.Reason);
            return;
        }

        if (useLinkDepth && candidateDepth > maxLinkDepth)
        {
            AddExcluded(result, excluded, candidateValidation.Uri.ToString(), "outside_link_depth");
            return;
        }

        var candidateKey = BuildCrawlKey(candidateValidation.Uri);
        if (seen.Add(candidateKey))
        {
            pending.Add(new CrawlQueueItem(
                candidateValidation.Uri,
                useLinkDepth ? candidateDepth : candidateScope.Depth));
        }
    }

    private static void PopulateRouteGraph(SiteCrawlResult result, PathDepthScope scope)
    {
        var routePaths = new HashSet<string>(StringComparer.Ordinal);
        var pageRoutePaths = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < result.Pages.Count; index++)
        {
            var page = result.Pages[index];
            var finalUri = new Uri(page.FinalUrl);
            var routePath = BuildRoutePath(finalUri);
            var pageId = $"page-{index + 1}";
            routePaths.Add(routePath);
            pageRoutePaths[page.FinalUrl] = routePath;
            result.ExtractedModel.RouteGraph.Routes.Add(new ExtractedRoute
            {
                Path = routePath,
                PageId = pageId,
                Depth = page.Depth,
                Title = page.Title,
            });
        }

        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in result.Pages)
        {
            var from = pageRoutePaths[page.FinalUrl];
            foreach (var link in page.Links)
            {
                var linkValidation = SafeUrlPolicy.Validate(link);
                if (!linkValidation.IsAllowed || linkValidation.Uri is null)
                {
                    continue;
                }

                var linkScope = scope.Evaluate(linkValidation.Uri);
                if (!linkScope.IsAllowed)
                {
                    continue;
                }

                var to = BuildRoutePath(linkValidation.Uri);
                if (!routePaths.Contains(to))
                {
                    continue;
                }

                var edgeKey = $"{from}\n{to}";
                if (edgeKeys.Add(edgeKey))
                {
                    result.ExtractedModel.RouteGraph.Edges.Add(new ExtractedRouteEdge
                    {
                        From = from,
                        To = to,
                        Kind = "internal_link",
                    });
                }
            }
        }
    }

    private static void MergeThemeTokens(ExtractedThemeTokens target, ExtractedThemeTokens source)
    {
        foreach (var color in source.Colors)
        {
            target.Colors.TryAdd(color.Key, color.Value);
        }

        foreach (var typography in source.Typography)
        {
            target.Typography.TryAdd(typography.Key, typography.Value);
        }
    }

    private static void AddExcluded(
        SiteCrawlResult result,
        ISet<string> seen,
        string url,
        string reason)
    {
        var key = $"{url}\n{reason}";
        if (seen.Add(key))
        {
            result.Excluded.Add(new SiteCrawlExcludedUrl
            {
                Url = url,
                Reason = string.IsNullOrWhiteSpace(reason) ? "excluded" : reason,
            });
        }
    }

    private static string BuildCrawlKey(Uri uri)
    {
        return RemoveFragment(uri).ToString();
    }

    private static string BuildRoutePath(Uri uri)
    {
        return string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
    }

    private static Uri RemoveFragment(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    private sealed record CrawlQueueItem(Uri Uri, int Depth);
}
