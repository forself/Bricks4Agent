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
    public void Convert_WhenSectionRoleHasNoBuiltInComponent_UsesAtomicCompositionBeforeGeneratingComponents()
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

        document.ComponentRequests.Should().BeEmpty();
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        Flatten(document.Routes[0].Root).Should()
            .ContainSingle(node => node.Type == "AtomicSection" && (string)node.Props["variant"]! == "standard");
        Flatten(document.Routes[0].Root).Should()
            .Contain(node => node.Type == "TextBlock");
    }

    [Fact]
    public void Convert_DoesNotRenderRawCrawlLinksAsVisualNavigation()
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
        links.Should().BeEmpty();

        document.Routes[0].Root.Children.Should().NotContain(node => node.Type == "LinkList");
        Flatten(document.Routes[0].Root)
            .Any(node => node.Props.TryGetValue("links", out var value) &&
                value is List<Dictionary<string, string>> linksValue &&
                linksValue.Count > 0)
            .Should().BeFalse();
    }

    [Fact]
    public void Convert_ComposesVisualSectionsFromReusableAtomicComponents()
    {
        var crawl = BuildCrawlResult();
        var page = crawl.ExtractedModel.Pages[0];
        page.Sections.Clear();
        page.Sections.Add(new ExtractedSection
        {
            Id = "hero",
            Role = "hero",
            Headline = "Study at SHU",
            Body = "Media and communication programs in Taipei.",
            SourceSelector = "section.hero",
            Media =
            [
                new ExtractedMedia { Url = "https://example.com/assets/hero.jpg", Alt = "Campus gate", Kind = "image" },
            ],
            Actions =
            [
                new ExtractedAction { Label = "Apply now", Url = "https://example.com/apply", Kind = "primary" },
            ],
        });
        page.Sections.Add(new ExtractedSection
        {
            Id = "programs",
            Role = "program_grid",
            Headline = "Programs",
            Body = "Choose a path.",
            SourceSelector = "section.programs",
            Items =
            [
                new ExtractedItem
                {
                    Title = "Journalism",
                    Body = "Reporting and multimedia storytelling.",
                    MediaUrl = "https://example.com/assets/journalism.jpg",
                    Url = "https://example.com/programs/journalism",
                },
            ],
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        document.ComponentRequests.Should().BeEmpty();
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);

        var nodes = Flatten(document.Routes[0].Root).ToList();
        nodes.Should().Contain(node => node.Type == "AtomicSection" && (string)node.Props["variant"]! == "hero");
        nodes.Should().Contain(node => node.Type == "ImageBlock");
        nodes.Should().Contain(node => node.Type == "TextBlock");
        nodes.Should().Contain(node => node.Type == "ButtonLink");
        nodes.Should().Contain(node => node.Type == "CardGrid");
        nodes.Should().Contain(node => node.Type == "FeatureCard");
        nodes.Should().NotContain(node => node.Type.StartsWith("Generated", StringComparison.Ordinal));
    }

    [Fact]
    public void Convert_RewritesAtomicActionAndCardLinksToGeneratedRoutes()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/apply",
            Depth = 1,
            StatusCode = 200,
            Title = "Apply",
            TextExcerpt = "Apply.",
            Links = [],
        });
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/programs/journalism",
            Depth = 1,
            StatusCode = 200,
            Title = "Journalism",
            TextExcerpt = "Journalism.",
            Links = [],
        });
        var page = crawl.ExtractedModel.Pages[0];
        page.Sections.Clear();
        page.Sections.Add(new ExtractedSection
        {
            Id = "hero",
            Role = "hero",
            Headline = "Study at SHU",
            Body = "Media and communication programs in Taipei.",
            SourceSelector = "section.hero",
            Actions =
            [
                new ExtractedAction { Label = "Apply now", Url = "https://example.com/apply", Kind = "primary" },
            ],
        });
        page.Sections.Add(new ExtractedSection
        {
            Id = "programs",
            Role = "program_grid",
            Headline = "Programs",
            Body = "Choose a path.",
            SourceSelector = "section.programs",
            Items =
            [
                new ExtractedItem
                {
                    Title = "Journalism",
                    Body = "Reporting and multimedia storytelling.",
                    Url = "https://example.com/programs/journalism",
                },
            ],
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var button = Flatten(document.Routes[0].Root).Single(node => node.Type == "ButtonLink");
        button.Props["url"].Should().Be("/apply");

        var card = Flatten(document.Routes[0].Root).Single(node => node.Type == "FeatureCard");
        card.Props["url"].Should().Be("/programs/journalism");
    }

    [Fact]
    public void Convert_PreservesVisualHeaderFooterAndUsesStaticRoutes()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/admission.aspx",
            Depth = 1,
            StatusCode = 200,
            Title = "Admissions",
            TextExcerpt = "Admissions.",
            Links = [],
        });
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/Spotlight.aspx?from=06&sID=32588",
            Depth = 1,
            StatusCode = 200,
            Title = "Spotlight",
            TextExcerpt = "Spotlight.",
            Links = [],
        });
        var page = crawl.ExtractedModel.Pages[0];
        page.Header = new ExtractedHeader
        {
            LogoUrl = "https://example.com/logo.png",
            LogoAlt = "University logo",
            UtilityLinks =
            [
                new ExtractedAction { Label = "Apply", Url = "https://example.com/apply.aspx" },
            ],
            PrimaryLinks =
            [
                new ExtractedAction { Label = "Admissions", Url = "https://example.com/admission.aspx" },
            ],
        };
        page.Footer = new ExtractedFooter
        {
            LogoUrl = "https://example.com/footer.png",
            LogoAlt = "Footer logo",
            Text = "No. 1, University Road",
            Links =
            [
                new ExtractedAction { Label = "Privacy", Url = "https://example.com/privacy.aspx" },
            ],
        };
        page.Sections.Clear();
        page.Sections.Add(new ExtractedSection
        {
            Id = "news",
            Role = "news",
            Headline = "News",
            Body = "Latest stories.",
            SourceSelector = "div.carousel",
            Items =
            [
                new ExtractedItem
                {
                    Title = "Story",
                    Body = "Summary.",
                    Url = "https://example.com/Spotlight.aspx?from=06&sID=32588",
                },
            ],
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        document.Routes.Select(route => route.Path).Should().Contain(["/", "/admission", "/Spotlight/from-06-sID-32588"]);

        var header = document.Routes[0].Root.Children.Single(node => node.Type == "SiteHeader");
        header.Props["logo_url"].Should().Be("https://example.com/logo.png");
        var primaryLinks = header.Props["primary_links"].Should().BeAssignableTo<List<Dictionary<string, string>>>().Subject;
        primaryLinks.Should().Contain(link => link["label"] == "Admissions" && link["url"] == "/admission");

        var footer = document.Routes[0].Root.Children.Single(node => node.Type == "SiteFooter");
        footer.Props["logo_url"].Should().Be("https://example.com/footer.png");
        footer.Props["contact_text"].Should().Be("No. 1, University Road");
        var footerLinks = footer.Props["links"].Should().BeAssignableTo<List<Dictionary<string, string>>>().Subject;
        footerLinks.Should().Contain(link => link["label"] == "Privacy" && link["url"] == "/privacy");

        var grid = Flatten(document.Routes[0].Root).Single(node => node.Type == "CardGrid");
        grid.Props["layout"].Should().Be("carousel");
        var card = Flatten(document.Routes[0].Root).Single(node => node.Type == "FeatureCard");
        card.Props["url"].Should().Be("/Spotlight/from-06-sID-32588");
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
