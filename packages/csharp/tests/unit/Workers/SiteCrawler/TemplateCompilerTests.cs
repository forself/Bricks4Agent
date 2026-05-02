using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class TemplateCompilerTests
{
    [Fact]
    public void Compile_UsesHighLevelTemplateComponentsAndLocalRoutes()
    {
        var crawl = BuildVisualCrawl();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentExtractor().Extract(crawl);
        var plan = new TemplateMatcher(new TemplateFrameworkLoader().LoadDefault(), manifest).Match(intent);
        var compiler = new TemplateCompiler(manifest);

        var document = compiler.Compile(crawl, intent, plan);

        document.Routes.Should().HaveCount(crawl.Pages.Count);
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        document.ComponentRequests.Should().BeEmpty();

        var knownTypes = document.ComponentLibrary.Components.Select(component => component.Type).ToHashSet(StringComparer.Ordinal);
        Flatten(document.Routes[0].Root).Should().OnlyContain(node => knownTypes.Contains(node.Type));
        Flatten(document.Routes[0].Root).Select(node => node.Type).Should().Contain([
            "MegaHeader",
            "HeroCarousel",
            "QuickLinkRibbon",
            "NewsCardCarousel",
            "InstitutionFooter",
        ]);
        document.Routes.Select(route => route.Path).Should().Contain(["/", "/admission", "/Spotlight/from-06-sID-32588"]);

        var linkValues = Flatten(document.Routes[0].Root)
            .SelectMany(node => node.Props.Values)
            .SelectMany(ExtractLinkDictionaries)
            .Where(link => link.TryGetValue("scope", out var scope) && scope == "internal")
            .Select(link => link["url"])
            .ToList();
        linkValues.Should().Contain(["/admission", "/Spotlight/from-06-sID-32588"]);
        linkValues.Should().OnlyContain(url =>
            url.StartsWith("/", StringComparison.Ordinal) &&
            !url.Contains(".aspx", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains(".html", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains(".htm", StringComparison.OrdinalIgnoreCase));

        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_WhenPreferredComponentFallsBack_RecordsRequestWithoutGeneratedComponents()
    {
        var crawl = BuildVisualCrawl();
        var manifest = DefaultComponentLibrary.Create();
        manifest.Components.RemoveAll(component => component.Type == "HeroCarousel");
        var intent = new SiteIntentExtractor().Extract(crawl);
        var plan = new TemplateMatcher(new TemplateFrameworkLoader().LoadDefault(), manifest).Match(intent);
        var compiler = new TemplateCompiler(manifest);

        var document = compiler.Compile(crawl, intent, plan);

        Flatten(document.Routes[0].Root).Should().Contain(node => node.Type == "HeroBanner");
        document.ComponentRequests.Should().Contain(request =>
            request.ComponentType == "HeroCarousel" &&
            request.Reason.Contains("preferred_component_missing", StringComparison.Ordinal));
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
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

    private static IEnumerable<Dictionary<string, string>> ExtractLinkDictionaries(object? value)
    {
        if (value is List<Dictionary<string, string>> links)
        {
            return links;
        }

        if (value is List<Dictionary<string, object?>> objectLinks)
        {
            return objectLinks.Select(item => item.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal));
        }

        return [];
    }

    private static SiteCrawlResult BuildVisualCrawl()
    {
        return new SiteCrawlResult
        {
            CrawlRunId = "crawl-1",
            Root = new SiteCrawlRoot
            {
                StartUrl = "https://example.edu/",
                NormalizedStartUrl = "https://example.edu/",
                Origin = "https://example.edu",
                PathPrefix = "/",
            },
            Pages =
            [
                new SiteCrawlPage
                {
                    FinalUrl = "https://example.edu/",
                    Depth = 0,
                    StatusCode = 200,
                    Title = "Example University",
                    TextExcerpt = "Example University.",
                    VisualSnapshot = new VisualPageSnapshot
                    {
                        Regions =
                        [
                            new VisualRegion
                            {
                                Id = "header",
                                Role = "header",
                                Selector = "header",
                                Media = [new ExtractedMedia { Url = "https://example.edu/logo.png", Alt = "University logo" }],
                                Actions =
                                [
                                    new ExtractedAction { Label = "Admissions", Url = "https://example.edu/admission.aspx" },
                                ],
                            },
                            new VisualRegion
                            {
                                Id = "hero",
                                Role = "carousel",
                                Selector = ".hero",
                                Headline = "Campus Life",
                                Text = "Campus Life\nStudy with us.",
                                Bounds = new VisualBox { Y = 120, Width = 1200, Height = 420 },
                                Media =
                                [
                                    new ExtractedMedia { Url = "https://example.edu/hero-1.jpg", Alt = "Hero 1" },
                                    new ExtractedMedia { Url = "https://example.edu/hero-2.jpg", Alt = "Hero 2" },
                                ],
                            },
                            new VisualRegion
                            {
                                Id = "quick",
                                Role = "card_grid",
                                Selector = ".quick",
                                Headline = "Quick Links",
                                Actions =
                                [
                                    new ExtractedAction { Label = "Admissions", Url = "https://example.edu/admission.aspx" },
                                    new ExtractedAction { Label = "Calendar", Url = "https://example.edu/calendar.aspx" },
                                    new ExtractedAction { Label = "Library", Url = "https://example.edu/library.aspx" },
                                ],
                            },
                            new VisualRegion
                            {
                                Id = "news",
                                Role = "news",
                                Selector = ".news",
                                Headline = "Latest News",
                                Text = "2026-05-01 Campus",
                                Items =
                                [
                                    new ExtractedItem
                                    {
                                        Title = "Campus",
                                        Body = "2026-05-01",
                                        Url = "https://example.edu/Spotlight.aspx?from=06&sID=32588",
                                        MediaUrl = "https://example.edu/news.jpg",
                                        MediaAlt = "News",
                                    },
                                    new ExtractedItem
                                    {
                                        Title = "Research",
                                        Body = "2026-04-20",
                                        Url = "https://example.edu/news.aspx?id=2",
                                        MediaUrl = "https://example.edu/research.jpg",
                                        MediaAlt = "Research",
                                    },
                                ],
                            },
                            new VisualRegion
                            {
                                Id = "footer",
                                Role = "footer",
                                Selector = "footer",
                                Text = "No. 1 University Road",
                            },
                        ],
                    },
                },
                new SiteCrawlPage
                {
                    FinalUrl = "https://example.edu/admission.aspx",
                    Depth = 1,
                    StatusCode = 200,
                    Title = "Admissions",
                    TextExcerpt = "Admissions.",
                },
                new SiteCrawlPage
                {
                    FinalUrl = "https://example.edu/Spotlight.aspx?from=06&sID=32588",
                    Depth = 1,
                    StatusCode = 200,
                    Title = "Spotlight",
                    TextExcerpt = "Spotlight.",
                },
            ],
            ExtractedModel = new ExtractedSiteModel
            {
                ThemeTokens = new ExtractedThemeTokens
                {
                    Colors = { ["brand"] = "#004a98" },
                    Typography = { ["font_family"] = "Inter, sans-serif" },
                },
            },
        };
    }
}
