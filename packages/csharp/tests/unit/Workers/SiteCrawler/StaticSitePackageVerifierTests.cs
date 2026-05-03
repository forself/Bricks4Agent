using System.IO.Compression;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class StaticSitePackageVerifierTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-package-verify-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Verify_WhenPackageIsCompleteWithArchive_ReturnsPassingReport()
    {
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        var package = new StaticSitePackageGenerator().Generate(document, new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "verified-site",
            EnforceQualityGate = true,
            CreateArchive = true,
        });
        var verifier = new StaticSitePackageVerifier();

        var report = verifier.Verify(package);

        report.IsPassed.Should().BeTrue();
        report.Errors.Should().BeEmpty();
        report.HasArchive.Should().BeTrue();
        report.RouteCount.Should().Be(document.Routes.Count);
        report.ComponentNodeCount.Should().BeGreaterThan(0);
        report.RequiredFiles.Should().Contain([
            "index.html",
            "runtime.js",
            "styles.css",
            "site.json",
            "components/manifest.json",
        ]);
        report.ArchiveEntries.Should().Contain([
            "index.html",
            "runtime.js",
            "styles.css",
            "site.json",
            "components/manifest.json",
        ]);
        package.VerificationReport.IsPassed.Should().BeTrue();
    }

    [Fact]
    public void Verify_WhenArchiveMissesRuntime_ReturnsError()
    {
        var package = new StaticSitePackageGenerator().Generate(ComponentSchemaValidatorTests.BuildValidDocument(), new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "broken-archive-site",
            EnforceQualityGate = true,
            CreateArchive = true,
        });
        var brokenArchive = Path.Combine(tempRoot, "broken.zip");
        using (var archive = ZipFile.Open(brokenArchive, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(package.EntryPoint, "index.html");
            archive.CreateEntryFromFile(package.SiteJsonPath, "site.json");
            archive.CreateEntryFromFile(package.ManifestPath, "components/manifest.json");
        }

        package.ArchivePath = brokenArchive;
        var verifier = new StaticSitePackageVerifier();

        var report = verifier.Verify(package);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("runtime.js", StringComparison.Ordinal));
    }
}
