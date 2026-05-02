using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class ComponentSchemaValidatorTests
{
    [Fact]
    public void Validate_WhenDocumentUsesKnownComponentsAndValidProps_ReturnsSuccess()
    {
        var document = BuildValidDocument();
        var validator = new ComponentSchemaValidator();

        var result = validator.Validate(document);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenComponentTypeIsUnknown_ReturnsError()
    {
        var document = BuildValidDocument();
        document.Routes[0].Root.Children.Add(new ComponentNode
        {
            Id = "unknown",
            Type = "NotInManifest",
        });
        var validator = new ComponentSchemaValidator();

        var result = validator.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("unknown component type 'NotInManifest'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenRequiredPropIsMissing_ReturnsError()
    {
        var document = BuildValidDocument();
        document.Routes[0].Root.Children[0].Props.Remove("title");
        var validator = new ComponentSchemaValidator();

        var result = validator.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("missing required prop 'title'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenPropTypeIsWrong_ReturnsError()
    {
        var document = BuildValidDocument();
        document.Routes[0].Root.Children[0].Props["links"] = "not links";
        var validator = new ComponentSchemaValidator();

        var result = validator.Validate(document);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("prop 'links' must be array", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_WhenDocumentIsInvalid_DoesNotWritePackage()
    {
        var document = BuildValidDocument();
        document.Routes[0].Root.Type = "MissingShell";
        var outputRoot = Path.Combine(Path.GetTempPath(), $"b4a-invalid-package-{Guid.NewGuid():N}");
        var generator = new StaticSitePackageGenerator();

        var act = () => generator.Generate(document, new StaticSitePackageOptions
        {
            OutputDirectory = outputRoot,
            PackageName = "bad",
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown component type 'MissingShell'*");
        Directory.Exists(Path.Combine(outputRoot, "bad")).Should().BeFalse();
    }

    internal static GeneratorSiteDocument BuildValidDocument()
    {
        return new GeneratorSiteDocument
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
                                Id = "header",
                                Type = "SiteHeader",
                                Props =
                                {
                                    ["title"] = "Example",
                                    ["links"] = new List<Dictionary<string, string>>
                                    {
                                        new()
                                        {
                                            ["label"] = "About",
                                            ["url"] = "https://example.com/about",
                                        },
                                    },
                                },
                            },
                            new ComponentNode
                            {
                                Id = "hero",
                                Type = "HeroSection",
                                Props =
                                {
                                    ["title"] = "Example",
                                    ["body"] = "Welcome.",
                                    ["source_selector"] = "section.hero",
                                },
                            },
                        ],
                    },
                },
            ],
        };
    }
}
