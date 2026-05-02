using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
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
    public async Task CrawlAsync_WithLinkDepth_CrawlsByLinkHopsIgnoringUrlPath()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = PageFetchResult.Ok(
                new Uri("https://example.com/"),
                200,
                "text/html",
                """
                <html><head><title>Root</title></head>
                <body><main><a href="/a.aspx">A</a></main></body></html>
                """),
            ["https://example.com/a.aspx"] = PageFetchResult.Ok(
                new Uri("https://example.com/a.aspx"),
                200,
                "text/html",
                """
                <html><head><title>A</title></head>
                <body><main><a href="/b.aspx">B</a></main></body></html>
                """),
            ["https://example.com/b.aspx"] = PageFetchResult.Ok(
                new Uri("https://example.com/b.aspx"),
                200,
                "text/html",
                """
                <html><head><title>B</title></head>
                <body><main><a href="/c.aspx">C</a></main></body></html>
                """),
            ["https://example.com/c.aspx"] = PageFetchResult.Ok(
                new Uri("https://example.com/c.aspx"),
                200,
                "text/html",
                """
                <html><head><title>C</title></head>
                <body><main><h1>C</h1></main></body></html>
                """),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/",
            Scope = new SiteCrawlScope
            {
                Kind = "link_depth",
                MaxDepth = 2,
                SameOriginOnly = true,
                PathPrefixLock = true,
            },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 },
        }, CancellationToken.None);

        result.Pages.Select(page => (page.FinalUrl, page.Depth)).Should().Equal(
            ("https://example.com/", 0),
            ("https://example.com/a.aspx", 1),
            ("https://example.com/b.aspx", 2));
        result.Pages.Should().NotContain(page => page.FinalUrl == "https://example.com/c.aspx");
        result.Excluded.Should().Contain(excluded =>
            excluded.Url == "https://example.com/c.aspx" &&
            excluded.Reason == "outside_link_depth");
        result.ExtractedModel.RouteGraph.Routes.Select(route => (route.Path, route.Depth)).Should().Equal(
            ("/", 0),
            ("/a.aspx", 1),
            ("/b.aspx", 2));
    }

    [Fact]
    public async Task CrawlAsync_WithLinkDepthAndPageBudget_StillSamplesDeeperHops()
    {
        var pages = new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = PageFetchResult.Ok(
                new Uri("https://example.com/"),
                200,
                "text/html",
                """
                <html><head><title>Root</title></head><body><main>
                  <a href="/branch.aspx">Branch</a>
                  <a href="/shallow-1.aspx">Shallow 1</a>
                  <a href="/shallow-2.aspx">Shallow 2</a>
                  <a href="/shallow-3.aspx">Shallow 3</a>
                  <a href="/shallow-4.aspx">Shallow 4</a>
                </main></body></html>
                """),
            ["https://example.com/branch.aspx"] = PageFetchResult.Ok(
                new Uri("https://example.com/branch.aspx"),
                200,
                "text/html",
                """
                <html><head><title>Branch</title></head>
                <body><main><a href="/child.aspx">Child</a></main></body></html>
                """),
            ["https://example.com/child.aspx"] = PageFetchResult.Ok(
                new Uri("https://example.com/child.aspx"),
                200,
                "text/html",
                """
                <html><head><title>Child</title></head>
                <body><main><a href="/grandchild.aspx">Grandchild</a></main></body></html>
                """),
            ["https://example.com/grandchild.aspx"] = PageFetchResult.Ok(
                new Uri("https://example.com/grandchild.aspx"),
                200,
                "text/html",
                """
                <html><head><title>Grandchild</title></head>
                <body><main><h1>Grandchild</h1></main></body></html>
                """),
        };

        for (var index = 1; index <= 4; index++)
        {
            pages[$"https://example.com/shallow-{index}.aspx"] = PageFetchResult.Ok(
                new Uri($"https://example.com/shallow-{index}.aspx"),
                200,
                "text/html",
                $"<html><head><title>Shallow {index}</title></head><body><main><h1>Shallow {index}</h1></main></body></html>");
        }

        var service = new SiteCrawlerService(new FakePageFetcher(pages), new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/",
            Scope = new SiteCrawlScope
            {
                Kind = "link_depth",
                MaxDepth = 3,
                SameOriginOnly = true,
                PathPrefixLock = true,
            },
            Budgets = new SiteCrawlBudgets { MaxPages = 4 },
        }, CancellationToken.None);

        result.Limits.PageLimitHit.Should().BeTrue();
        result.Pages.Select(page => (page.FinalUrl, page.Depth)).Should().Equal(
            ("https://example.com/", 0),
            ("https://example.com/branch.aspx", 1),
            ("https://example.com/child.aspx", 2),
            ("https://example.com/grandchild.aspx", 3));
    }

    [Fact]
    public async Task CrawlAsync_WhenVisualRendererIsAvailable_UsesRenderedDomForExtractionAndLinks()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = PageFetchResult.Ok(
                new Uri("https://example.com/"),
                200,
                "text/html",
                "<html><head><title>Static</title></head><body><main><section><h1>Static Source</h1></section></main></body></html>"),
            ["https://example.com/rendered"] = PageFetchResult.Ok(
                new Uri("https://example.com/rendered"),
                200,
                "text/html",
                "<html><head><title>Rendered Child</title></head><body><main><section><h1>Rendered Child</h1></section></main></body></html>"),
        });
        var renderer = new FakeVisualPageRenderer(new Dictionary<string, VisualPageRenderResult>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = VisualPageRenderResult.Ok(
                new Uri("https://example.com/"),
                200,
                """
                <html><head><title>Rendered</title></head><body>
                  <main><section class="hero"><h1>Rendered Browser DOM</h1><a href="/rendered">Rendered child</a></section></main>
                </body></html>
                """,
                new VisualPageSnapshot
                {
                    CaptureMode = "browser_render",
                    Regions =
                    [
                        new VisualRegion
                        {
                            Role = "hero",
                            Selector = "section.hero",
                            Headline = "Rendered Browser DOM",
                            Text = "Rendered Browser DOM Rendered child",
                        },
                    ],
                }),
        });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor(), renderer);

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/",
            Scope = new SiteCrawlScope
            {
                Kind = "link_depth",
                MaxDepth = 1,
                SameOriginOnly = true,
                PathPrefixLock = true,
            },
            Capture = new SiteCrawlCaptureOptions { Html = true, RenderedDom = true },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 },
        }, CancellationToken.None);

        result.Pages.Select(page => page.FinalUrl).Should().Equal(
            "https://example.com/",
            "https://example.com/rendered");
        result.Pages[0].Html.Should().Contain("Rendered Browser DOM");
        result.Pages[0].VisualSnapshot.Should().NotBeNull();
        result.ExtractedModel.Pages[0].Sections.Should()
            .Contain(section => section.Headline == "Rendered Browser DOM");
    }

    [Fact]
    public async Task CrawlAsync_WhenOnePageFetchThrowsRecoverableNetworkError_ExcludesPageAndContinues()
    {
        var fetcher = new ThrowingPageFetcher(
            new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
            {
                ["https://example.com/"] = PageFetchResult.Ok(
                    new Uri("https://example.com/"),
                    200,
                    "text/html",
                    """
                    <html><head><title>Root</title></head><body>
                      <main>
                        <a href="/bad">Bad</a>
                        <a href="/good">Good</a>
                      </main>
                    </body></html>
                    """),
                ["https://example.com/good"] = PageFetchResult.Ok(
                    new Uri("https://example.com/good"),
                    200,
                    "text/html",
                    "<html><head><title>Good</title></head><body><main><h1>Good</h1></main></body></html>"),
            },
            new Dictionary<string, Exception>(StringComparer.Ordinal)
            {
                ["https://example.com/bad"] = new HttpRequestException("connection reset"),
            });
        var service = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/",
            Scope = new SiteCrawlScope
            {
                Kind = "link_depth",
                MaxDepth = 1,
                SameOriginOnly = true,
                PathPrefixLock = true,
            },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 },
        }, CancellationToken.None);

        result.Pages.Select(page => page.FinalUrl).Should().Equal(
            "https://example.com/",
            "https://example.com/good");
        result.Excluded.Should().Contain(excluded =>
            excluded.Url == "https://example.com/bad" &&
            excluded.Reason == "fetch_http_request_failed");
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
    public void HttpPageFetcher_ProductionHandlerDisablesCookies()
    {
        var factory = typeof(HttpPageFetcher).GetMethod(
            "CreateSafeSocketsHandler",
            BindingFlags.NonPublic | BindingFlags.Static);

        factory.Should().NotBeNull();
        using var handler = (SocketsHttpHandler)factory!.Invoke(
            null,
            new object[] { new FakeHostAddressResolver(IPAddress.Parse("93.184.216.34")) })!;

        handler.UseCookies.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WithSameFetcher_DoesNotReplayResponseCookies()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var observedCookies = new List<string?>();
        var serverTask = RunCookieServerAsync(listener, observedCookies, expectedRequests: 2);
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectCallback = async (_, ct) =>
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(IPAddress.Loopback, port, ct);
                    return client.GetStream();
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            },
        };
        var fetcher = new HttpPageFetcher(
            handler,
            new FakeHostAddressResolver(IPAddress.Parse("93.184.216.34")));

        var first = await fetcher.FetchAsync(new Uri("http://public.example/first"), CancellationToken.None);
        var second = await fetcher.FetchAsync(new Uri("http://public.example/second"), CancellationToken.None);
        await serverTask;

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        observedCookies.Should().Equal(null, null);
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

    [Fact]
    public async Task FetchAsync_WhenTransportRequestFails_ReturnsFailureResult()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("connection reset", new IOException("reset")));
        var fetcher = new HttpPageFetcher(
            handler,
            new FakeHostAddressResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new Uri("https://example.com/docs/"), 1024, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureReason.Should().Be("fetch_http_request_failed");
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

    private static async Task RunCookieServerAsync(
        TcpListener listener,
        ICollection<string?> observedCookies,
        int expectedRequests)
    {
        for (var index = 0; index < expectedRequests; index++)
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var requestHeaders = await ReadHeadersAsync(stream);
            observedCookies.Add(FindHeaderValue(requestHeaders, "Cookie"));

            const string body = "<html><body>ok</body></html>";
            var response = string.Concat(
                "HTTP/1.1 200 OK\r\n",
                "Content-Type: text/html\r\n",
                "Set-Cookie: crawl_secret=one\r\n",
                "Connection: close\r\n",
                "Content-Length: ",
                Encoding.UTF8.GetByteCount(body).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "\r\n\r\n",
                body);
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes);
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        using var requestBytes = new MemoryStream();

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer);
            if (bytesRead == 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, bytesRead);
            var requestText = Encoding.ASCII.GetString(requestBytes.ToArray());
            if (requestText.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return requestText;
            }
        }

        return Encoding.ASCII.GetString(requestBytes.ToArray());
    }

    private static string? FindHeaderValue(string headers, string headerName)
    {
        var prefix = headerName + ":";
        return headers
            .Split("\r\n", StringSplitOptions.None)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
            .Substring(prefix.Length)
            .Trim();
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

    private sealed class ThrowingPageFetcher : IPageFetcher
    {
        private readonly IReadOnlyDictionary<string, PageFetchResult> pages;
        private readonly IReadOnlyDictionary<string, Exception> exceptions;

        public ThrowingPageFetcher(
            IReadOnlyDictionary<string, PageFetchResult> pages,
            IReadOnlyDictionary<string, Exception> exceptions)
        {
            this.pages = pages;
            this.exceptions = exceptions;
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            return FetchAsync(uri, long.MaxValue, ct);
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            if (exceptions.TryGetValue(uri.ToString(), out var exception))
            {
                throw exception;
            }

            return Task.FromResult(pages.TryGetValue(uri.ToString(), out var result)
                ? result
                : PageFetchResult.Fail(uri, 404, "not_found"));
        }
    }

    private sealed class FakeVisualPageRenderer : IVisualPageRenderer
    {
        private readonly IReadOnlyDictionary<string, VisualPageRenderResult> pages;

        public FakeVisualPageRenderer(IReadOnlyDictionary<string, VisualPageRenderResult> pages)
        {
            this.pages = pages;
        }

        public Task<VisualPageRenderResult> CaptureAsync(Uri uri, CancellationToken ct)
        {
            return Task.FromResult(pages.TryGetValue(uri.ToString(), out var result)
                ? result
                : VisualPageRenderResult.Fail(uri, "not_rendered"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
