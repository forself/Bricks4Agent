using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteCrawlSourceHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenArgsPayloadIsValid_ReturnsSerializedCrawlResult()
    {
        var handler = CreateHandler(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/"),
                200,
                "text/html",
                """
                <html>
                <head><title>Docs</title></head>
                <body><section><h1>Docs</h1><p>Welcome</p></section></body>
                </html>
                """),
        });

        var payload = """
            {
              "args": {
                "start_url": "https://example.com/docs/",
                "scope": { "max_depth": 0 },
                "budgets": { "max_pages": 5 }
              }
            }
            """;

        var result = await handler.ExecuteAsync("crawl-args", "site.crawl_source", payload, "scope", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        var crawl = DeserializeResult(result.ResultPayload);
        crawl.CrawlRunId.Should().Be("crawl-args");
        crawl.Root.StartUrl.Should().Be("https://example.com/docs/");
        crawl.Pages.Should().ContainSingle().Which.Title.Should().Be("Docs");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStartUrlIsMissing_ReturnsValidationError()
    {
        var handler = CreateHandler(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal));

        var result = await handler.ExecuteAsync("crawl-missing", "site.crawl_source", "{}", "scope", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ResultPayload.Should().BeNull();
        result.Error.Should().Be("start_url is required.");
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestIdIsBlankInPayload_PropagatesExecuteRequestId()
    {
        var handler = CreateHandler(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/docs/"] = PageFetchResult.Ok(
                new Uri("https://example.com/docs/"),
                200,
                "text/html",
                "<html><head><title>Root</title></head><body><section><h1>Root</h1></section></body></html>"),
        });

        var payload = """
            {
              "start_url": "https://example.com/docs/",
              "request_id": "",
              "scope": { "max_depth": 0 }
            }
            """;

        var result = await handler.ExecuteAsync("crawl-propagated", "site.crawl_source", payload, "scope", CancellationToken.None);

        result.Success.Should().BeTrue();
        DeserializeResult(result.ResultPayload).CrawlRunId.Should().Be("crawl-propagated");
    }

    [Fact]
    public async Task ExecuteAsync_WhenPayloadIsInvalidJson_ReturnsCleanFailure()
    {
        var handler = CreateHandler(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal));

        var result = await handler.ExecuteAsync("crawl-invalid", "site.crawl_source", "{", "scope", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ResultPayload.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    private static SiteCrawlSourceHandler CreateHandler(IReadOnlyDictionary<string, PageFetchResult> pages)
    {
        var service = new SiteCrawlerService(
            new FakePageFetcher(pages),
            new DeterministicSiteExtractor(),
            NullLogger<SiteCrawlerService>.Instance);

        return new SiteCrawlSourceHandler(service, NullLogger<SiteCrawlSourceHandler>.Instance);
    }

    private static SiteCrawlResult DeserializeResult(string? payload)
    {
        payload.Should().NotBeNullOrWhiteSpace();
        return JsonSerializer.Deserialize<SiteCrawlResult>(
            payload!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed class FakePageFetcher : IPageFetcher
    {
        private readonly IReadOnlyDictionary<string, PageFetchResult> pages;

        public FakePageFetcher(IReadOnlyDictionary<string, PageFetchResult> pages)
        {
            this.pages = pages;
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            return FetchAsync(uri, long.MaxValue, ct);
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            return Task.FromResult(pages.TryGetValue(uri.ToString(), out var result)
                ? result
                : PageFetchResult.Fail(uri, 404, "not_found"));
        }
    }
}
