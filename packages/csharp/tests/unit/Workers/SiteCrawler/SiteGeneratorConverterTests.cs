using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteGeneratorConverterTests
{
    [Fact]
    public void Convert_BuildsTemplateComponentDocumentFromCrawlResult()
    {
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(BuildCrawlResult());

        document.SchemaVersion.Should().Be("site-generator/v1");
        document.Routes.Should().ContainSingle();
        document.Routes[0].Root.Type.Should().Be("PageShell");
        document.ComponentRequests.Should().BeEmpty();
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);

        var knownTypes = document.ComponentLibrary.Components.Select(component => component.Type).ToHashSet(StringComparer.Ordinal);
        Flatten(document.Routes[0].Root).Should().OnlyContain(node => knownTypes.Contains(node.Type));
        Flatten(document.Routes[0].Root).Select(node => node.Type).Should().Contain([
            "MegaHeader",
            "HeroBanner",
            "ContentArticle",
            "FormBlock",
            "InstitutionFooter",
        ]);
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

        var header = document.Routes[0].Root.Children.Single(node => node.Type == "MegaHeader");
        HeaderLinks(header).Should().BeEmpty();
        document.Routes[0].Root.Children.Should().NotContain(node => node.Type == "LinkList");
    }

    [Fact]
    public void Convert_MapsSpecialStaticRolesToReusableTemplateComponents()
    {
        var crawl = BuildCrawlResult();
        crawl.ExtractedModel.Pages[0].Sections.Add(new ExtractedSection
        {
            Id = "gallery-1",
            Role = "gallery",
            Headline = "Campus Gallery",
            Body = "Visual campus highlights.",
            SourceSelector = "section.gallery",
            Media =
            [
                new ExtractedMedia { Url = "https://example.com/assets/slide-1.jpg", Alt = "Slide 1", Kind = "image" },
                new ExtractedMedia { Url = "https://example.com/assets/slide-2.jpg", Alt = "Slide 2", Kind = "image" },
            ],
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        document.ComponentRequests.Should().BeEmpty();
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        var gallery = Flatten(document.Routes[0].Root).Single(node => node.Type == "MediaFeatureGrid");
        GetItems(gallery).Should().HaveCount(2);
    }

    [Fact]
    public void Convert_WhenVisualCardGridHasManyDistinctImages_TreatsItAsMediaFeatureGrid()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Regions =
            [
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "div.visual-slider",
                    Headline = "Visual Highlights",
                    Text = "Visual Highlights",
                    Items =
                    [
                        new ExtractedItem { Title = "A", MediaUrl = "https://example.com/a.jpg" },
                        new ExtractedItem { Title = "B", MediaUrl = "https://example.com/b.jpg" },
                        new ExtractedItem { Title = "C", MediaUrl = "https://example.com/c.jpg" },
                    ],
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var grid = Flatten(document.Routes[0].Root).Single(node => node.Type == "MediaFeatureGrid");
        GetItems(grid).Select(item => item["media_url"]).Should()
            .Contain(["https://example.com/a.jpg", "https://example.com/b.jpg", "https://example.com/c.jpg"]);
    }

    [Fact]
    public void Convert_RewritesTemplateItemLinksToGeneratedRoutes()
    {
        var crawl = BuildCrawlResult();
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

        var grid = Flatten(document.Routes[0].Root).Single(node => node.Type == "MediaFeatureGrid");
        GetItems(grid).Single()["url"].Should().Be("/programs/journalism");
    }

    [Fact]
    public void Convert_WhenRootQueryPagesExist_BuildsUniqueStaticRoutes()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/?Lang=zh-tw",
            Depth = 1,
            StatusCode = 200,
            Title = "Chinese",
            TextExcerpt = "Chinese homepage.",
            Links = [],
        });
        crawl.ExtractedModel.Pages.Add(new ExtractedPageModel
        {
            PageUrl = "https://example.com/?Lang=zh-tw",
            Sections =
            [
                new ExtractedSection
                {
                    Id = "zh-home",
                    Role = "content",
                    Headline = "Chinese",
                    Body = "Chinese homepage.",
                    SourceSelector = "main",
                },
            ],
        });
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);
        var quality = new SiteGenerationQualityAnalyzer().Analyze(document);

        document.Routes.Select(route => route.Path).Should().Contain(["/", "/Lang-zh-tw"]);
        document.Routes.Select(route => route.Path).Should().OnlyHaveUniqueItems();
        quality.IsPassed.Should().BeTrue();
    }

    [Fact]
    public void Convert_PreservesHeaderFooterAndUsesStaticRoutes()
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

        var header = document.Routes[0].Root.Children.Single(node => node.Type == "MegaHeader");
        header.Props["logo_url"].Should().Be("https://example.com/logo.png");
        HeaderLinks(header).Should().Contain(link => link["label"] == "Admissions" && link["url"] == "/admission");

        var footer = document.Routes[0].Root.Children.Single(node => node.Type == "InstitutionFooter");
        footer.Props["logo_url"].Should().Be("https://example.com/footer.png");
        footer.Props["contact_text"].Should().Be("No. 1, University Road");
        GetLinks(footer).Should().Contain(link => link["label"] == "Privacy" && link["url"] == "/privacy");

        var news = Flatten(document.Routes[0].Root).Single(node => node.Type == "NewsCardCarousel");
        GetItems(news).Single()["url"].Should().Be("/Spotlight/from-06-sID-32588");
    }

    [Fact]
    public void Convert_UsesVisualSnapshotBeforeStaticExtractedModel()
    {
        var crawl = BuildCrawlResult();
        crawl.ExtractedModel.Pages[0].Sections[0].Headline = "Static Source Hero";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header.site-header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 96 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/rendered-logo.png", Alt = "Rendered logo" },
                    ],
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.com/admission.aspx" },
                    ],
                },
                new VisualRegion
                {
                    Role = "hero",
                    Selector = "section.visual-hero",
                    Headline = "Rendered Hero",
                    Text = "Rendered page copy from the browser.",
                    Bounds = new VisualBox { X = 0, Y = 96, Width = 1366, Height = 520 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/rendered-hero.jpg", Alt = "Rendered campus" },
                    ],
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div.rendered-carousel",
                    Headline = "Rendered Stories",
                    Text = "Rendered carousel",
                    Bounds = new VisualBox { X = 0, Y = 650, Width = 1366, Height = 340 },
                    Items =
                    [
                        new ExtractedItem
                        {
                            Title = "Rendered Story",
                            Body = "Story from rendered layout.",
                            Url = "https://example.com/Spotlight.aspx?from=06&sID=32588",
                            MediaUrl = "https://example.com/story.jpg",
                            MediaAlt = "Story",
                        },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer.site-footer",
                    Text = "Rendered address",
                    Bounds = new VisualBox { X = 0, Y = 1200, Width = 1366, Height = 220 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/rendered-footer.png", Alt = "Rendered footer" },
                    ],
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var header = document.Routes[0].Root.Children.Single(node => node.Type == "MegaHeader");
        header.Props["logo_url"].Should().Be("https://example.com/rendered-logo.png");

        var nodes = Flatten(document.Routes[0].Root).ToList();
        nodes.Where(HasStaticSourceHeroTitle).Should().BeEmpty();
        nodes.Should().Contain(node => node.Type == "HeroBanner" && (string)node.Props["title"]! == "Rendered Hero");
        nodes.Should().Contain(node => node.Type == "NewsCardCarousel" && GetItems(node).Any(item => item["title"] == "Rendered Story"));

        var footer = document.Routes[0].Root.Children.Single(node => node.Type == "InstitutionFooter");
        footer.Props["logo_url"].Should().Be("https://example.com/rendered-footer.png");
        footer.Props["contact_text"].Should().Be("Rendered address");
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

    private static bool HasStaticSourceHeroTitle(ComponentNode node)
    {
        return node.Props.TryGetValue("title", out var title) &&
            string.Equals(title?.ToString(), "Static Source Hero", StringComparison.Ordinal);
    }

    private static List<Dictionary<string, string>> HeaderLinks(ComponentNode header)
    {
        return GetLinks(header, "utility_links").Concat(GetLinks(header, "primary_links")).ToList();
    }

    private static List<Dictionary<string, string>> GetLinks(ComponentNode node, string propName = "links")
    {
        return node.Props.TryGetValue(propName, out var value) && value is List<Dictionary<string, string>> links
            ? links
            : [];
    }

    private static List<Dictionary<string, string>> GetItems(ComponentNode node)
    {
        return node.Props.TryGetValue("items", out var value) && value is List<Dictionary<string, string>> items
            ? items
            : [];
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
