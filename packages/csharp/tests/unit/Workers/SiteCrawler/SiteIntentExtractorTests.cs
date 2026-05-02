using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteIntentExtractorTests
{
    [Fact]
    public void Extract_PrefersVisualSnapshotAndClassifiesUniversityHome()
    {
        var crawl = BuildCrawlResult();
        crawl.ExtractedModel.Pages[0].Sections.Add(new ExtractedSection
        {
            Id = "static-hero",
            Role = "hero",
            Headline = "Static Source Hero",
            Body = "This should lose to the rendered visual snapshot.",
            SourceSelector = "section.static",
        });
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            CaptureMode = "browser_render",
            Viewport = new VisualViewport { Width = 1366, Height = 900 },
            Regions =
            [
                new VisualRegion
                {
                    Id = "rendered-header",
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { X = 0, Y = 0, Width = 1366, Height = 96 },
                    Media = [new ExtractedMedia { Url = "https://example.edu/logo.png", Alt = "University logo" }],
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.edu/admission.aspx" },
                        new ExtractedAction { Label = "About", Url = "https://example.edu/about.aspx" },
                    ],
                },
                new VisualRegion
                {
                    Id = "rendered-hero",
                    Role = "carousel",
                    Selector = ".hero-slider",
                    Headline = "Rendered Campus",
                    Text = "Rendered Campus\nWelcome to Example University.",
                    Bounds = new VisualBox { X = 0, Y = 96, Width = 1366, Height = 480 },
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.edu/slide-1.jpg", Alt = "Slide 1" },
                        new ExtractedMedia { Url = "https://example.edu/slide-2.jpg", Alt = "Slide 2" },
                    ],
                },
                new VisualRegion
                {
                    Id = "quick",
                    Role = "card_grid",
                    Selector = ".quick-links",
                    Headline = "Quick Links",
                    Text = "Admissions\nLibrary\nCalendar",
                    Bounds = new VisualBox { X = 80, Y = 600, Width = 1120, Height = 140 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Admissions", Url = "https://example.edu/admission.aspx" },
                        new ExtractedAction { Label = "Library", Url = "https://example.edu/library.aspx" },
                        new ExtractedAction { Label = "Calendar", Url = "https://example.edu/calendar.aspx" },
                    ],
                },
                new VisualRegion
                {
                    Id = "news",
                    Role = "news",
                    Selector = ".news-slider",
                    Headline = "Latest News",
                    Text = "2026-05-01 Campus event\n2026-04-20 Research award",
                    Bounds = new VisualBox { X = 80, Y = 760, Width = 1120, Height = 260 },
                    Items =
                    [
                        new ExtractedItem { Title = "Campus event", Body = "2026-05-01", Url = "https://example.edu/news.aspx?id=1" },
                        new ExtractedItem { Title = "Research award", Body = "2026-04-20", Url = "https://example.edu/news.aspx?id=2" },
                    ],
                },
                new VisualRegion
                {
                    Id = "footer",
                    Role = "footer",
                    Selector = "footer",
                    Text = "No. 1 University Road",
                    Bounds = new VisualBox { X = 0, Y = 1200, Width = 1366, Height = 180 },
                },
            ],
        };
        var extractor = new SiteIntentExtractor();

        var intent = extractor.Extract(crawl);

        intent.SiteKind.Should().Be("university");
        var home = intent.Pages.Should().ContainSingle(page => page.Depth == 0).Subject;
        home.PageType.Should().Be("home");
        home.Blocks.Select(block => block.Kind).Should().Contain(["header", "hero_carousel", "quick_links", "news_carousel", "footer"]);
        home.Blocks.Should().NotContain(block => block.Section.Headline == "Static Source Hero");
        intent.GlobalHeader.PrimaryLinks.Should().Contain(link => link.Label == "Admissions");
        intent.GlobalFooter.Text.Should().Be("No. 1 University Road");
    }

    [Fact]
    public void Extract_ClassifiesListingPagesFromRepeatedItems()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Depth = 1;
        crawl.Pages[0].Title = "News";
        crawl.ExtractedModel.Pages[0].Sections =
        [
            new ExtractedSection
            {
                Id = "news-list",
                Role = "news",
                Headline = "News",
                Body = "Recent updates.",
                SourceSelector = ".news-list",
                Items =
                [
                    new ExtractedItem { Title = "A", Url = "https://example.edu/news.aspx?id=1" },
                    new ExtractedItem { Title = "B", Url = "https://example.edu/news.aspx?id=2" },
                    new ExtractedItem { Title = "C", Url = "https://example.edu/news.aspx?id=3" },
                    new ExtractedItem { Title = "D", Url = "https://example.edu/news.aspx?id=4" },
                ],
            },
        ];
        var extractor = new SiteIntentExtractor();

        var intent = extractor.Extract(crawl);

        intent.Pages[0].PageType.Should().Be("listing");
        intent.Pages[0].Blocks.Should().Contain(block => block.Kind == "article_list");
    }

    [Fact]
    public void Extract_ClassifiesArticlePagesFromLongText()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Depth = 2;
        crawl.Pages[0].Title = "About Example University";
        crawl.ExtractedModel.Pages[0].Sections =
        [
            new ExtractedSection
            {
                Id = "article",
                Role = "article",
                Headline = "About Example University",
                Body = string.Join(" ", Enumerable.Repeat("A long institutional article paragraph.", 35)),
                SourceSelector = "main article",
            },
        ];
        var extractor = new SiteIntentExtractor();

        var intent = extractor.Extract(crawl);

        intent.Pages[0].PageType.Should().Be("article");
        intent.Pages[0].Blocks.Should().Contain(block => block.Kind == "content_article");
    }

    private static SiteCrawlResult BuildCrawlResult()
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
                    Url = "https://example.edu/",
                    FinalUrl = "https://example.edu/",
                    Depth = 0,
                    StatusCode = 200,
                    Title = "Example University",
                    TextExcerpt = "Example University is a school.",
                    Links = ["https://example.edu/about.aspx", "https://example.edu/news.aspx"],
                },
            ],
            ExtractedModel = new ExtractedSiteModel
            {
                Pages =
                [
                    new ExtractedPageModel
                    {
                        PageUrl = "https://example.edu/",
                        Header = new ExtractedHeader
                        {
                            PrimaryLinks =
                            [
                                new ExtractedAction { Label = "About", Url = "https://example.edu/about.aspx" },
                            ],
                        },
                    },
                ],
                ThemeTokens = new ExtractedThemeTokens
                {
                    Colors = { ["brand"] = "#004a98" },
                    Typography = { ["font_family"] = "Inter, sans-serif" },
                },
            },
        };
    }
}
