using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteGeneratePackageHandlerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-handler-package-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenPayloadContainsCrawlResult_GeneratesPackage()
    {
        var handler = new SiteGeneratePackageHandler(
            new SiteGeneratorConverter(DefaultComponentLibrary.Create()),
            new StaticSitePackageGenerator(),
            NullLogger<SiteGeneratePackageHandler>.Instance);
        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                crawl_result = BuildCrawlResult(),
                output_directory = tempRoot,
                package_name = "handler-site"
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await handler.ExecuteAsync("req-1", "site_generate_package", payload, "{}", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        var package = JsonSerializer.Deserialize<StaticSitePackageResult>(
            result.ResultPayload!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        package.Should().NotBeNull();
        File.Exists(package!.EntryPoint).Should().BeTrue();
        File.Exists(package.SiteJsonPath).Should().BeTrue();
        File.Exists(package.ManifestPath).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPayloadHasNoCrawlOrSiteDocument_ReturnsValidationError()
    {
        var handler = new SiteGeneratePackageHandler(
            new SiteGeneratorConverter(DefaultComponentLibrary.Create()),
            new StaticSitePackageGenerator(),
            NullLogger<SiteGeneratePackageHandler>.Instance);

        var result = await handler.ExecuteAsync("req-1", "site_generate_package", "{}", "{}", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("crawl_result or site_document is required.");
    }

    private static SiteCrawlResult BuildCrawlResult()
    {
        return new SiteCrawlResult
        {
            CrawlRunId = "crawl-handler",
            Root = new SiteCrawlRoot
            {
                StartUrl = "https://example.com/",
                NormalizedStartUrl = "https://example.com/",
                Origin = "https://example.com",
            },
            Pages =
            [
                new SiteCrawlPage
                {
                    FinalUrl = "https://example.com/",
                    Depth = 0,
                    StatusCode = 200,
                    Title = "Example",
                    TextExcerpt = "Welcome",
                },
            ],
            ExtractedModel = new ExtractedSiteModel
            {
                Pages =
                [
                    new ExtractedPageModel
                    {
                        PageUrl = "https://example.com/",
                        Sections =
                        [
                            new ExtractedSection
                            {
                                Id = "hero",
                                Role = "hero",
                                Headline = "Example",
                                Body = "Welcome",
                                SourceSelector = "section.hero",
                            },
                        ],
                    },
                ],
            },
        };
    }
}
