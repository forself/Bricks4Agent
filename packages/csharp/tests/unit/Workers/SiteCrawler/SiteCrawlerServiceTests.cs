using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
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
                "text/html",
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
                "text/html; charset=utf-8",
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
        var service = new SiteCrawlerService(
            fetcher,
            new DeterministicSiteExtractor(),
            NullLogger<SiteCrawlerService>.Instance);

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
                "text/html",
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
    public async Task CrawlAsync_WhenFetchedHtmlExceedsTotalByteBudget_TruncatesWithoutAddingPage()
    {
        var oversizedHtml = $"""
            <html>
            <head><title>Large</title></head>
            <body><section><h1>Large</h1><p>{new string('x', 128)}</p></section></body>
            </html>
            """;
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/"),
                200,
                "text/html",
                oversizedHtml),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 0 },
            Budgets = new SiteCrawlBudgets
            {
                MaxPages = 10,
                MaxTotalBytes = 32,
                WallClockTimeoutSeconds = 180,
            },
        }, CancellationToken.None);

        fetcher.RequestedUrls.Should().ContainSingle().Which.Should().Be("https://example.com/docs/");
        result.Limits.ByteLimitHit.Should().BeTrue();
        result.Limits.Truncated.Should().BeTrue();
        result.Pages.Should().BeEmpty();
        result.ExtractedModel.Pages.Should().BeEmpty();
    }

    [Fact]
    public async Task CrawlAsync_WhenFetcherReportsResponseTooLarge_SetsByteLimitAndStops()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Fail(
                new Uri("https://example.com/docs/"),
                200,
                "response_too_large"),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 0 },
            Budgets = new SiteCrawlBudgets
            {
                MaxPages = 10,
                MaxTotalBytes = 64,
                WallClockTimeoutSeconds = 180,
            },
        }, CancellationToken.None);

        fetcher.RequestedUrls.Should().ContainSingle().Which.Should().Be("https://example.com/docs/");
        fetcher.RequestedMaxBytes.Should().ContainSingle().Which.Should().Be(64);
        result.Limits.ByteLimitHit.Should().BeTrue();
        result.Limits.Truncated.Should().BeTrue();
        result.Pages.Should().BeEmpty();
    }

    [Fact]
    public async Task CrawlAsync_WhenFetchExceedsWallClockBudget_TruncatesWithoutAddingPage()
    {
        var fetcher = new DelayingPageFetcher(TimeSpan.FromSeconds(5));
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 0 },
            Budgets = new SiteCrawlBudgets
            {
                MaxPages = 10,
                MaxTotalBytes = 1024,
                WallClockTimeoutSeconds = 1,
            },
        }, CancellationToken.None);

        fetcher.RequestedUrls.Should().ContainSingle().Which.Should().Be("https://example.com/docs/");
        result.Limits.Truncated.Should().BeTrue();
        result.Pages.Should().BeEmpty();
    }

    [Fact]
    public async Task CrawlAsync_WhenWallClockBudgetIsZero_TruncatesBeforeFetching()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/"),
                200,
                "text/html",
                "<html><head><title>Root</title></head><body><section><h1>Root</h1></section></body></html>"),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 0 },
            Budgets = new SiteCrawlBudgets
            {
                MaxPages = 10,
                WallClockTimeoutSeconds = 0,
            },
        }, CancellationToken.None);

        fetcher.RequestedUrls.Should().BeEmpty();
        result.Limits.Truncated.Should().BeTrue();
        result.Pages.Should().BeEmpty();
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

    [Fact]
    public void HttpPageFetcher_DefaultConstructorCreatesProductionFetcher()
    {
        var fetcher = new HttpPageFetcher();

        fetcher.Should().NotBeNull();
    }

    [Fact]
    public async Task FetchAsync_WhenResponseExceedsMaxBytes_ReturnsTooLargeWithoutFullBody()
    {
        var largeHtml = $"<html><body>{new string('x', 256)}</body></html>";
        var handler = new FakeHttpMessageHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(largeHtml, System.Text.Encoding.UTF8, "text/html"),
        });
        var fetcher = new HttpPageFetcher(
            handler,
            new FakeHostAddressResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new Uri("https://example.com/docs/"), 32, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be("response_too_large");
        result.Html.Should().BeEmpty();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task ResolveAllowedEndpointAsync_WhenResolvedAddressIsBlocked_FailsConnectTimeValidation(
        string resolvedAddress)
    {
        var resolver = new FakeHostAddressResolver(IPAddress.Parse(resolvedAddress));
        var endPoint = new DnsEndPoint("public.example", 443);

        var act = async () => await HttpPageFetcher.ResolveAllowedEndpointAsync(endPoint, resolver, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*blocked_resolved_ip*");
    }

    [Fact]
    public async Task FetchAsync_WhenConnectTimeValidationRejectsAddress_ReturnsFailure()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("blocked_resolved_ip"));
        var fetcher = new HttpPageFetcher(
            handler,
            new FakeHostAddressResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new Uri("https://public.example/docs/"), 1024, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be("blocked_resolved_ip");
        result.Html.Should().BeEmpty();
    }

    [Fact]
    public void PageFetchResultOk_PreservesRequestUriFinalUriAndContentType()
    {
        var uri = new Uri("https://example.com/docs/");
        var html = "<html><body>Docs</body></html>";

        var result = PageFetchResult.Ok(uri, 200, "text/html; charset=utf-8", html);

        result.Uri.Should().Be(uri);
        result.FinalUri.Should().Be(uri);
        result.ContentType.Should().Be("text/html; charset=utf-8");
        result.Html.Should().Be(html);
    }

    [Fact]
    public async Task FetchAsync_WhenResponseIsRedirect_DoesNotFollowRedirect()
    {
        var redirectTarget = new Uri("http://127.0.0.1/secret");
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found)
            {
                RequestMessage = request,
            };
            response.Headers.Location = redirectTarget;
            return response;
        });
        var fetcher = new HttpPageFetcher(
            handler,
            new FakeHostAddressResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new Uri("https://example.com/docs/"), CancellationToken.None);

        handler.Requests.Should().ContainSingle()
            .Which.RequestUri.Should().Be("https://example.com/docs/");
        result.IsSuccess.Should().BeFalse();
        result.Uri.Should().Be(new Uri("https://example.com/docs/"));
        result.FinalUri.Should().Be(new Uri("https://example.com/docs/"));
        result.RedirectUri.Should().Be(redirectTarget);
        result.FailureReason.Should().Be("redirect_not_followed");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task FetchAsync_WhenDnsResolvesToBlockedAddress_FailsBeforeSending(string resolvedAddress)
    {
        var handler = new FakeHttpMessageHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent("<html><body>Should not fetch</body></html>"),
        });
        var fetcher = new HttpPageFetcher(
            handler,
            new FakeHostAddressResolver(IPAddress.Parse(resolvedAddress)));

        var result = await fetcher.FetchAsync(new Uri("https://public.example/docs/"), CancellationToken.None);

        handler.Requests.Should().BeEmpty();
        result.IsSuccess.Should().BeFalse();
        result.Uri.Should().Be(new Uri("https://public.example/docs/"));
        result.FailureReason.Should().Be("blocked_resolved_ip");
    }

    private sealed class FakePageFetcher : IPageFetcher
    {
        private readonly IReadOnlyDictionary<string, PageFetchResult> pages;

        public FakePageFetcher(IReadOnlyDictionary<string, PageFetchResult> pages)
        {
            this.pages = pages;
        }

        public List<string> RequestedUrls { get; } = new();

        public List<long> RequestedMaxBytes { get; } = new();

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            return FetchAsync(uri, long.MaxValue, ct);
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            RequestedUrls.Add(uri.ToString());
            RequestedMaxBytes.Add(maxBytes);
            return Task.FromResult(pages.TryGetValue(uri.ToString(), out var result)
                ? result
                : PageFetchResult.Fail(uri, 404, "not_found"));
        }
    }

    private sealed class DelayingPageFetcher : IPageFetcher
    {
        private readonly TimeSpan delay;

        public DelayingPageFetcher(TimeSpan delay)
        {
            this.delay = delay;
        }

        public List<string> RequestedUrls { get; } = new();

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            return FetchAsync(uri, long.MaxValue, ct);
        }

        public async Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            RequestedUrls.Add(uri.ToString());
            await Task.Delay(delay, ct);
            return PageFetchResult.Ok(
                uri,
                200,
                "text/html",
                "<html><head><title>Late</title></head><body><section><h1>Late</h1></section></body></html>");
        }
    }

    private sealed class FakeHostAddressResolver : IHostAddressResolver
    {
        private readonly IReadOnlyList<IPAddress> addresses;

        public FakeHostAddressResolver(params IPAddress[] addresses)
        {
            this.addresses = addresses;
        }

        public Task<IReadOnlyList<IPAddress>> GetHostAddressesAsync(string host, CancellationToken ct)
        {
            return Task.FromResult(addresses);
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
        {
            this.send = send;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(send(request, cancellationToken));
        }
    }
}
