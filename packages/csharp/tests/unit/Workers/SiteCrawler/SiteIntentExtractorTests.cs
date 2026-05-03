using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteIntentExtractorTests
{
    [Fact]
    public void Extract_PrefersVisualSnapshotAndExtractsHeroNewsHome()
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

    [Fact]
    public void Extract_SplitsLargeWebFormsHomeRegionIntoHeaderHeroNewsAndFooter()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                new VisualRegion
                {
                    Id = "top",
                    Role = "form",
                    Selector = "div.logosearch-area",
                    Bounds = new VisualBox { Y = 0, Height = 140 },
                    Text = "Apply 招生與入學 校務系統 Welcome to SHU 關於世新",
                    Media = [new ExtractedMedia { Url = "https://example.edu/logo.png", Alt = "Logo" }],
                    Actions =
                    [
                        new ExtractedAction { Label = "招生與入學", Url = "https://example.edu/Admission.aspx" },
                        new ExtractedAction { Label = "校務系統", Url = "https://example.edu/System-info.aspx" },
                        new ExtractedAction { Label = "關於世新", Url = "https://example.edu/SHU.aspx" },
                        new ExtractedAction { Label = "網站導覽", Url = "https://example.edu/Sitemap.aspx" },
                    ],
                },
                new VisualRegion
                {
                    Id = "main",
                    Role = "card_grid",
                    Selector = "div:nth-of-type(3)",
                    Bounds = new VisualBox { Y = 140, Height = 3100 },
                    Headline = "亞洲夢工廠 全媒體全傳播 夢想從世新開始",
                    Text = "亞洲夢工廠 全媒體全傳播 夢想從世新開始 貼心提醒 Reminder 新生入學資訊 2026-05-01 焦點新聞",
                    Media =
                    [
                        new ExtractedMedia { Url = "https://example.edu/slide-1.jpg", Alt = "Slide 1" },
                        new ExtractedMedia { Url = "https://example.edu/slide-2.jpg", Alt = "Slide 2" },
                    ],
                    Items =
                    [
                        new ExtractedItem { Title = "焦點新聞 A", Body = "2026-05-01", Url = "https://example.edu/Spotlight.aspx?from=06&sID=1", MediaUrl = "https://example.edu/a.jpg" },
                        new ExtractedItem { Title = "焦點新聞 B", Body = "2026-04-30", Url = "https://example.edu/Spotlight.aspx?from=06&sID=2", MediaUrl = "https://example.edu/b.jpg" },
                    ],
                },
                new VisualRegion
                {
                    Id = "copyright",
                    Role = "content",
                    Selector = "div.m1-area",
                    Bounds = new VisualBox { Y = 3241, Height = 125 },
                    Text = "世新大學 版權所有 Shih Hsin University All Rights Reserved",
                },
            ],
        };
        var extractor = new SiteIntentExtractor();

        var intent = extractor.Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should()
            .Contain(["header", "hero_carousel", "news_carousel", "footer"]);
    }

    [Fact]
    public void Extract_ClassifiesSearchServiceVisualPatterns()
    {
        var crawl = BuildCrawlResult();
        crawl.Root = new SiteCrawlRoot
        {
            StartUrl = "https://www.gov.tw/",
            NormalizedStartUrl = "https://www.gov.tw/",
            Origin = "https://www.gov.tw",
            PathPrefix = "/",
        };
        crawl.Pages[0].Url = "https://www.gov.tw/";
        crawl.Pages[0].FinalUrl = "https://www.gov.tw/";
        crawl.Pages[0].Title = "Government Service Portal";
        crawl.Pages[0].TextExcerpt = "Search public services, application services, and government information.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                new VisualRegion
                {
                    Id = "header",
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { Y = 0, Height = 96 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Home", Url = "https://www.gov.tw/" },
                        new ExtractedAction { Label = "Site map", Url = "https://www.gov.tw/sitemap" },
                    ],
                },
                new VisualRegion
                {
                    Id = "search",
                    Role = "content",
                    Selector = ".portal-search",
                    Bounds = new VisualBox { Y = 96, Height = 360 },
                    Headline = "How can we help?",
                    Text = "Search services and hot keywords: tax filing, household registration, parking.",
                    Actions =
                    [
                        new ExtractedAction { Label = "Tax filing", Url = "https://www.gov.tw/service/tax" },
                        new ExtractedAction { Label = "Household registration", Url = "https://www.gov.tw/service/household" },
                        new ExtractedAction { Label = "Parking payment", Url = "https://www.gov.tw/service/parking" },
                    ],
                },
                new VisualRegion
                {
                    Id = "categories",
                    Role = "card_grid",
                    Selector = ".service-categories",
                    Bounds = new VisualBox { Y = 480, Height = 420 },
                    Headline = "Citizen Services",
                    Text = "Household Healthcare Transport Employment Tax",
                    Items =
                    [
                        new ExtractedItem { Title = "Household", Body = "Registration and identity services.", Url = "https://www.gov.tw/service/household" },
                        new ExtractedItem { Title = "Healthcare", Body = "Health insurance and medical services.", Url = "https://www.gov.tw/service/health" },
                        new ExtractedItem { Title = "Transport", Body = "Parking, traffic, and licenses.", Url = "https://www.gov.tw/service/transport" },
                    ],
                },
                new VisualRegion
                {
                    Id = "news",
                    Role = "news",
                    Selector = ".announcements",
                    Bounds = new VisualBox { Y = 940, Height = 320 },
                    Headline = "Announcements",
                    Text = "2026-05-01 Service update",
                    Items =
                    [
                        new ExtractedItem { Title = "Service update", Body = "2026-05-01", Url = "https://www.gov.tw/news/1" },
                        new ExtractedItem { Title = "New application", Body = "2026-04-28", Url = "https://www.gov.tw/news/2" },
                    ],
                },
                new VisualRegion
                {
                    Id = "footer",
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { Y = 1300, Height = 180 },
                    Text = "Government service contact center.",
                },
            ],
        };
        var extractor = new SiteIntentExtractor();

        var intent = extractor.Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "header",
            "search_hero",
            "service_category_grid",
            "tabbed_news",
            "footer",
        ]);
        intent.Pages[0].Blocks.Single(block => block.Kind == "search_hero").Slot.Should().Be("search");
        intent.Pages[0].Blocks.Single(block => block.Kind == "service_category_grid").Slot.Should().Be("service_categories");
        intent.Pages[0].Blocks.Single(block => block.Kind == "tabbed_news").Slot.Should().Be("tabbed_news");
    }

    [Fact]
    public void Extract_ClassifiesServiceActionVisualPatterns()
    {
        var crawl = BuildCrawlResult();
        crawl.Root = new SiteCrawlRoot
        {
            StartUrl = "https://www.cgh.org.tw/",
            NormalizedStartUrl = "https://www.cgh.org.tw/",
            Origin = "https://www.cgh.org.tw",
            PathPrefix = "/",
        };
        crawl.Pages[0].Url = "https://www.cgh.org.tw/";
        crawl.Pages[0].FinalUrl = "https://www.cgh.org.tw/";
        crawl.Pages[0].Title = "Example Hospital";
        crawl.Pages[0].TextExcerpt = "Hospital outpatient registration, doctors, departments, and medical services.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                new VisualRegion
                {
                    Id = "header",
                    Role = "header",
                    Selector = "header",
                    Bounds = new VisualBox { Y = 0, Height = 108 },
                    Actions =
                    [
                        new ExtractedAction { Label = "Departments", Url = "https://www.cgh.org.tw/departments" },
                        new ExtractedAction { Label = "Contact", Url = "https://www.cgh.org.tw/contact" },
                    ],
                },
                new VisualRegion
                {
                    Id = "hero",
                    Role = "carousel",
                    Selector = ".hero",
                    Bounds = new VisualBox { Y = 110, Height = 420 },
                    Headline = "Patient-centered care",
                    Text = "Patient-centered care and medical service.",
                    Media =
                    [
                        new ExtractedMedia { Url = "https://www.cgh.org.tw/hero-a.jpg", Alt = "Hospital" },
                        new ExtractedMedia { Url = "https://www.cgh.org.tw/hero-b.jpg", Alt = "Care" },
                    ],
                },
                new VisualRegion
                {
                    Id = "actions",
                    Role = "card_grid",
                    Selector = ".service-actions",
                    Bounds = new VisualBox { Y = 560, Height = 220 },
                    Headline = "Medical Services",
                    Text = "Online registration Find a doctor Departments Emergency",
                    Actions =
                    [
                        new ExtractedAction { Label = "Online registration", Url = "https://www.cgh.org.tw/register", Kind = "primary" },
                        new ExtractedAction { Label = "Find a doctor", Url = "https://www.cgh.org.tw/doctors" },
                        new ExtractedAction { Label = "Departments", Url = "https://www.cgh.org.tw/departments" },
                        new ExtractedAction { Label = "Emergency", Url = "https://www.cgh.org.tw/emergency", Kind = "primary" },
                    ],
                },
                new VisualRegion
                {
                    Id = "news",
                    Role = "news",
                    Selector = ".latest-news",
                    Bounds = new VisualBox { Y = 820, Height = 360 },
                    Headline = "Latest News",
                    Text = "2026-05-01 Clinic notice",
                    Items =
                    [
                        new ExtractedItem { Title = "Clinic notice", Body = "2026-05-01", Url = "https://www.cgh.org.tw/news/1" },
                        new ExtractedItem { Title = "Receipt notice", Body = "2026-04-22", Url = "https://www.cgh.org.tw/news/2" },
                    ],
                },
                new VisualRegion
                {
                    Id = "footer",
                    Role = "footer",
                    Selector = "footer",
                    Bounds = new VisualBox { Y = 1240, Height = 180 },
                    Text = "Hospital address and contact.",
                },
            ],
        };
        var extractor = new SiteIntentExtractor();

        var intent = extractor.Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "header",
            "hero_carousel",
            "service_action_grid",
            "tabbed_news",
            "footer",
        ]);
        intent.Pages[0].Blocks.Single(block => block.Kind == "service_action_grid").Slot.Should().Be("service_actions");
        intent.Pages[0].Blocks.Single(block => block.Kind == "tabbed_news").Slot.Should().Be("tabbed_news");
    }

    [Fact]
    public void Extract_ClassifiesSearchResultsVisualPatterns()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Title = "Search results";
        crawl.Pages[0].TextExcerpt = "Search results for permits with filters and pagination.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                BuildRegion("header", "header", "header", 0, "Search"),
                BuildRegion("query", "search", ".search", 110, "Search permits", "Keyword suggestions", actions:
                [
                    new ExtractedAction { Label = "Building permit", Url = "https://example.edu/search?q=building" },
                    new ExtractedAction { Label = "Parking permit", Url = "https://example.edu/search?q=parking" },
                ]),
                BuildRegion("filters", "filters", ".filters", 230, "Filter results", "Type Date Department"),
                BuildRegion("results", "results", ".results", 360, "120 results", "Result snippets", items:
                [
                    new ExtractedItem { Title = "Building permit guide", Body = "Permit application result.", Url = "https://example.edu/results/1" },
                    new ExtractedItem { Title = "Parking permit service", Body = "Parking permit result.", Url = "https://example.edu/results/2" },
                ]),
                BuildRegion("pager", "pagination", ".pager", 760, "Pages", "1 2 Next", actions:
                [
                    new ExtractedAction { Label = "2", Url = "https://example.edu/search?page=2" },
                    new ExtractedAction { Label = "Next", Url = "https://example.edu/search?page=2" },
                ]),
                BuildRegion("footer", "footer", "footer", 900, "Footer"),
            ],
        };

        var intent = new SiteIntentExtractor().Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "search_box",
            "filter_panel",
            "result_list",
            "pagination",
        ]);
        intent.Pages[0].Blocks.Single(block => block.Kind == "search_box").Slot.Should().Be("search_box");
        intent.Pages[0].Blocks.Single(block => block.Kind == "filter_panel").Slot.Should().Be("filter_panel");
        intent.Pages[0].Blocks.Single(block => block.Kind == "result_list").Slot.Should().Be("result_list");
        intent.Pages[0].Blocks.Single(block => block.Kind == "pagination").Slot.Should().Be("pagination");
    }

    [Fact]
    public void Extract_ClassifiesReportDashboardVisualPatterns()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Title = "Performance dashboard";
        crawl.Pages[0].TextExcerpt = "Dashboard with filters, KPI metrics, charts, and data table.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                BuildRegion("header", "header", "header", 0, "Dashboard"),
                BuildRegion("filters", "filter_bar", ".dashboard-filters", 110, "Report filters", "Date range Region Export", actions:
                [
                    new ExtractedAction { Label = "Export CSV", Url = "https://example.edu/report.csv" },
                ]),
                BuildRegion("metrics", "stats", ".metrics", 220, "Key metrics", "Total 1200 Growth 12% Active 98"),
                BuildRegion("chart", "chart", ".chart", 380, "Monthly trend", "Jan 20 Feb 32 Mar 41"),
                BuildRegion("table", "table", ".data-table", 620, "Detailed rows", "Department Count Status", items:
                [
                    new ExtractedItem { Title = "Admissions", Body = "120 Complete", Url = "https://example.edu/report/admissions" },
                    new ExtractedItem { Title = "Library", Body = "80 Pending", Url = "https://example.edu/report/library" },
                ]),
                BuildRegion("footer", "footer", "footer", 920, "Footer"),
            ],
        };

        var intent = new SiteIntentExtractor().Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "filter_bar",
            "metric_summary",
            "chart_panel",
            "data_table",
        ]);
        intent.Pages[0].Blocks.Single(block => block.Kind == "filter_bar").Slot.Should().Be("filter_bar");
        intent.Pages[0].Blocks.Single(block => block.Kind == "metric_summary").Slot.Should().Be("metric_summary");
        intent.Pages[0].Blocks.Single(block => block.Kind == "chart_panel").Slot.Should().Be("chart_panel");
        intent.Pages[0].Blocks.Single(block => block.Kind == "data_table").Slot.Should().Be("data_table");
    }

    [Fact]
    public void Extract_ClassifiesInputFlowVisualPatterns()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Title = "Application form";
        crawl.Pages[0].TextExcerpt = "Step 1 applicant information required fields continue submit.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                BuildRegion("header", "header", "header", 0, "Application"),
                BuildRegion("steps", "steps", ".steps", 110, "Step 1 of 3", "Applicant Information Review Submit"),
                BuildRegion("form", "form", ".application-form", 220, "Applicant information", "Name Email Phone Required"),
                BuildRegion("validation", "validation", ".validation", 560, "Required fields", "Name and email are required."),
                BuildRegion("actions", "action_bar", ".actions", 680, "Actions", "Back Continue Submit", actions:
                [
                    new ExtractedAction { Label = "Back", Url = "https://example.edu/apply?step=0", Kind = "secondary" },
                    new ExtractedAction { Label = "Continue", Url = "https://example.edu/apply?step=2", Kind = "primary" },
                ]),
                BuildRegion("footer", "footer", "footer", 900, "Footer"),
            ],
        };

        var intent = new SiteIntentExtractor().Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "step_indicator",
            "form_fields",
            "validation_summary",
            "action_bar",
        ]);
        intent.Pages[0].Blocks.Single(block => block.Kind == "step_indicator").Slot.Should().Be("step_indicator");
        intent.Pages[0].Blocks.Single(block => block.Kind == "form_fields").Slot.Should().Be("form_fields");
        intent.Pages[0].Blocks.Single(block => block.Kind == "validation_summary").Slot.Should().Be("validation_summary");
        intent.Pages[0].Blocks.Single(block => block.Kind == "action_bar").Slot.Should().Be("action_bar");
    }

    [Fact]
    public void Extract_ClassifiesCommercialShowcaseVisualPatterns()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Title = "Product showcase";
        crawl.Pages[0].TextExcerpt = "Product showcase pricing plans testimonials start free contact sales.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                BuildRegion("header", "header", "header", 0, "Product"),
                BuildRegion("hero", "product_hero", ".hero", 110, "Build faster", "Launch your product with a modern platform.", media:
                [
                    new ExtractedMedia { Url = "https://example.edu/product.jpg", Alt = "Product" },
                ], actions:
                [
                    new ExtractedAction { Label = "Start free", Url = "https://example.edu/signup", Kind = "primary" },
                    new ExtractedAction { Label = "Contact sales", Url = "https://example.edu/contact", Kind = "secondary" },
                ]),
                BuildRegion("products", "products", ".products", 480, "Products", "Plan Feature Offer", items:
                [
                    new ExtractedItem { Title = "Starter", Body = "For small teams.", Url = "https://example.edu/starter", MediaUrl = "https://example.edu/starter.jpg", MediaAlt = "Starter" },
                    new ExtractedItem { Title = "Scale", Body = "For growing teams.", Url = "https://example.edu/scale", MediaUrl = "https://example.edu/scale.jpg", MediaAlt = "Scale" },
                ]),
                BuildRegion("proof", "proof", ".proof", 760, "Trusted by teams", "500 customers 99.9 uptime"),
                BuildRegion("pricing", "pricing", ".pricing", 900, "Pricing", "$19 Starter $49 Pro", items:
                [
                    new ExtractedItem { Title = "Starter", Body = "$19 per month", Url = "https://example.edu/pricing/starter" },
                    new ExtractedItem { Title = "Pro", Body = "$49 per month", Url = "https://example.edu/pricing/pro" },
                ]),
                BuildRegion("cta", "cta", ".cta", 1160, "Ready to start?", "Start free today.", actions:
                [
                    new ExtractedAction { Label = "Start free", Url = "https://example.edu/signup", Kind = "primary" },
                ]),
                BuildRegion("footer", "footer", "footer", 1340, "Footer"),
            ],
        };

        var intent = new SiteIntentExtractor().Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "showcase_hero",
            "product_cards",
            "proof_strip",
            "pricing_panel",
            "cta_band",
        ]);
        intent.Pages[0].Blocks.Single(block => block.Kind == "showcase_hero").Slot.Should().Be("showcase_hero");
        intent.Pages[0].Blocks.Single(block => block.Kind == "product_cards").Slot.Should().Be("product_cards");
        intent.Pages[0].Blocks.Single(block => block.Kind == "proof_strip").Slot.Should().Be("proof_strip");
        intent.Pages[0].Blocks.Single(block => block.Kind == "pricing_panel").Slot.Should().Be("pricing_panel");
        intent.Pages[0].Blocks.Single(block => block.Kind == "cta_band").Slot.Should().Be("cta_band");
    }

    [Fact]
    public void Extract_SplitsLargeCommercialVisualRegionIntoReusableSlots()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Title = "Pricing built for businesses of all sizes";
        crawl.Pages[0].TextExcerpt = "Products features pricing plans get started contact sales.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                BuildRegion("header", "header", "header", 0, "Product"),
                new VisualRegion
                {
                    Id = "marketing",
                    Role = "content",
                    Selector = "div#marketing",
                    Bounds = new VisualBox { Y = 120, Height = 1500, Width = 1200 },
                    Headline = "Pricing built for businesses of all sizes",
                    Text = """
                        Pricing built for businesses of all sizes
                        Access a complete payments platform with simple, pay-as-you-go pricing.
                        Get started Contact sales
                        Features available out of the box
                        Global access Built-in fraud prevention Tools to optimize your checkout
                        Standard pricing
                        Starter $19 per month Pro $49 per month
                        Ready to start?
                        Start now
                        """,
                    Actions =
                    [
                        new ExtractedAction { Label = "Get started", Url = "https://example.edu/start", Kind = "primary" },
                        new ExtractedAction { Label = "Contact sales", Url = "https://example.edu/contact", Kind = "secondary" },
                    ],
                    Items =
                    [
                        new ExtractedItem { Title = "Global access", Body = "195 countries and 135 currencies.", Url = "https://example.edu/global" },
                        new ExtractedItem { Title = "Built-in fraud prevention", Body = "Machine learning fraud tools.", Url = "https://example.edu/fraud" },
                        new ExtractedItem { Title = "Starter", Body = "$19 per month", Url = "https://example.edu/pricing/starter" },
                        new ExtractedItem { Title = "Pro", Body = "$49 per month", Url = "https://example.edu/pricing/pro" },
                    ],
                },
                BuildRegion("footer", "footer", "footer", 1700, "Footer"),
            ],
        };

        var intent = new SiteIntentExtractor().Extract(crawl);

        intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain([
            "showcase_hero",
            "product_cards",
            "pricing_panel",
            "cta_band",
        ]);
    }

    [Fact]
    public void Extract_DoesNotApplyPageLevelPricingIntentToStructuralNavAndFooter()
    {
        var crawl = BuildCrawlResult();
        crawl.Pages[0].Title = "Pricing built for businesses of all sizes";
        crawl.Pages[0].TextExcerpt = "Products features pricing plans get started contact sales.";
        crawl.Pages[0].VisualSnapshot = new VisualPageSnapshot
        {
            Regions =
            [
                BuildRegion("hero", "content", "main#pricing", 140, "Pricing", "Pricing plans for teams."),
                BuildRegion("pricing-nav", "content", "nav.PricingHeroNav__nav", 420, "", "Overview Enterprise Contact sales"),
                BuildRegion("footer-copy", "content", "footer.Copy__footer", 1850, "", "Products Developers Resources Company"),
            ],
        };

        var intent = new SiteIntentExtractor().Extract(crawl);

        intent.Pages[0].Blocks.Single(block => block.Id == "pricing-nav").Kind.Should().NotBe("pricing_panel");
        intent.Pages[0].Blocks.Single(block => block.Id == "footer-copy").Kind.Should().Be("footer");
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

    private static VisualRegion BuildRegion(
        string id,
        string role,
        string selector,
        double y,
        string headline,
        string text = "",
        List<ExtractedAction>? actions = null,
        List<ExtractedItem>? items = null,
        List<ExtractedMedia>? media = null)
    {
        return new VisualRegion
        {
            Id = id,
            Role = role,
            Selector = selector,
            Bounds = new VisualBox { Y = y, Height = 160, Width = 1200 },
            Headline = headline,
            Text = string.IsNullOrWhiteSpace(text) ? headline : text,
            Actions = actions ?? [],
            Items = items ?? [],
            Media = media ?? [],
        };
    }
}
