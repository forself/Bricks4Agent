using System.Text.Json;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class StaticSitePackageGeneratorTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-package-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Generate_WritesRuntimePackageThatLoadsSiteJson()
    {
        var document = new GeneratorSiteDocument
        {
            SchemaVersion = "site-generator/v1",
            Site = new GeneratorSiteMetadata { Title = "Example", SourceUrl = "https://example.com/" },
            ComponentLibrary = DefaultComponentLibrary.Create(),
            Routes =
            [
                new GeneratorRoute
                {
                    Path = "/",
                    Title = "Example",
                    Root = new ComponentNode
                    {
                        Id = "page",
                        Type = "PageShell",
                        Props =
                        {
                            ["title"] = "Example",
                            ["source_url"] = "https://example.com/",
                        },
                        Children =
                        [
                            new ComponentNode
                            {
                                Id = "hero",
                                Type = "HeroSection",
                                Props =
                                {
                                    ["title"] = "Example",
                                    ["body"] = "Welcome.",
                                },
                            },
                        ],
                    },
                },
            ],
        };
        var generator = new StaticSitePackageGenerator();

        var result = generator.Generate(document, new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "example-site",
        });

        File.Exists(Path.Combine(result.OutputDirectory, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(result.OutputDirectory, "runtime.js")).Should().BeTrue();
        File.Exists(Path.Combine(result.OutputDirectory, "styles.css")).Should().BeTrue();
        File.Exists(Path.Combine(result.OutputDirectory, "site.json")).Should().BeTrue();
        File.Exists(Path.Combine(result.OutputDirectory, "components", "manifest.json")).Should().BeTrue();

        var index = File.ReadAllText(Path.Combine(result.OutputDirectory, "index.html"));
        index.Should().Contain("<div id=\"app\"></div>");
        index.Should().Contain("./runtime.js");
        index.Should().NotContain("Welcome.");

        var runtime = File.ReadAllText(Path.Combine(result.OutputDirectory, "runtime.js"));
        runtime.Should().Contain("fetch('./site.json')");
        runtime.Should().Contain("componentRenderers");

        var siteJson = JsonSerializer.Deserialize<GeneratorSiteDocument>(
            File.ReadAllText(Path.Combine(result.OutputDirectory, "site.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        siteJson.Should().NotBeNull();
        siteJson!.Routes.Should().ContainSingle();
        result.EntryPoint.Should().Be(Path.Combine(result.OutputDirectory, "index.html"));
    }
}
