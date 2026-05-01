using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteCrawlerServiceTests
{
    [Fact]
    public async Task CrawlAsync_CrawlsPathDepthScopeAndReturnsExtractedModel()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/"),
                200,
                """
                <html>
                <head>
                  <title>Root</title>
                  <style>:root { --brand: #3366ff; }</style>
                </head>
                <body>
                  <section>
                    <h1>Root</h1>
                    <a href="/docs/a">A</a>
                    <a href="/docs/a/detail">Detail</a>
                    <a href="/other">Other</a>
                  </section>
                </body>
                </html>
                """),
            ["https://example.com/docs/a"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/a"),
                200,
                """
                <html>
                <head><title>A</title></head>
                <body>
                  <section>
                    <h1>A</h1>
                    <p>Page A</p>
                  </section>
                </body>
                </html>
                """),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            RequestId = "crawl-1",
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope
            {
                MaxDepth = 1,
                SameOriginOnly = true,
                PathPrefixLock = true,
            },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 },
        }, CancellationToken.None);

        result.Root.StartUrl.Should().Be("https://example.com/docs/");
        result.Root.NormalizedStartUrl.Should().Be("https://example.com/docs/");
        result.Root.Origin.Should().Be("https://example.com");
        result.Root.PathPrefix.Should().Be("/docs/");
        result.Pages.Select(page => page.FinalUrl).Should().Equal(
            "https://example.com/docs/",
            "https://example.com/docs/a");
        result.Pages.Select(page => page.Title).Should().Equal("Root", "A");
        result.Excluded.Should().Contain(excluded =>
            excluded.Url == "https://example.com/docs/a/detail" &&
            excluded.Reason == "outside_path_depth");
        result.Excluded.Should().Contain(excluded =>
            excluded.Url == "https://example.com/other" &&
            excluded.Reason == "outside_path_prefix");
        result.ExtractedModel.Pages.Should().HaveCount(2);
        result.ExtractedModel.ThemeTokens.Colors.Should().ContainKey("brand").WhoseValue.Should().Be("#3366ff");
        result.ExtractedModel.RouteGraph.Routes.Should().HaveCount(2);
        result.ExtractedModel.RouteGraph.Routes.Select(route => route.Path).Should().Equal("/docs/", "/docs/a");
        result.ExtractedModel.RouteGraph.Edges.Should().Contain(edge =>
            edge.From == "/docs/" &&
            edge.To == "/docs/a" &&
            edge.Kind == "internal_link");
    }

    [Fact]
    public async Task CrawlAsync_WhenCaptureHtmlIsFalse_BlanksPageHtml()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/"),
                200,
                "<html><head><title>Root</title></head><body><section><h1>Root</h1></section></body></html>"),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 0 },
            Capture = new SiteCrawlCaptureOptions { Html = false },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 },
        }, CancellationToken.None);

        result.Pages.Should().ContainSingle().Which.Html.Should().BeEmpty();
        result.ExtractedModel.Pages.Should().ContainSingle();
    }

    [Fact]
    public async Task CrawlAsync_WhenStartUrlIsUnsafe_ThrowsWithPolicyReasonAndDoesNotFetch()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal));
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var act = async () => await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "http://127.0.0.1/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 1 },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 },
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*blocked_host*");
        fetcher.RequestedUrls.Should().BeEmpty();
    }

    private sealed class FakePageFetcher : IPageFetcher
    {
        private readonly IReadOnlyDictionary<string, PageFetchResult> pages;

        public FakePageFetcher(IReadOnlyDictionary<string, PageFetchResult> pages)
        {
            this.pages = pages;
        }

        public List<string> RequestedUrls { get; } = new();

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            RequestedUrls.Add(uri.ToString());
            return Task.FromResult(pages.TryGetValue(uri.ToString(), out var result)
                ? result
                : PageFetchResult.Fail(uri, 404, "not_found"));
        }
    }
}
