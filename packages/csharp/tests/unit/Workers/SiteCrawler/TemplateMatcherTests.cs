using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class TemplateMatcherTests
{
    [Fact]
    public void Match_MapsUniversityHomeIntentToInstitutionalSlots()
    {
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var manifest = BuildManifestWithHighLevelComponents();
        var intent = BuildHomeIntent();
        var matcher = new TemplateMatcher(templates, manifest);

        var plan = matcher.Match(intent);

        plan.TemplateId.Should().Be("institutional_site");
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

    private static SiteIntentModel BuildHomeIntent()
    {
        return new SiteIntentModel
        {
            SiteKind = "university",
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
