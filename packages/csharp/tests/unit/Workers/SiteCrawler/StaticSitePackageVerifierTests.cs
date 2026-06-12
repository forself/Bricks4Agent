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
        report.RuntimeRendererTypes.Should().Contain(["PageShell", "HeroSection"]);
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

    [Fact]
    public void Verify_WhenIndexDoesNotLoadRuntime_ReturnsError()
    {
        var package = new StaticSitePackageGenerator().Generate(ComponentSchemaValidatorTests.BuildValidDocument(), new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "broken-index-site",
            EnforceQualityGate = true,
        });
        File.WriteAllText(package.EntryPoint, "<!doctype html><div id=\"app\"></div>");
        var verifier = new StaticSitePackageVerifier();

        var report = verifier.Verify(package);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("index.html", StringComparison.Ordinal) &&
            error.Contains("runtime.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_WhenRuntimeDoesNotLoadSiteJsonAndManifest_ReturnsError()
    {
        var package = new StaticSitePackageGenerator().Generate(ComponentSchemaValidatorTests.BuildValidDocument(), new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "broken-runtime-site",
            EnforceQualityGate = true,
        });
        File.WriteAllText(Path.Combine(package.OutputDirectory, "runtime.js"), "console.log('broken runtime');");
        var verifier = new StaticSitePackageVerifier();

        var report = verifier.Verify(package);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("runtime.js", StringComparison.Ordinal) &&
            error.Contains("site.json", StringComparison.Ordinal));
        report.Errors.Should().Contain(error => error.Contains("runtime.js", StringComparison.Ordinal) &&
            error.Contains("components/manifest.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_WhenManifestFileDoesNotDeclareUsedComponent_ReturnsError()
    {
        var package = new StaticSitePackageGenerator().Generate(ComponentSchemaValidatorTests.BuildValidDocument(), new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "broken-manifest-site",
            EnforceQualityGate = true,
        });
        File.WriteAllText(package.ManifestPath, """
            {
              "library_id": "broken",
              "version": "1.0.0",
              "components": []
            }
            """);
        var verifier = new StaticSitePackageVerifier();

        var report = verifier.Verify(package);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("components/manifest.json", StringComparison.Ordinal) &&
            error.Contains("PageShell", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_WhenUsedLibraryComponentHasNoRuntimeRenderer_ReturnsError()
    {
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        document.ComponentLibrary.Components.Add(DefaultComponentLibrary.Define(
            "RuntimeMissingPanel",
            "Schema-valid component without a runtime renderer.",
            ["content"],
            new ComponentPropsSchema
            {
                Required = ["title"],
                Properties =
                {
                    ["title"] = new ComponentPropSchema { Type = "string" },
                },
            }));
        document.Routes[0].Root.Children.Add(new ComponentNode
        {
            Id = "runtime-missing",
            Type = "RuntimeMissingPanel",
            Props =
            {
                ["title"] = "Runtime missing",
            },
        });
        var package = new StaticSitePackageGenerator().Generate(document, new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "missing-runtime-renderer-site",
            EnforceQualityGate = true,
        });
        var verifier = new StaticSitePackageVerifier();

        var report = verifier.Verify(package);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("runtime.js", StringComparison.Ordinal) &&
            error.Contains("RuntimeMissingPanel", StringComparison.Ordinal));
    }
}
