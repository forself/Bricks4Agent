using System.Text.Json;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

/// <summary>
/// Stage 1 (ComponentLibraryConsolidation): the generator vocabulary is anchored to B's closed set.
/// Every manifest component binds to a real ui_components class; out-of-set or missing bindings are
/// rejected fail-closed; the matcher never falls back to an arbitrary or fabricated component.
/// </summary>
public class BComponentBindingTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-binding-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DefaultManifest_EveryComponentBindsToTheBClosedSet()
    {
        var manifest = DefaultComponentLibrary.Create();

        manifest.Components.Should().NotBeEmpty();
        manifest.Components.Should().OnlyContain(component => BComponentRegistry.Contains(component.BComponent));
    }

    [Fact]
    public void DefaultManifest_DropsLegacyHeroSectionJargon()
    {
        var manifest = DefaultComponentLibrary.Create();

        manifest.Components.Should().NotContain(component => component.Type == "HeroSection");
    }

    [Fact]
    public void Load_WhenBindingIsOutsideTheBClosedSet_ThrowsFailClosed()
    {
        var path = WriteManifestWithBinding("NotARealBComponent");
        var loader = new ComponentLibraryLoader();

        var act = () => loader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not in the ui_components closed set*");
    }

    [Fact]
    public void Load_WhenBindingIsMissing_ThrowsFailClosed()
    {
        var path = WriteManifestWithBinding(string.Empty);
        var loader = new ComponentLibraryLoader();

        var act = () => loader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing b_component*");
    }

    [Fact]
    public void Match_WhenNoAcceptedComponentExists_FallsBackToNeutralContainerAndRecordsGap()
    {
        // Strip every component the "news" slot could accept, forcing the fail-closed path.
        var manifest = DefaultComponentLibrary.Create();
        manifest.Components.RemoveAll(component =>
            component.Type is "NewsCardCarousel" or "NewsGrid" or "CardGrid"
                or "ArticleList" or "MediaFeatureGrid" or "TabbedNewsBoard");
        var crawl = BuildHomeWithNews();
        var intent = new SiteIntentExtractor().Extract(crawl);

        var plan = new TemplateMatcher(new TemplateFrameworkLoader().LoadDefault(), manifest).Match(intent);

        // Gap is flagged, never silently filled.
        plan.ComponentRequests.Should().NotBeEmpty();
        // Every chosen component stays inside the loaded manifest closed set — no arbitrary pick,
        // no fabricated/Generated type, no empty slot.
        var knownTypes = manifest.Components.Select(component => component.Type).ToHashSet(StringComparer.Ordinal);
        plan.Pages.SelectMany(page => page.Slots)
            .Should().OnlyContain(slot =>
                !string.IsNullOrWhiteSpace(slot.ComponentType) && knownTypes.Contains(slot.ComponentType));
        // The neutral container (AtomicSection) absorbs the unmatched news slot.
        plan.Pages.SelectMany(page => page.Slots)
            .Should().Contain(slot => slot.ComponentType == "AtomicSection");
    }

    private string WriteManifestWithBinding(string binding)
    {
        Directory.CreateDirectory(tempRoot);
        var manifest = DefaultComponentLibrary.Create();
        manifest.Components[1].BComponent = binding;
        var path = Path.Combine(tempRoot, "manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }

    private static SiteCrawlResult BuildHomeWithNews()
    {
        return new SiteCrawlResult
        {
            CrawlRunId = "crawl-binding",
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
                    Title = "Example",
                    TextExcerpt = "Example.",
                    VisualSnapshot = new VisualPageSnapshot
                    {
                        Regions =
                        [
                            new VisualRegion { Id = "header", Role = "header", Selector = "header" },
                            new VisualRegion
                            {
                                Id = "news",
                                Role = "news",
                                Selector = ".news",
                                Headline = "Latest News",
                                Text = "2026-05-01 Item",
                                Items =
                                [
                                    new ExtractedItem { Title = "Item", Body = "2026-05-01", Url = "https://example.edu/news/1" },
                                ],
                            },
                            new VisualRegion { Id = "footer", Role = "footer", Selector = "footer", Text = "Footer" },
                        ],
                    },
                },
            ],
        };
    }
}
