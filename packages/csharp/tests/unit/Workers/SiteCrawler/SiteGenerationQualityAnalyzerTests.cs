using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteGenerationQualityAnalyzerTests
{
    [Fact]
    public void Analyze_WhenDocumentIsClean_ReturnsPassingReportWithStats()
    {
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        var analyzer = new SiteGenerationQualityAnalyzer();

        var report = analyzer.Analyze(document);

        report.IsPassed.Should().BeTrue();
        report.Errors.Should().BeEmpty();
        report.RouteCount.Should().Be(1);
        report.ComponentNodeCount.Should().Be(3);
        report.ComponentRequestCount.Should().Be(0);
        report.GeneratedComponentCount.Should().Be(0);
        report.ComponentTypes.Should().Contain(["PageShell", "SiteHeader", "HeroSection"]);
    }

    [Fact]
    public void Analyze_WhenSchemaIsInvalid_ReturnsSchemaErrors()
    {
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        document.Routes[0].Root.Children.Add(new ComponentNode
        {
            Id = "bad",
            Type = "NotInLibrary",
        });
        var analyzer = new SiteGenerationQualityAnalyzer();

        var report = analyzer.Analyze(document);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("schema:", StringComparison.Ordinal));
        report.UnknownComponentTypes.Should().Contain("NotInLibrary");
    }

    [Fact]
    public void Analyze_WhenDocumentNeedsNewComponents_FailsStrictPolicy()
    {
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        document.ComponentRequests.Add(new ComponentRequest
        {
            RequestId = "component-request-1",
            Role = "pricing",
            ComponentType = "MissingPricingPanel",
            Reason = "preferred component missing",
        });
        document.ComponentLibrary.Components.Add(DefaultComponentLibrary.Define(
            "GeneratedFallback",
            "Generated fallback.",
            ["content"],
            new ComponentPropsSchema
            {
                Required = ["title"],
                Properties = { ["title"] = new ComponentPropSchema { Type = "string" } },
            },
            generated: true));
        var analyzer = new SiteGenerationQualityAnalyzer();

        var report = analyzer.Analyze(document);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("component request", StringComparison.OrdinalIgnoreCase));
        report.Errors.Should().Contain(error => error.Contains("generated component", StringComparison.OrdinalIgnoreCase));
        report.ComponentRequestCount.Should().Be(1);
        report.GeneratedComponentCount.Should().Be(1);
    }

    [Fact]
    public void Analyze_WhenRoutePathsRepeat_FailsStrictPolicy()
    {
        var document = ComponentSchemaValidatorTests.BuildValidDocument();
        document.Routes.Add(new GeneratorRoute
        {
            Path = "/",
            Title = "Duplicate",
            SourceUrl = "https://example.com/duplicate",
            Root = new ComponentNode
            {
                Id = "page-duplicate",
                Type = "PageShell",
                Props =
                {
                    ["title"] = "Duplicate",
                    ["source_url"] = "https://example.com/duplicate",
                },
            },
        });
        var analyzer = new SiteGenerationQualityAnalyzer();

        var report = analyzer.Analyze(document);

        report.IsPassed.Should().BeFalse();
        report.Errors.Should().Contain(error => error.Contains("duplicate route path '/'", StringComparison.Ordinal));
    }
}
