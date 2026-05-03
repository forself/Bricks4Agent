using System.Text.Json;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteReconstructPackageHandlerTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-reconstruct-handler-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CrawlsConvertsAndPackagesWithStrictQualityReport()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = PageFetchResult.Ok(
                new Uri("https://example.com/"),
                200,
                "text/html",
                """
                <html>
                <head><title>Example Site</title></head>
                <body>
                  <header><a href="/">Example</a></header>
                  <main>
                    <section class="hero">
                      <h1>Example Site</h1>
                      <p>Reusable reconstruction source.</p>
                    </section>
                  </main>
                </body>
                </html>
                """),
        });
        var handler = BuildHandler(fetcher);
        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                start_url = "https://example.com/",
                scope = new { kind = "link_depth", max_depth = 0, same_origin_only = true, path_prefix_lock = true },
                capture = new { html = true, rendered_dom = false },
                budgets = new { max_pages = 1, max_total_bytes = 1024 * 1024, wall_clock_timeout_seconds = 30 },
                output_directory = tempRoot,
                package_name = "example-rebuild"
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await handler.ExecuteAsync("req-rebuild", "site_reconstruct_package", payload, "{}", CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Error.Should().BeNull();
        var response = JsonSerializer.Deserialize<SiteReconstructPackageResult>(
            result.ResultPayload!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        response.Should().NotBeNull();
        response!.CrawlRunId.Should().Be("req-rebuild");
        response.PageCount.Should().Be(1);
        File.Exists(response.Package.EntryPoint).Should().BeTrue();
        File.Exists(response.Package.SiteJsonPath).Should().BeTrue();
        File.Exists(response.Package.ArchivePath).Should().BeTrue();
        using (var archive = ZipFile.OpenRead(response.Package.ArchivePath))
        {
            archive.Entries.Select(entry => entry.FullName).Should().Contain("index.html");
        }
        response.Package.QualityReport.IsPassed.Should().BeTrue();
        response.Package.QualityReport.ComponentRequestCount.Should().Be(0);
        response.Package.VerificationReport.IsPassed.Should().BeTrue();
        response.Package.VerificationReport.ArchiveEntries.Should().Contain("runtime.js");
        fetcher.RequestedUrls.Should().Equal("https://example.com/");
    }

    [Fact]
    public async Task ExecuteAsync_WhenGeneratedDocumentFailsQualityGate_ReturnsStructuredQualityPayload()
    {
        var fetcher = new FakePageFetcher(new Dictionary<string, PageFetchResult>(StringComparer.Ordinal)
        {
            ["https://example.com/"] = PageFetchResult.Ok(
                new Uri("https://example.com/"),
                200,
                "text/html",
                """
                <html>
                <head><title>Unsupported</title></head>
                <body><main><custom-widget><h1>Custom Widget</h1><p>Unknown role.</p></custom-widget></main></body>
                </html>
                """),
        });
        var componentLibrary = DefaultComponentLibrary.Create();
        componentLibrary.Components.Add(DefaultComponentLibrary.Define(
            "GeneratedDiagnosticOnly",
            "Generated component that should be blocked by the strict gate.",
            ["diagnostic"],
            new ComponentPropsSchema(),
            generated: true));
        var handler = BuildHandler(fetcher, componentLibrary);
        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                start_url = "https://example.com/",
                budgets = new { max_pages = 1 },
                output_directory = tempRoot,
                package_name = "blocked-rebuild"
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await handler.ExecuteAsync("req-rebuild", "site_reconstruct_package", payload, "{}", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("quality gate failed");
        result.ResultPayload.Should().NotBeNull();
        using var failure = JsonDocument.Parse(result.ResultPayload!);
        failure.RootElement.GetProperty("quality_report").GetProperty("is_passed").GetBoolean().Should().BeFalse();
        Directory.Exists(Path.Combine(tempRoot, "blocked-rebuild")).Should().BeFalse();
    }

    private static SiteReconstructPackageHandler BuildHandler(
        IPageFetcher fetcher,
        ComponentLibraryManifest? componentLibrary = null)
    {
        var crawler = new SiteCrawlerService(fetcher, new DeterministicSiteExtractor());
        var manifest = componentLibrary ?? DefaultComponentLibrary.Create();
        return new SiteReconstructPackageHandler(
            crawler,
            new SiteGeneratorConverter(manifest),
            new StaticSitePackageGenerator(),
            NullLogger<SiteReconstructPackageHandler>.Instance);
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
            return FetchAsync(uri, long.MaxValue, ct);
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            RequestedUrls.Add(uri.ToString());
            return Task.FromResult(pages.TryGetValue(uri.ToString(), out var result)
                ? result
                : PageFetchResult.Fail(uri, 404, "not_found"));
        }
    }
}
