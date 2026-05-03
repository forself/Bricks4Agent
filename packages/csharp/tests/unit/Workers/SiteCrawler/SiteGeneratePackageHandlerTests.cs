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

        result.Error.Should().BeNull();
        result.Error.Should().BeNull();
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        var package = JsonSerializer.Deserialize<StaticSitePackageResult>(
            result.ResultPayload!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        package.Should().NotBeNull();
        File.Exists(package!.EntryPoint).Should().BeTrue();
        File.Exists(package.SiteJsonPath).Should().BeTrue();
        File.Exists(package.ManifestPath).Should().BeTrue();
        package.QualityReport.IsPassed.Should().BeTrue();
        package.QualityReport.ComponentNodeCount.Should().BeGreaterThan(0);
        package.QualityReport.ComponentRequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenQualityGateFailsByDefault_DoesNotWritePackage()
    {
        var handler = new SiteGeneratePackageHandler(
            new SiteGeneratorConverter(DefaultComponentLibrary.Create()),
            new StaticSitePackageGenerator(),
            NullLogger<SiteGeneratePackageHandler>.Instance);
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        document.ComponentRequests.Add(new ComponentRequest
        {
            RequestId = "missing-hero",
            Role = "hero",
            ComponentType = "MissingHero",
            Reason = "No reusable component supports this visual pattern.",
        });
        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                site_document = document,
                output_directory = tempRoot,
                package_name = "blocked-site"
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await handler.ExecuteAsync("req-1", "site_generate_package", payload, "{}", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("quality gate failed");
        result.Error.Should().Contain("component request");
        result.ResultPayload.Should().NotBeNull();
        using var failure = JsonDocument.Parse(result.ResultPayload!);
        failure.RootElement.GetProperty("quality_report").GetProperty("is_passed").GetBoolean().Should().BeFalse();
        failure.RootElement.GetProperty("quality_report").GetProperty("component_request_count").GetInt32().Should().Be(1);
        Directory.Exists(Path.Combine(tempRoot, "blocked-site")).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenQualityGateIsDisabled_ReturnsDiagnosticReportWithPackage()
    {
        var handler = new SiteGeneratePackageHandler(
            new SiteGeneratorConverter(DefaultComponentLibrary.Create()),
            new StaticSitePackageGenerator(),
            NullLogger<SiteGeneratePackageHandler>.Instance);
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        document.ComponentRequests.Add(new ComponentRequest
        {
            RequestId = "missing-hero",
            Role = "hero",
            ComponentType = "MissingHero",
            Reason = "No reusable component supports this visual pattern.",
        });
        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                site_document = document,
                output_directory = tempRoot,
                package_name = "diagnostic-site",
                enforce_quality_gate = false
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await handler.ExecuteAsync("req-1", "site_generate_package", payload, "{}", CancellationToken.None);

        result.Error.Should().BeNull();
        result.Success.Should().BeTrue();
        var package = JsonSerializer.Deserialize<StaticSitePackageResult>(
            result.ResultPayload!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        package.Should().NotBeNull();
        File.Exists(package!.EntryPoint).Should().BeTrue();
        package.QualityReport.IsPassed.Should().BeFalse();
        package.QualityReport.ComponentRequestCount.Should().Be(1);
        package.QualityReport.Errors.Should().Contain(error => error.Contains("component request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCreateArchiveIsRequested_ReturnsArchivePath()
    {
        var handler = new SiteGeneratePackageHandler(
            new SiteGeneratorConverter(DefaultComponentLibrary.Create()),
            new StaticSitePackageGenerator(),
            NullLogger<SiteGeneratePackageHandler>.Instance);
        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                site_document = ComponentSchemaValidatorTests.BuildValidDocument(),
                output_directory = tempRoot,
                package_name = "handler-archive-site",
                create_archive = true
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await handler.ExecuteAsync("req-1", "site_generate_package", payload, "{}", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        var package = JsonSerializer.Deserialize<StaticSitePackageResult>(
            result.ResultPayload!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        package.Should().NotBeNull();
        package!.ArchivePath.Should().Be(Path.Combine(tempRoot, "handler-archive-site.zip"));
        File.Exists(package.ArchivePath).Should().BeTrue();
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
