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

    [Fact]
    public void Compile_UsesSearchServicePortalComponents()
    {
        var crawl = BuildPublicServiceCrawl();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentExtractor().Extract(crawl);
        var plan = new TemplateMatcher(new TemplateFrameworkLoader().LoadDefault(), manifest).Match(intent);
        var compiler = new TemplateCompiler(manifest);

        var document = compiler.Compile(crawl, intent, plan);

        plan.TemplateId.Should().Be("search_service_portal");
        var types = Flatten(document.Routes[0].Root).Select(node => node.Type).ToList();
        types.Should().Contain([
            "MegaHeader",
            "ServiceSearchHero",
            "ServiceCategoryGrid",
            "TabbedNewsBoard",
            "InstitutionFooter",
        ]);
        var search = Flatten(document.Routes[0].Root).Single(node => node.Type == "ServiceSearchHero");
        search.Props["query_placeholder"].Should().Be("Search services");
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_UsesServiceActionPortalComponents()
    {
        var crawl = BuildHealthcareCrawl();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentExtractor().Extract(crawl);
        var plan = new TemplateMatcher(new TemplateFrameworkLoader().LoadDefault(), manifest).Match(intent);
        var compiler = new TemplateCompiler(manifest);

        var document = compiler.Compile(crawl, intent, plan);

        plan.TemplateId.Should().Be("service_action_portal");
        var types = Flatten(document.Routes[0].Root).Select(node => node.Type).ToList();
        types.Should().Contain([
            "MegaHeader",
            "HeroCarousel",
            "ServiceActionGrid",
            "TabbedNewsBoard",
            "InstitutionFooter",
        ]);
        var actions = Flatten(document.Routes[0].Root).Single(node => node.Type == "ServiceActionGrid");
        actions.Props["actions"].Should().BeAssignableTo<IEnumerable<Dictionary<string, string>>>();
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_UsesSearchResultsComponents()
    {
        var document = CompileCrawl(BuildSearchResultsCrawl(), out var plan);

        plan.TemplateId.Should().Be("search_results_portal");
        var types = Flatten(document.Routes[0].Root).Select(node => node.Type).ToList();
        types.Should().Contain(["SearchBoxPanel", "FacetFilterPanel", "ResultList", "PaginationNav"]);
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_UsesReportDashboardComponents()
    {
        var document = CompileCrawl(BuildReportDashboardCrawl(), out var plan);

        plan.TemplateId.Should().Be("report_dashboard");
        var types = Flatten(document.Routes[0].Root).Select(node => node.Type).ToList();
        types.Should().Contain(["DashboardFilterBar", "MetricSummaryGrid", "ChartPanel", "DataTablePreview"]);
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_UsesInputFlowComponents()
    {
        var document = CompileCrawl(BuildInputFlowCrawl(), out var plan);

        plan.TemplateId.Should().Be("input_flow");
        var types = Flatten(document.Routes[0].Root).Select(node => node.Type).ToList();
        types.Should().Contain(["StepIndicator", "StructuredFormPanel", "ValidationSummary", "FormActionBar"]);
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_UsesCommercialShowcaseComponents()
    {
        var document = CompileCrawl(BuildCommercialShowcaseCrawl(), out var plan);

        plan.TemplateId.Should().Be("commercial_showcase");
        var types = Flatten(document.Routes[0].Root).Select(node => node.Type).ToList();
        types.Should().Contain(["ShowcaseHero", "ProductCardGrid", "ProofStrip", "PricingPanel", "CtaBand"]);
        document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
        new ComponentSchemaValidator().Validate(document).IsValid.Should().BeTrue();
    }

    private static GeneratorSiteDocument CompileCrawl(SiteCrawlResult crawl, out TemplatePlan plan)
    {
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentExtractor().Extract(crawl);
        plan = new TemplateMatcher(new TemplateFrameworkLoader().LoadDefault(), manifest).Match(intent);
        return new TemplateCompiler(manifest).Compile(crawl, intent, plan);
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

    private static SiteCrawlResult BuildPublicServiceCrawl()
    {
        return new SiteCrawlResult
        {
            CrawlRunId = "crawl-public-service",
            Root = new SiteCrawlRoot
            {
                StartUrl = "https://www.gov.tw/",
                NormalizedStartUrl = "https://www.gov.tw/",
                Origin = "https://www.gov.tw",
                PathPrefix = "/",
            },
            Pages =
            [
                new SiteCrawlPage
                {
                    FinalUrl = "https://www.gov.tw/",
                    Depth = 0,
                    StatusCode = 200,
                    Title = "Government Service Portal",
                    TextExcerpt = "Search public services and application services.",
                    VisualSnapshot = new VisualPageSnapshot
                    {
                        Regions =
                        [
                            new VisualRegion
                            {
                                Id = "header",
                                Role = "header",
                                Selector = "header",
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
                                Bounds = new VisualBox { Y = 120, Height = 300 },
                                Headline = "How can we help?",
                                Text = "Search services and hot keywords.",
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
                                Text = "Household Healthcare Transport",
                                Items =
                                [
                                    new ExtractedItem { Title = "Household", Body = "Registration services.", Url = "https://www.gov.tw/service/household" },
                                    new ExtractedItem { Title = "Healthcare", Body = "Insurance and medical services.", Url = "https://www.gov.tw/service/health" },
                                    new ExtractedItem { Title = "Transport", Body = "Parking and traffic.", Url = "https://www.gov.tw/service/transport" },
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
                                Text = "Government service contact center.",
                            },
                        ],
                    },
                },
            ],
        };
    }

    private static SiteCrawlResult BuildHealthcareCrawl()
    {
        return new SiteCrawlResult
        {
            CrawlRunId = "crawl-healthcare",
            Root = new SiteCrawlRoot
            {
                StartUrl = "https://www.cgh.org.tw/",
                NormalizedStartUrl = "https://www.cgh.org.tw/",
                Origin = "https://www.cgh.org.tw",
                PathPrefix = "/",
            },
            Pages =
            [
                new SiteCrawlPage
                {
                    FinalUrl = "https://www.cgh.org.tw/",
                    Depth = 0,
                    StatusCode = 200,
                    Title = "Example Hospital",
                    TextExcerpt = "Hospital outpatient registration, doctors, departments, and medical services.",
                    VisualSnapshot = new VisualPageSnapshot
                    {
                        Regions =
                        [
                            new VisualRegion
                            {
                                Id = "header",
                                Role = "header",
                                Selector = "header",
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
                                Bounds = new VisualBox { Y = 120, Height = 420 },
                                Headline = "Patient-centered care",
                                Text = "Patient-centered care.",
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
                                Text = "Hospital address and contact.",
                            },
                        ],
                    },
                },
            ],
        };
    }

    private static SiteCrawlResult BuildSearchResultsCrawl()
    {
        return BuildPatternCrawl(
            "crawl-search-results",
            "Search results",
            "Search results with filters and pagination.",
            BuildRegion("header", "header", "header", 0, "Search"),
            BuildRegion("query", "search", ".search", 110, "Search permits", "Keyword suggestions", actions:
            [
                new ExtractedAction { Label = "Building permit", Url = "https://example.com/search?q=building" },
                new ExtractedAction { Label = "Parking permit", Url = "https://example.com/search?q=parking" },
            ]),
            BuildRegion("filters", "filters", ".filters", 230, "Filter results", "Type Date Department"),
            BuildRegion("results", "results", ".results", 360, "120 results", "Result snippets", items:
            [
                new ExtractedItem { Title = "Building permit guide", Body = "Permit application result.", Url = "https://example.com/results/1" },
                new ExtractedItem { Title = "Parking permit service", Body = "Parking permit result.", Url = "https://example.com/results/2" },
            ]),
            BuildRegion("pager", "pagination", ".pager", 760, "Pages", "1 2 Next", actions:
            [
                new ExtractedAction { Label = "2", Url = "https://example.com/search?page=2" },
                new ExtractedAction { Label = "Next", Url = "https://example.com/search?page=2" },
            ]),
            BuildRegion("footer", "footer", "footer", 900, "Footer"));
    }

    private static SiteCrawlResult BuildReportDashboardCrawl()
    {
        return BuildPatternCrawl(
            "crawl-report-dashboard",
            "Performance dashboard",
            "Dashboard with filters, KPI metrics, charts, and data table.",
            BuildRegion("header", "header", "header", 0, "Dashboard"),
            BuildRegion("filters", "filter_bar", ".dashboard-filters", 110, "Report filters", "Date range Region Export", actions:
            [
                new ExtractedAction { Label = "Export CSV", Url = "https://example.com/report.csv" },
            ]),
            BuildRegion("metrics", "stats", ".metrics", 220, "Key metrics", "Total 1200 Growth 12% Active 98"),
            BuildRegion("chart", "chart", ".chart", 380, "Monthly trend", "Jan 20 Feb 32 Mar 41"),
            BuildRegion("table", "table", ".data-table", 620, "Detailed rows", "Department Count Status", items:
            [
                new ExtractedItem { Title = "Admissions", Body = "120 Complete", Url = "https://example.com/report/admissions" },
                new ExtractedItem { Title = "Library", Body = "80 Pending", Url = "https://example.com/report/library" },
            ]),
            BuildRegion("footer", "footer", "footer", 920, "Footer"));
    }

    private static SiteCrawlResult BuildInputFlowCrawl()
    {
        return BuildPatternCrawl(
            "crawl-input-flow",
            "Application form",
            "Step 1 applicant information required fields continue submit.",
            BuildRegion("header", "header", "header", 0, "Application"),
            BuildRegion("steps", "steps", ".steps", 110, "Step 1 of 3", "Applicant Information Review Submit"),
            BuildRegion("form", "form", ".application-form", 220, "Applicant information", "Name Email Phone Required"),
            BuildRegion("validation", "validation", ".validation", 560, "Required fields", "Name and email are required."),
            BuildRegion("actions", "action_bar", ".actions", 680, "Actions", "Back Continue Submit", actions:
            [
                new ExtractedAction { Label = "Back", Url = "https://example.com/apply?step=0", Kind = "secondary" },
                new ExtractedAction { Label = "Continue", Url = "https://example.com/apply?step=2", Kind = "primary" },
            ]),
            BuildRegion("footer", "footer", "footer", 900, "Footer"));
    }

    private static SiteCrawlResult BuildCommercialShowcaseCrawl()
    {
        return BuildPatternCrawl(
            "crawl-commercial-showcase",
            "Product showcase",
            "Product showcase pricing plans testimonials start free contact sales.",
            BuildRegion("header", "header", "header", 0, "Product"),
            BuildRegion("hero", "product_hero", ".hero", 110, "Build faster", "Launch your product with a modern platform.", media:
            [
                new ExtractedMedia { Url = "https://example.com/product.jpg", Alt = "Product" },
            ], actions:
            [
                new ExtractedAction { Label = "Start free", Url = "https://example.com/signup", Kind = "primary" },
                new ExtractedAction { Label = "Contact sales", Url = "https://example.com/contact", Kind = "secondary" },
            ]),
            BuildRegion("products", "products", ".products", 480, "Products", "Plan Feature Offer", items:
            [
                new ExtractedItem { Title = "Starter", Body = "For small teams.", Url = "https://example.com/starter", MediaUrl = "https://example.com/starter.jpg", MediaAlt = "Starter" },
                new ExtractedItem { Title = "Scale", Body = "For growing teams.", Url = "https://example.com/scale", MediaUrl = "https://example.com/scale.jpg", MediaAlt = "Scale" },
            ]),
            BuildRegion("proof", "proof", ".proof", 760, "Trusted by teams", "500 customers 99.9 uptime"),
            BuildRegion("pricing", "pricing", ".pricing", 900, "Pricing", "$19 Starter $49 Pro", items:
            [
                new ExtractedItem { Title = "Starter", Body = "$19 per month", Url = "https://example.com/pricing/starter" },
                new ExtractedItem { Title = "Pro", Body = "$49 per month", Url = "https://example.com/pricing/pro" },
            ]),
            BuildRegion("cta", "cta", ".cta", 1160, "Ready to start?", "Start free today.", actions:
            [
                new ExtractedAction { Label = "Start free", Url = "https://example.com/signup", Kind = "primary" },
            ]),
            BuildRegion("footer", "footer", "footer", 1340, "Footer"));
    }

    private static SiteCrawlResult BuildPatternCrawl(string crawlRunId, string title, string excerpt, params VisualRegion[] regions)
    {
        return new SiteCrawlResult
        {
            CrawlRunId = crawlRunId,
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
                    Title = title,
                    TextExcerpt = excerpt,
                    VisualSnapshot = new VisualPageSnapshot
                    {
                        Regions = regions.ToList(),
                    },
                },
            ],
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
