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
        runtime.Should().Contain("navigateToRoute");
        runtime.Should().Contain("data-local-route");
        runtime.Should().Contain("renderAtomicSection");
        runtime.Should().Contain("renderFeatureCard");
        runtime.Should().Contain("renderLinkSet");
        runtime.Should().Contain("card-grid--carousel");
        runtime.Should().Contain("normalizeInternalRoute");
        runtime.Should().Contain("toStaticHref");

        var siteJson = JsonSerializer.Deserialize<GeneratorSiteDocument>(
            File.ReadAllText(Path.Combine(result.OutputDirectory, "site.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        siteJson.Should().NotBeNull();
        siteJson!.Routes.Should().ContainSingle();
        result.EntryPoint.Should().Be(Path.Combine(result.OutputDirectory, "index.html"));
    }

    [Fact]
    public void Generate_WhenDocumentHasGeneratedComponent_WritesLoadableComponentModule()
    {
        var document = new GeneratorSiteDocument
        {
            SchemaVersion = "site-generator/v1",
            Site = new GeneratorSiteMetadata { Title = "Example", SourceUrl = "https://example.com/" },
            ComponentLibrary = DefaultComponentLibrary.Create(),
        };
        document.ComponentLibrary.Components.Add(DefaultComponentLibrary.Define(
            "GeneratedNewsSection",
            "Generated news component.",
            ["news"],
            new ComponentPropsSchema
            {
                Required = ["title", "body"],
                Properties =
                {
                    ["title"] = new ComponentPropSchema { Type = "string" },
                    ["body"] = new ComponentPropSchema { Type = "string" },
                    ["source_selector"] = new ComponentPropSchema { Type = "string" },
                },
            },
            generated: true));
        document.Routes.Add(new GeneratorRoute
        {
            Path = "/",
            Title = "Example",
            SourceUrl = "https://example.com/",
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
                        Id = "news",
                        Type = "GeneratedNewsSection",
                        Props =
                        {
                            ["title"] = "Campus News",
                            ["body"] = "Latest updates.",
                            ["source_selector"] = "section.news-list",
                        },
                    },
                ],
            },
        });
        var generator = new StaticSitePackageGenerator();

        var result = generator.Generate(document, new StaticSitePackageOptions
        {
            OutputDirectory = tempRoot,
            PackageName = "custom-component-site",
        });

        var modulePath = Path.Combine(result.OutputDirectory, "components", "generated", "GeneratedNewsSection.js");
        File.Exists(modulePath).Should().BeTrue();
        File.ReadAllText(modulePath).Should().Contain("export function render");
        File.ReadAllText(Path.Combine(result.OutputDirectory, "runtime.js")).Should().Contain("loadGeneratedRenderers");
        result.Files.Should().Contain(modulePath);
    }

    [Fact]
    public void Generate_WritesCompactStandardImageStylesAndHeroOnlyLargeImageStyles()
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
                    SourceUrl = "https://example.com/",
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
                                Id = "notice",
                                Type = "AtomicSection",
                                Props =
                                {
                                    ["variant"] = "standard",
                                    ["source_selector"] = "div.reminder",
                                },
                                Children =
                                [
                                    new ComponentNode
                                    {
                                        Id = "notice-image",
                                        Type = "ImageBlock",
                                        Props =
                                        {
                                            ["url"] = "https://example.com/reminder.png",
                                            ["alt"] = "Reminder",
                                        },
                                    },
                                ],
                            },
                            new ComponentNode
                            {
                                Id = "hero",
                                Type = "AtomicSection",
                                Props =
                                {
                                    ["variant"] = "hero",
                                    ["source_selector"] = "section.hero",
                                },
                                Children =
                                [
                                    new ComponentNode
                                    {
                                        Id = "hero-image",
                                        Type = "ImageBlock",
                                        Props =
                                        {
                                            ["url"] = "https://example.com/hero.jpg",
                                            ["alt"] = "Campus",
                                        },
                                    },
                                ],
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
            PackageName = "image-style-site",
        });

        var styles = File.ReadAllText(Path.Combine(result.OutputDirectory, "styles.css"));
        styles.Should().Contain(".atomic-section--standard .image-block");
        styles.Should().Contain("width: min(260px, 100%)");
        styles.Should().Contain("max-height: 160px");
        styles.Should().Contain(".atomic-section--hero .image-block");
        styles.Should().Contain("min-height: 260px");
        styles.Should().Contain("object-fit: cover");
    }
}
