using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteGeneratorConverterTests
{
    [Fact]
    public void Convert_BuildsLegalComponentDocumentFromCrawlResult()
    {
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(BuildCrawlResult());

        document.SchemaVersion.Should().Be("site-generator/v1");
        document.Routes.Should().ContainSingle();
        document.Routes[0].Root.Type.Should().Be("PageShell");

        var knownTypes = document.ComponentLibrary.Components
            .Select(component => component.Type)
            .ToHashSet(StringComparer.Ordinal);
        Flatten(document.Routes[0].Root).Should().OnlyContain(node => knownTypes.Contains(node.Type));
        Flatten(document.Routes[0].Root).Should().Contain(node => node.Type == "HeroSection");
        Flatten(document.Routes[0].Root).Should().Contain(node => node.Type == "ContentSection");
        Flatten(document.Routes[0].Root).Should().Contain(node => node.Type == "FormBlock");
        Flatten(document.Routes[0].Root).Should().Contain(node => node.Type == "SiteFooter");
    }

    [Fact]
    public void Convert_WhenSectionRoleHasNoBuiltInComponent_GeneratesLocalComponentDefinition()
    {
        var crawl = BuildCrawlResult();
        crawl.ExtractedModel.Pages[0].Sections.Add(new ExtractedSection
        {
            Id = "gallery-1",
            Role = "gallery",
            Headline = "Campus Gallery",
            Body = "Visual campus highlights.",
            SourceSelector = "section.gallery",
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        document.ComponentRequests.Should().ContainSingle(request => request.Role == "gallery");
        document.ComponentLibrary.Components.Should()
            .ContainSingle(component => component.Type == "GeneratedGallerySection" && component.Generated);
        Flatten(document.Routes[0].Root).Should()
            .ContainSingle(node => node.Type == "GeneratedGallerySection");
    }

    [Fact]
    public void Convert_RewritesCrawledSameOriginLinksToGeneratedRoutes()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/about",
            Depth = 1,
            StatusCode = 200,
            Title = "About",
            TextExcerpt = "About page.",
            Links = [],
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var header = document.Routes[0].Root.Children.Single(node => node.Type == "SiteHeader");
        var links = header.Props["links"].Should().BeAssignableTo<List<Dictionary<string, string>>>().Subject;
        links.Should().Contain(link =>
            link["label"] == "about" &&
            link["url"] == "/about" &&
            link["source_url"] == "https://example.com/about" &&
            link["scope"] == "internal");
        links.Should().Contain(link =>
            link["label"] == "contact" &&
            link["url"] == "https://example.com/contact" &&
            link["scope"] == "external");
    }

    private static IEnumerable<ComponentNode> Flatten(ComponentNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    private static SiteCrawlResult BuildCrawlResult()
    {
        return new SiteCrawlResult
        {
            CrawlRunId = "crawl-1",
            Root = new SiteCrawlRoot
            {
                StartUrl = "https://example.com/",
                NormalizedStartUrl = "https://example.com/",
                Origin = "https://example.com",
                PathPrefix = "/",
            },
            Pages =
            [
                new SiteCrawlPage
                {
                    FinalUrl = "https://example.com/",
                    Depth = 0,
                    StatusCode = 200,
                    Title = "Example University",
                    TextExcerpt = "Welcome to Example University.",
                    Links = ["https://example.com/about", "https://example.com/contact"],
                    Forms =
                    [
                        new ExtractedForm
                        {
                            Selector = "form",
                            Method = "post",
                            Action = "/contact",
                            Fields =
                            [
                                new ExtractedFormField
                                {
                                    Name = "email",
                                    Type = "email",
                                    Label = "Email",
                                    Required = true,
                                },
                            ],
                        },
                    ],
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
                                Headline = "Example University",
                                Body = "Welcome to Example University.",
                                SourceSelector = "section.hero",
                            },
                            new ExtractedSection
                            {
                                Id = "intro",
                                Role = "content",
                                Headline = "About",
                                Body = "A school focused on communication and design.",
                                SourceSelector = "section",
                            },
                        ],
                    },
                ],
                ThemeTokens = new ExtractedThemeTokens
                {
                    Colors = { ["brand"] = "#3366ff" },
                    Typography = { ["font_family"] = "Inter, sans-serif" },
                },
                RouteGraph = new ExtractedRouteGraph
                {
                    Routes =
                    [
                        new ExtractedRoute { Path = "/", PageId = "page-1", Depth = 0, Title = "Example University" },
                    ],
                },
            },
        };
    }
}
