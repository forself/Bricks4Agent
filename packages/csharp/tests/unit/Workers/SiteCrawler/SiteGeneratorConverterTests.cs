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
    public void Convert_WhenBelowFoldHomeContentIsLarge_DoesNotPromoteItToHeroCarousel()
    {
        var crawl = BuildCrawlResult();
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
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.com/admissions" },
                        new ExtractedAction { Label = "Academics", Url = "https://example.com/academics" },
                        new ExtractedAction { Label = "News", Url = "https://example.com/news" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "main.home-content",
                    Headline = "Activity Board",
                    Text = "Activity Board 2026-04-30 Event A 2026-04-29 Event B Announcements News Center Campus Links",
                    Bounds = new VisualBox { X = 96, Y = 720, Width = 1120, Height = 1280 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/activity-a.jpg", Alt = "Activity A" },
                        new ExtractedMedia { Url = "https://example.com/activity-b.jpg", Alt = "Activity B" },
                        new ExtractedMedia { Url = "https://example.com/news-a.jpg", Alt = "News A" },
                    ],
                    Items =
                    [
                        new ExtractedItem { Title = "Event A", Body = "2026-04-30", MediaUrl = "https://example.com/activity-a.jpg" },
                        new ExtractedItem { Title = "Event B", Body = "2026-04-29", MediaUrl = "https://example.com/activity-b.jpg" },
                        new ExtractedItem { Title = "News A", Body = "Campus story.", MediaUrl = "https://example.com/news-a.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer.site-footer",
                    Text = "Address and campus links",
                    Bounds = new VisualBox { X = 0, Y = 2100, Width = 1366, Height = 260 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        Flatten(document.Routes[0].Root)
            .Where(node => node.Type == "HeroCarousel")
            .Select(node => node.Props.TryGetValue("title", out var title) ? title?.ToString() : string.Empty)
            .Should()
            .NotContain("Activity Board");
    }

    [Fact]
    public void Convert_WhenVisualSnapshotHasBroadContainers_PrefersSpecificHomeRegions()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "form",
                    Selector = "div.wrap",
                    Headline = "Quick Search",
                    Text = "Search Admissions Academics News",
                    Bounds = new VisualBox { X = 0, Y = 10, Width = 1366, Height = 670 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.com/admissions" },
                        new ExtractedAction { Label = "Academics", Url = "https://example.com/academics" },
                    ],
                },
                new VisualRegion
                {
                    Role = "header",
                    Selector = "div.head",
                    Bounds = new VisualBox { X = 100, Y = 20, Width = 1050, Height = 120 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/logo.png", Alt = "Logo" },
                    ],
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.com/admissions" },
                        new ExtractedAction { Label = "Academics", Url = "https://example.com/academics" },
                    ],
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Headline = "Admissions Banner",
                    Text = "Admissions Banner",
                    Bounds = new VisualBox { X = 0, Y = 137, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/banner-1.jpg", Alt = "Banner 1" },
                        new ExtractedMedia { Url = "https://example.com/banner-2.jpg", Alt = "Banner 2" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "main#main-content",
                    Headline = "Activity Board",
                    Text = "Activity Board Announcements News Center",
                    Bounds = new VisualBox { X = 0, Y = 720, Width = 1366, Height = 1800 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/activity-a.jpg", Alt = "Activity A" },
                        new ExtractedMedia { Url = "https://example.com/news-a.jpg", Alt = "News A" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "div#Dyn_2_2",
                    Headline = "Activity Board",
                    Text = "Activity Board",
                    Bounds = new VisualBox { X = 100, Y = 744, Width = 1053, Height = 522 },
                    Items =
                    [
                        new ExtractedItem { Title = "Event A", Body = "2026-04-30", MediaUrl = "https://example.com/activity-a.jpg" },
                        new ExtractedItem { Title = "Event B", Body = "2026-04-29", MediaUrl = "https://example.com/activity-b.jpg" },
                        new ExtractedItem { Title = "Event C", Body = "2026-04-29", MediaUrl = "https://example.com/activity-c.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer.site-footer",
                    Text = "Address and campus links",
                    Bounds = new VisualBox { X = 0, Y = 2600, Width = 1366, Height = 320 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);
        var nodes = Flatten(document.Routes[0].Root).ToList();

        nodes.Any(node => node.Type == "HeroCarousel" &&
            string.Equals(node.Props.TryGetValue("title", out var title) ? title?.ToString() : string.Empty, "Admissions Banner", StringComparison.Ordinal))
            .Should()
            .BeTrue();
        nodes.Any(node => node.Type is "NewsCardCarousel" or "NewsGrid" or "MediaFeatureGrid" &&
            GetItems(node).Any(item => item["title"] == "Event A"))
            .Should()
            .BeTrue();
        nodes.Should().NotContain(node => node.Type == "ServiceSearchHero");
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
        crawl.Pages.Add(new SiteCrawlPage
        {
            FinalUrl = "https://example.com/campus.php?Lang=zh-tw",
            Depth = 1,
            StatusCode = 200,
            Title = "Campus",
            TextExcerpt = "Campus.",
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
                new ExtractedAction { Label = "Campus", Url = "https://example.com/campus.php?Lang=zh-tw" },
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

        document.Routes.Select(route => route.Path).Should().Contain(["/", "/admission", "/Spotlight/from-06-sID-32588", "/campus/Lang-zh-tw"]);

        var header = document.Routes[0].Root.Children.Single(node => node.Type == "MegaHeader");
        header.Props["logo_url"].Should().Be("https://example.com/logo.png");
        HeaderLinks(header).Should().Contain(link => link["label"] == "Admissions" && link["url"] == "/admission");
        HeaderLinks(header).Should().Contain(link => link["label"] == "Campus" && link["url"] == "/campus/Lang-zh-tw");

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

    [Fact]
    public void Convert_WhenHeroCarouselTextIsOnlyControls_CleansControlText()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 100 },
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Text = "‹ › . . . . .",
                    Bounds = new VisualBox { X = 0, Y = 120, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero-a.jpg", Alt = "." },
                        new ExtractedMedia { Url = "https://example.com/hero-b.jpg", Alt = "Campus award" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 720, Width = 1366, Height = 160 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var hero = Flatten(document.Routes[0].Root).Single(node => node.Type == "HeroCarousel");
        hero.Props["body"].Should().Be(string.Empty);
        GetSlides(hero).Should().Contain(slide =>
            slide["media_url"] == "https://example.com/hero-a.jpg" &&
            slide["title"] == string.Empty &&
            slide["body"] == string.Empty);
    }

    [Fact]
    public void Convert_WhenVisualSnapshotContainsHeaderLogoRegion_DoesNotRenderDuplicateLogoArticle()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "div.head",
                    Bounds = new VisualBox { X = 100, Y = 20, Width = 1100, Height = 120 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/logo.png", Alt = "School logo" },
                    ],
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.com/admissions" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "div.mlogo",
                    Bounds = new VisualBox { X = 100, Y = 20, Width = 300, Height = 120 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/logo.png", Alt = "School logo" },
                    ],
                    Actions =
                    [
                        new ExtractedAction { Label = "Home", Url = "https://example.com/" },
                    ],
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 150, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 720, Width = 1366, Height = 160 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var duplicateLogoArticles = Flatten(document.Routes[0].Root)
            .Where(node => node.Type == "ContentArticle")
            .Where(node => HasMediaUrl(node, "https://example.com/logo.png"))
            .ToList();
        duplicateLogoArticles.Should().BeEmpty();
    }

    [Fact]
    public void Convert_WhenHomeHasMultipleVisualNewsRegions_PreservesSeparateSections()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 130, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "section.activity",
                    Headline = "Activity Board",
                    Bounds = new VisualBox { X = 80, Y = 760, Width = 1100, Height = 320 },
                    Items =
                    [
                        new ExtractedItem { Title = "Activity A", Body = "2026-04-30", MediaUrl = "https://example.com/activity-a.jpg" },
                        new ExtractedItem { Title = "Activity B", Body = "2026-04-29", MediaUrl = "https://example.com/activity-b.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "section.activity .module",
                    Headline = "Activity Board",
                    Bounds = new VisualBox { X = 90, Y = 780, Width = 1080, Height = 280 },
                    Items =
                    [
                        new ExtractedItem { Title = "Activity A", Body = "2026-04-30", MediaUrl = "https://example.com/activity-a.jpg" },
                        new ExtractedItem { Title = "Activity B", Body = "2026-04-29", MediaUrl = "https://example.com/activity-b.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "section.news",
                    Headline = "News Center",
                    Bounds = new VisualBox { X = 80, Y = 1240, Width = 1100, Height = 520 },
                    Items =
                    [
                        new ExtractedItem { Title = "News A", Body = "Campus story.", MediaUrl = "https://example.com/news-a.jpg" },
                        new ExtractedItem { Title = "News B", Body = "Campus story.", MediaUrl = "https://example.com/news-b.jpg" },
                        new ExtractedItem { Title = "News C", Body = "Campus story.", MediaUrl = "https://example.com/news-c.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 1850, Width = 1366, Height = 220 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        var sectionTitles = Flatten(document.Routes[0].Root)
            .Where(node => node.Type is "NewsGrid" or "NewsCardCarousel" or "MediaFeatureGrid")
            .Select(node => node.Props.TryGetValue("title", out var title) ? title?.ToString() : string.Empty)
            .ToList();
        sectionTitles.Should().Contain("Activity Board");
        sectionTitles.Should().Contain("News Center");
    }

    [Fact]
    public void Convert_WhenOnlySearchFormOnVisualHome_DoesNotAppendFormBlock()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Forms =
        [
            new ExtractedForm
            {
                Selector = "form.search",
                Method = "post",
                Action = "/app/index.php?Action=mobileptsearch",
                Fields =
                [
                    new ExtractedFormField { Name = "SchKey", Type = "text", Label = "關鍵字" },
                    new ExtractedFormField { Name = "req_token", Type = "hidden", Label = "req_token" },
                ],
            },
        ];
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Search", Url = "https://example.com/" },
                    ],
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 130, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 720, Width = 1366, Height = 160 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        Flatten(document.Routes[0].Root).Should().NotContain(node => node.Type == "FormBlock");
    }

    [Fact]
    public void Convert_WhenFooterHeadingIsNestedInFooter_DoesNotRenderContentArticle()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 130, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "div.footer",
                    Headline = "Campus Links",
                    Text = "Campus Links Privacy Contact",
                    Bounds = new VisualBox { X = 0, Y = 820, Width = 1366, Height = 260 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Privacy", Url = "https://example.com/privacy" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "div.footer header.mt",
                    Headline = "Campus Links",
                    Text = "Campus Links",
                    Bounds = new VisualBox { X = 880, Y = 850, Width = 360, Height = 40 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        Flatten(document.Routes[0].Root)
            .Where(node => node.Type == "ContentArticle")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Convert_WhenContentRegionOnlyHasChromeAction_DoesNotRenderEmptyContentArticle()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 130, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "div.skip-anchor",
                    Text = ":::",
                    Bounds = new VisualBox { X = 100, Y = 700, Width = 300, Height = 40 },
                    Actions =
                    [
                        new ExtractedAction { Label = ":::", Url = "https://example.com/" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 820, Width = 1366, Height = 220 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        Flatten(document.Routes[0].Root)
            .Where(node => node.Type == "ContentArticle")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Convert_WhenHomeContentRegionOnlyHasBody_DoesNotRenderCardFragmentAsArticle()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 130, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "div.d-item",
                    Text = "2026-04-30 Activity A",
                    Bounds = new VisualBox { X = 100, Y = 700, Width = 300, Height = 120 },
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 900, Width = 1366, Height = 220 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);

        Flatten(document.Routes[0].Root)
            .Where(node => node.Type == "ContentArticle")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Convert_WhenHomeHasMultipleNewsLikeVisualSections_UsesUnusedListForContentSlot()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 120 },
                },
                new VisualRegion
                {
                    Role = "carousel",
                    Selector = "div#banner",
                    Bounds = new VisualBox { X = 0, Y = 130, Width = 1366, Height = 500 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/hero.jpg", Alt = "Hero" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "section.activity",
                    Headline = "Activity Board",
                    Text = "Activity Board 2026-04-30 Event A 2026-04-29 Event B",
                    Bounds = new VisualBox { X = 100, Y = 720, Width = 1080, Height = 420 },
                    Items =
                    [
                        new ExtractedItem { Title = "Event A", Body = "2026-04-30", MediaUrl = "https://example.com/event-a.jpg" },
                        new ExtractedItem { Title = "Event B", Body = "2026-04-29", MediaUrl = "https://example.com/event-b.jpg" },
                        new ExtractedItem { Title = "Event C", Body = "2026-04-28", MediaUrl = "https://example.com/event-c.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "section.announcements",
                    Headline = "Announcement Center",
                    Text = "Announcement Center 2026-05-01 Notice A 2026-04-30 Notice B 2026-04-30 Notice C",
                    Bounds = new VisualBox { X = 100, Y = 1180, Width = 1080, Height = 720 },
                    Items =
                    [
                        new ExtractedItem { Title = "Notice A", Body = "2026-05-01", Url = "https://example.com/notice-a" },
                        new ExtractedItem { Title = "Notice B", Body = "2026-04-30", Url = "https://example.com/notice-b" },
                        new ExtractedItem { Title = "Notice C", Body = "2026-04-30", Url = "https://example.com/notice-c" },
                    ],
                    Actions =
                    [
                        new ExtractedAction { Label = "Announcements", Url = "https://example.com/announcements" },
                        new ExtractedAction { Label = "Research", Url = "https://example.com/research" },
                        new ExtractedAction { Label = "Admissions", Url = "https://example.com/admissions" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "section.announcements header.mt",
                    Headline = "Announcement Center",
                    Text = "ANNOUNCEMENT",
                    Bounds = new VisualBox { X = 100, Y = 1180, Width = 250, Height = 80 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.com/boardTitle.png", Alt = "Announcement Center" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "section.announcements .row:nth-child(1)",
                    Text = "2026-05-01 Notice A",
                    Bounds = new VisualBox { X = 300, Y = 1320, Width = 720, Height = 48 },
                    Items =
                    [
                        new ExtractedItem { Title = "Notice A", Body = "2026-05-01", Url = "https://example.com/notice-a" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "section.announcements .row:nth-child(2)",
                    Text = "2026-04-30 Notice B",
                    Bounds = new VisualBox { X = 300, Y = 1370, Width = 720, Height = 48 },
                    Items =
                    [
                        new ExtractedItem { Title = "Notice B", Body = "2026-04-30", Url = "https://example.com/notice-b" },
                    ],
                },
                new VisualRegion
                {
                    Role = "content",
                    Selector = "section.announcements .row:nth-child(3)",
                    Text = "2026-04-30 Notice C",
                    Bounds = new VisualBox { X = 300, Y = 1420, Width = 720, Height = 48 },
                    Items =
                    [
                        new ExtractedItem { Title = "Notice C", Body = "2026-04-30", Url = "https://example.com/notice-c" },
                    ],
                },
                new VisualRegion
                {
                    Role = "card_grid",
                    Selector = "section.news-center",
                    Headline = "News Center",
                    Text = "News Center 2026-04-24 Story A 2026-04-23 Story B 2026-04-21 Story C",
                    Bounds = new VisualBox { X = 100, Y = 1920, Width = 1080, Height = 360 },
                    Items =
                    [
                        new ExtractedItem { Title = "Story A", Body = "2026-04-24", MediaUrl = "https://example.com/story-a.jpg" },
                        new ExtractedItem { Title = "Story B", Body = "2026-04-23", MediaUrl = "https://example.com/story-b.jpg" },
                        new ExtractedItem { Title = "Story C", Body = "2026-04-21", MediaUrl = "https://example.com/story-c.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { X = 0, Y = 2400, Width = 1366, Height = 220 },
                },
            ],
        };
        var converter = new SiteGeneratorConverter(DefaultComponentLibrary.Create());

        var document = converter.Convert(crawl);
        var nodes = Flatten(document.Routes[0].Root).ToList();
        var children = document.Routes[0].Root.Children;

        nodes.Any(node => node.Type == "NewsGrid" &&
            node.Props.TryGetValue("title", out var title) &&
            string.Equals(title?.ToString(), "Activity Board", StringComparison.Ordinal))
            .Should()
            .BeTrue();
        nodes.Any(node => node.Type == "TabbedNewsBoard" &&
            node.Props.TryGetValue("title", out var title) &&
            string.Equals(title?.ToString(), "Announcement Center", StringComparison.Ordinal) &&
            GetTabs(node).Any(tab => string.Equals(tab["label"]?.ToString(), "Announcements", StringComparison.Ordinal)))
            .Should()
            .BeTrue();
        children.FindIndex(node => HasTitle(node, "Activity Board")).Should()
            .BeLessThan(children.FindIndex(node => HasTitle(node, "Announcement Center")));
        children.FindIndex(node => HasTitle(node, "Announcement Center")).Should()
            .BeLessThan(children.FindIndex(node => HasTitle(node, "News Center")));
        nodes.Any(node => node.Type == "ContentArticle" &&
            node.Props.TryGetValue("body", out var body) &&
            string.Equals(body?.ToString(), "ANNOUNCEMENT", StringComparison.Ordinal))
            .Should()
            .BeFalse();
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

    private static List<Dictionary<string, object?>> GetTabs(ComponentNode node)
    {
        return node.Props.TryGetValue("tabs", out var value) && value is List<Dictionary<string, object?>> tabs
            ? tabs
            : [];
    }

    private static bool HasTitle(ComponentNode node, string title)
    {
        return node.Props.TryGetValue("title", out var value) &&
            string.Equals(value?.ToString(), title, StringComparison.Ordinal);
    }

    private static List<Dictionary<string, string>> GetSlides(ComponentNode node)
    {
        return node.Props.TryGetValue("slides", out var value) && value is List<Dictionary<string, string>> slides
            ? slides
            : [];
    }

    private static bool HasMediaUrl(ComponentNode node, string mediaUrl)
    {
        if (!node.Props.TryGetValue("media", out var value) ||
            value is not List<Dictionary<string, string>> media)
        {
            return false;
        }

        return media.Any(item => item.TryGetValue("url", out var url) && url == mediaUrl);
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
