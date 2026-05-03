using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class TemplateMatcherTests
{
    [Fact]
    public void Match_MapsHeroNewsHomeIntentToVisualPatternSlots()
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = BuildManifestWithHighLevelComponents();
        var intent = BuildHomeIntent();
        var matcher = new TemplateMatcher(templates, manifest);

        var plan = matcher.Match(intent);

        plan.TemplateId.Should().Be("hero_news_portal");
        plan.ComponentRequests.Should().BeEmpty();
        var home = plan.Pages.Should().ContainSingle(page => page.PageType == "home").Subject;
        home.Slots.Select(slot => slot.SlotName).Should().Contain(["header", "hero", "quick_links", "news", "footer"]);
        home.Slots.Single(slot => slot.SlotName == "header").ComponentType.Should().Be("MegaHeader");
        home.Slots.Single(slot => slot.SlotName == "hero").ComponentType.Should().Be("HeroCarousel");
        home.Slots.Single(slot => slot.SlotName == "quick_links").ComponentType.Should().Be("QuickLinkRibbon");
        home.Slots.Single(slot => slot.SlotName == "news").ComponentType.Should().Be("NewsCardCarousel");
        home.Slots.Single(slot => slot.SlotName == "footer").ComponentType.Should().Be("InstitutionFooter");
    }

    [Fact]
    public void Match_WhenPreferredComponentIsMissing_UsesDeclaredFallbackAndRecordsRequest()
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = DefaultComponentLibrary.Create();
        manifest.Components.RemoveAll(component => component.Type == "HeroCarousel");
        if (manifest.Components.All(component => component.Type != "HeroBanner"))
        {
            manifest.Components.Add(DefineMinimal("HeroBanner"));
        }
        var intent = BuildHomeIntent();
        var matcher = new TemplateMatcher(templates, manifest);

        var plan = matcher.Match(intent);

        var hero = plan.Pages[0].Slots.Single(slot => slot.SlotName == "hero");
        hero.ComponentType.Should().Be("HeroBanner");
        plan.ComponentRequests.Should().ContainSingle(request =>
            request.ComponentType == "HeroCarousel" &&
            request.Role == "hero" &&
            request.Reason.Contains("preferred_component_missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_MapsSearchServicePatternToPortalTemplate()
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentModel
        {
            SiteKind = "unknown",
            Pages =
            [
                new SiteIntentPage
                {
                    PageUrl = "https://www.gov.tw/",
                    PageType = "home",
                    Depth = 0,
                    Title = "Government Service Portal",
                    Blocks =
                    [
                        BuildBlock("header", "header", "Header"),
                        BuildBlock("search_hero", "search", "How can we help?"),
                        BuildBlock("service_category_grid", "service_categories", "Citizen Services"),
                        BuildBlock("tabbed_news", "tabbed_news", "Announcements"),
                        BuildBlock("footer", "footer", "Footer"),
                    ],
                },
            ],
        };
        var matcher = new TemplateMatcher(templates, manifest);

        var plan = matcher.Match(intent);

        plan.TemplateId.Should().Be("search_service_portal");
        var home = plan.Pages.Should().ContainSingle(page => page.PageType == "home").Subject;
        home.Slots.Single(slot => slot.SlotName == "search").ComponentType.Should().Be("ServiceSearchHero");
        home.Slots.Single(slot => slot.SlotName == "service_categories").ComponentType.Should().Be("ServiceCategoryGrid");
        home.Slots.Single(slot => slot.SlotName == "tabbed_news").ComponentType.Should().Be("TabbedNewsBoard");
        home.Slots.Single(slot => slot.SlotName == "footer").ComponentType.Should().Be("InstitutionFooter");
        plan.ComponentRequests.Should().BeEmpty();
    }

    [Fact]
    public void Match_WhenSiteKindContradictsSearchServiceBlocks_PrefersVisualPattern()
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentModel
        {
            SiteKind = "university",
            Pages =
            [
                new SiteIntentPage
                {
                    PageUrl = "https://agency.gov.tw/",
                    PageType = "home",
                    Depth = 0,
                    Title = "Agency",
                    Blocks =
                    [
                        BuildBlock("header", "header", "Header"),
                        BuildBlock("search_hero", "search", "Search Services"),
                        BuildBlock("service_category_grid", "service_categories", "Services"),
                        BuildBlock("footer", "footer", "Footer"),
                    ],
                },
            ],
        };
        var matcher = new TemplateMatcher(templates, manifest);

        var plan = matcher.Match(intent);

        plan.TemplateId.Should().Be("search_service_portal");
        plan.Pages[0].Slots.Select(slot => slot.SlotName).Should().Contain(["search", "service_categories"]);
    }

    [Fact]
    public void Match_MapsServiceActionPatternToActionPortalTemplate()
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentModel
        {
            SiteKind = "unknown",
            Pages =
            [
                new SiteIntentPage
                {
                    PageUrl = "https://www.cgh.org.tw/",
                    PageType = "home",
                    Depth = 0,
                    Title = "Example Hospital",
                    Blocks =
                    [
                        BuildBlock("header", "header", "Header"),
                        BuildBlock("hero_carousel", "hero", "Patient-centered care"),
                        BuildBlock("service_action_grid", "service_actions", "Medical Services"),
                        BuildBlock("tabbed_news", "tabbed_news", "Latest News"),
                        BuildBlock("footer", "footer", "Footer"),
                    ],
                },
            ],
        };
        var matcher = new TemplateMatcher(templates, manifest);

        var plan = matcher.Match(intent);

        plan.TemplateId.Should().Be("service_action_portal");
        var home = plan.Pages.Should().ContainSingle(page => page.PageType == "home").Subject;
        home.Slots.Single(slot => slot.SlotName == "service_actions").ComponentType.Should().Be("ServiceActionGrid");
        home.Slots.Single(slot => slot.SlotName == "tabbed_news").ComponentType.Should().Be("TabbedNewsBoard");
        home.Slots.Single(slot => slot.SlotName == "hero").ComponentType.Should().Be("HeroCarousel");
    }

    [Fact]
    public void Match_MapsSearchResultsPatternBySlots()
    {
        var plan = MatchSinglePageIntent(
            siteKind: "commercial",
            BuildBlock("search_box", "search_box", "Search"),
            BuildBlock("filter_panel", "filter_panel", "Filters"),
            BuildBlock("result_list", "result_list", "Results"),
            BuildBlock("pagination", "pagination", "Pages"));

        plan.TemplateId.Should().Be("search_results_portal");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "search_box").ComponentType.Should().Be("SearchBoxPanel");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "filter_panel").ComponentType.Should().Be("FacetFilterPanel");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "result_list").ComponentType.Should().Be("ResultList");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "pagination").ComponentType.Should().Be("PaginationNav");
    }

    [Fact]
    public void Match_MapsReportDashboardPatternBySlots()
    {
        var plan = MatchSinglePageIntent(
            siteKind: "university",
            BuildBlock("filter_bar", "filter_bar", "Filters"),
            BuildBlock("metric_summary", "metric_summary", "Metrics"),
            BuildBlock("chart_panel", "chart_panel", "Chart"),
            BuildBlock("data_table", "data_table", "Data"));

        plan.TemplateId.Should().Be("report_dashboard");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "filter_bar").ComponentType.Should().Be("DashboardFilterBar");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "metric_summary").ComponentType.Should().Be("MetricSummaryGrid");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "chart_panel").ComponentType.Should().Be("ChartPanel");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "data_table").ComponentType.Should().Be("DataTablePreview");
    }

    [Fact]
    public void Match_MapsInputFlowPatternBySlots()
    {
        var plan = MatchSinglePageIntent(
            siteKind: "public_service",
            BuildBlock("step_indicator", "step_indicator", "Steps"),
            BuildBlock("form_fields", "form_fields", "Form"),
            BuildBlock("validation_summary", "validation_summary", "Required"),
            BuildBlock("action_bar", "action_bar", "Actions"));

        plan.TemplateId.Should().Be("input_flow");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "step_indicator").ComponentType.Should().Be("StepIndicator");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "form_fields").ComponentType.Should().Be("StructuredFormPanel");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "validation_summary").ComponentType.Should().Be("ValidationSummary");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "action_bar").ComponentType.Should().Be("FormActionBar");
    }

    [Fact]
    public void Match_MapsCommercialShowcasePatternBySlots()
    {
        var plan = MatchSinglePageIntent(
            siteKind: "healthcare",
            BuildBlock("showcase_hero", "showcase_hero", "Hero"),
            BuildBlock("product_cards", "product_cards", "Products"),
            BuildBlock("proof_strip", "proof_strip", "Proof"),
            BuildBlock("pricing_panel", "pricing_panel", "Pricing"),
            BuildBlock("cta_band", "cta_band", "CTA"));

        plan.TemplateId.Should().Be("commercial_showcase");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "showcase_hero").ComponentType.Should().Be("ShowcaseHero");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "product_cards").ComponentType.Should().Be("ProductCardGrid");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "proof_strip").ComponentType.Should().Be("ProofStrip");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "pricing_panel").ComponentType.Should().Be("PricingPanel");
        plan.Pages[0].Slots.Single(slot => slot.SlotName == "cta_band").ComponentType.Should().Be("CtaBand");
    }

    private static SiteIntentModel BuildHomeIntent()
    {
        return new SiteIntentModel
        {
            SiteKind = "unknown",
            Pages =
            [
                new SiteIntentPage
                {
                    PageUrl = "https://example.edu/",
                    PageType = "home",
                    Depth = 0,
                    Title = "Example University",
                    Blocks =
                    [
                        BuildBlock("header", "header", "Header"),
                        BuildBlock("hero_carousel", "hero", "Campus Life"),
                        BuildBlock("quick_links", "quick_links", "Quick Links"),
                        BuildBlock("news_carousel", "news", "Latest News"),
                        BuildBlock("footer", "footer", "Footer"),
                    ],
                },
            ],
        };
    }

    private static TemplatePlan MatchSinglePageIntent(string siteKind, params SiteIntentBlock[] blocks)
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = DefaultComponentLibrary.Create();
        var intent = new SiteIntentModel
        {
            SiteKind = siteKind,
            Pages =
            [
                new SiteIntentPage
                {
                    PageUrl = "https://example.com/",
                    PageType = "home",
                    Depth = 0,
                    Title = "Example",
                    Blocks = [BuildBlock("header", "header", "Header"), .. blocks, BuildBlock("footer", "footer", "Footer")],
                },
            ],
        };

        return new TemplateMatcher(templates, manifest).Match(intent);
    }

    private static SiteIntentBlock BuildBlock(string kind, string slot, string headline)
    {
        return new SiteIntentBlock
        {
            Id = kind,
            Kind = kind,
            Slot = slot,
            Confidence = 0.8,
            Reasons = [$"test:{kind}"],
            Section = new ExtractedSection
            {
                Id = kind,
                Role = kind,
                Headline = headline,
                Body = $"{headline} body",
                SourceSelector = $".{kind}",
            },
        };
    }

    private static ComponentLibraryManifest BuildManifestWithHighLevelComponents()
    {
        var manifest = DefaultComponentLibrary.Create();
        foreach (var type in new[]
        {
            "MegaHeader",
            "HeroCarousel",
            "HeroBanner",
            "QuickLinkRibbon",
            "NewsCardCarousel",
            "NewsGrid",
            "MediaFeatureGrid",
            "InstitutionFooter",
            "ArticleList",
            "ContentArticle",
        })
        {
            if (manifest.Components.All(component => component.Type != type))
            {
                manifest.Components.Add(DefineMinimal(type));
            }
        }

        return manifest;
    }

    private static ComponentDefinition DefineMinimal(string type)
    {
        return DefaultComponentLibrary.Define(
            type,
            $"Minimal {type} definition for matcher tests.",
            [type],
            new ComponentPropsSchema
            {
                Required = ["title"],
                Properties = { ["title"] = new ComponentPropSchema { Type = "string" } },
            });
    }
}
