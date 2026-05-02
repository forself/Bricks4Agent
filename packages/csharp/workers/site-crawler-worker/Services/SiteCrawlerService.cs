using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class SiteCrawlerService
{
    private readonly IPageFetcher pageFetcher;
    private readonly DeterministicSiteExtractor extractor;
    private readonly ILogger<SiteCrawlerService>? logger;

    public SiteCrawlerService(IPageFetcher pageFetcher, DeterministicSiteExtractor extractor)
        : this(pageFetcher, extractor, null)
    {
    }

    public SiteCrawlerService(
        IPageFetcher pageFetcher,
        DeterministicSiteExtractor extractor,
        ILogger<SiteCrawlerService>? logger)
    {
        this.pageFetcher = pageFetcher;
        this.extractor = extractor;
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
        var scope = PathDepthScope.Create(startValidation.Uri, scopeOptions);
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

        var queue = new Queue<CrawlQueueItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var crawled = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        long totalBytes = 0;

        queue.Enqueue(new CrawlQueueItem(startValidation.Uri));
        seen.Add(BuildCrawlKey(startValidation.Uri));

        while (queue.Count > 0)
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

            var current = queue.Dequeue();
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
                    EnqueueCandidate(fetch.RedirectUri, scope, result, excluded, seen, queue);
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

            var pageBytes = Encoding.UTF8.GetByteCount(fetch.Html);
            if (totalBytes + pageBytes > maxTotalBytes)
            {
                result.Limits.ByteLimitHit = true;
                result.Limits.Truncated = true;
                break;
            }

            totalBytes += pageBytes;

            var finalKey = BuildCrawlKey(finalValidation.Uri);
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

            var pageExtraction = extractor.ExtractPage(finalValidation.Uri, fetch.Html);
            result.Pages.Add(new SiteCrawlPage
            {
                Url = current.Uri.ToString(),
                FinalUrl = finalValidation.Uri.ToString(),
                Depth = finalScope.Depth,
                StatusCode = fetch.StatusCode,
                Title = pageExtraction.Title,
                Html = captureOptions.Html ? fetch.Html : string.Empty,
                TextExcerpt = pageExtraction.TextExcerpt,
                Links = pageExtraction.Links,
                Forms = pageExtraction.Forms,
            });
            result.ExtractedModel.Pages.Add(pageExtraction.Model);
            MergeThemeTokens(result.ExtractedModel.ThemeTokens, pageExtraction.ThemeTokens);

            foreach (var link in pageExtraction.Links)
            {
                EnqueueCandidate(new Uri(link), scope, result, excluded, seen, queue);
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

    private static void EnqueueCandidate(
        Uri candidateUri,
        PathDepthScope scope,
        SiteCrawlResult result,
        ISet<string> excluded,
        ISet<string> seen,
        Queue<CrawlQueueItem> queue)
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

        var candidateKey = BuildCrawlKey(candidateValidation.Uri);
        if (seen.Add(candidateKey))
        {
            queue.Enqueue(new CrawlQueueItem(candidateValidation.Uri));
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

    private sealed record CrawlQueueItem(Uri Uri);
}
