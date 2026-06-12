using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class TemplateCompiler
{
    private readonly ComponentLibraryManifest baseManifest;

    public TemplateCompiler(ComponentLibraryManifest componentLibrary)
    {
        baseManifest = CloneManifest(componentLibrary);
    }

    public GeneratorSiteDocument Compile(SiteCrawlResult crawl, SiteIntentModel intent, TemplatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(crawl);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(plan);

        var manifest = CloneManifest(baseManifest);
        var localRoutes = BuildLocalRouteMap(crawl.Pages, crawl.Redirects, crawl.Root.Origin);
        var document = new GeneratorSiteDocument
        {
            Site = new GeneratorSiteMetadata
            {
                Title = ResolveSiteTitle(crawl),
                SourceUrl = crawl.Root.NormalizedStartUrl,
                CrawlRunId = crawl.CrawlRunId,
                Theme = new GeneratorTheme
                {
                    Colors = new Dictionary<string, string>(crawl.ExtractedModel.ThemeTokens.Colors, StringComparer.Ordinal),
                    Typography = new Dictionary<string, string>(crawl.ExtractedModel.ThemeTokens.Typography, StringComparer.Ordinal),
                },
            },
            ComponentLibrary = manifest,
            ComponentRequests = plan.ComponentRequests.Select(CloneComponentRequest).ToList(),
        };

        foreach (var page in crawl.Pages)
        {
            var intentPage = intent.Pages.FirstOrDefault(candidate =>
                string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
            var pagePlan = plan.Pages.FirstOrDefault(candidate =>
                string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
            document.Routes.Add(BuildRoute(crawl, document, page, intent, intentPage, pagePlan, localRoutes));
        }

        return document;
    }

    private static GeneratorRoute BuildRoute(
        SiteCrawlResult crawl,
        GeneratorSiteDocument document,
        SiteCrawlPage page,
        SiteIntentModel intent,
        SiteIntentPage? intentPage,
        TemplatePagePlan? pagePlan,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var pageTitle = string.IsNullOrWhiteSpace(page.Title) ? document.Site.Title : page.Title;
        var root = new ComponentNode
        {
            Id = BuildNodeId("page", page.FinalUrl),
            Type = "PageShell",
            Props =
            {
                ["title"] = pageTitle,
                ["source_url"] = page.FinalUrl,
            },
        };

        if (pagePlan is null || pagePlan.Slots.Count == 0)
        {
            root.Children.Add(BuildHeaderNode("MegaHeader", document.Site.Title, intent.GlobalHeader, crawl.Root.Origin, localRoutes, page.FinalUrl));
            AddDefaultContent(root, page, crawl.Root.Origin);
            root.Children.Add(BuildFooterNode("InstitutionFooter", page.FinalUrl, intent.GlobalFooter, crawl.Root.Origin, localRoutes));
        }
        else
        {
            foreach (var slot in pagePlan.Slots)
            {
                root.Children.Add(BuildSlotNode(slot, document.Site.Title, page, intent, crawl.Root.Origin, localRoutes));
            }
        }

        foreach (var form in page.Forms.Where(ShouldRenderForm).Take(4))
        {
            root.Children.Add(BuildFormNode(page, form));
        }

        return new GeneratorRoute
        {
            Path = BuildRoutePath(page.FinalUrl, crawl.Root.Origin),
            Title = pageTitle,
            SourceUrl = page.FinalUrl,
            Root = root,
        };
    }

    private static ComponentNode BuildSlotNode(
        TemplateSlotPlan slot,
        string siteTitle,
        SiteCrawlPage page,
        SiteIntentModel intent,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return slot.SlotName switch
        {
            "header" => BuildHeaderNode(slot.ComponentType, siteTitle, intent.GlobalHeader, origin, localRoutes, page.FinalUrl),
            "footer" => BuildFooterNode(slot.ComponentType, page.FinalUrl, intent.GlobalFooter, origin, localRoutes),
            _ => BuildContentSlotNode(slot, page, origin, localRoutes),
        };
    }

    private static ComponentNode BuildHeaderNode(
        string componentType,
        string siteTitle,
        ExtractedHeader header,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes,
        string pageUrl)
    {
        if (componentType == "SiteHeader")
        {
            return new ComponentNode
            {
                Id = BuildNodeId("header", pageUrl),
                Type = "SiteHeader",
                Props =
                {
                    ["title"] = siteTitle,
                    ["logo_url"] = header.LogoUrl,
                    ["logo_alt"] = header.LogoAlt,
                    ["utility_links"] = BuildLinks(header.UtilityLinks, origin, localRoutes, 10),
                    ["primary_links"] = BuildLinks(header.PrimaryLinks, origin, localRoutes, 16),
                    ["links"] = new List<Dictionary<string, string>>(),
                },
            };
        }

        return new ComponentNode
        {
            Id = BuildNodeId("header", pageUrl),
            Type = "MegaHeader",
            Props =
            {
                ["title"] = siteTitle,
                ["logo_url"] = header.LogoUrl,
                ["logo_alt"] = header.LogoAlt,
                ["utility_links"] = BuildLinks(header.UtilityLinks, origin, localRoutes, 10),
                ["primary_links"] = BuildLinks(header.PrimaryLinks, origin, localRoutes, 16),
                ["search_enabled"] = true,
            },
        };
    }

    private static ComponentNode BuildFooterNode(
        string componentType,
        string pageUrl,
        ExtractedFooter footer,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (componentType == "SiteFooter")
        {
            return new ComponentNode
            {
                Id = BuildNodeId("footer", pageUrl),
                Type = "SiteFooter",
                Props =
                {
                    ["source_url"] = pageUrl,
                    ["notice"] = "Reconstructed from public visual structure using the declared component library.",
                    ["logo_url"] = footer.LogoUrl,
                    ["logo_alt"] = footer.LogoAlt,
                    ["contact_text"] = footer.Text,
                    ["links"] = BuildLinks(footer.Links, origin, localRoutes, 16),
                },
            };
        }

        return new ComponentNode
        {
            Id = BuildNodeId("footer", pageUrl),
            Type = "InstitutionFooter",
            Props =
            {
                ["source_url"] = pageUrl,
                ["logo_url"] = footer.LogoUrl,
                ["logo_alt"] = footer.LogoAlt,
                ["contact_text"] = footer.Text,
                ["links"] = BuildLinks(footer.Links, origin, localRoutes, 16),
            },
        };
    }

    private static ComponentNode BuildContentSlotNode(
        TemplateSlotPlan slot,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var section = slot.Block?.Section ?? new ExtractedSection
        {
            Id = slot.SlotName,
            Headline = string.IsNullOrWhiteSpace(page.Title) ? slot.SlotName : page.Title,
            Body = page.TextExcerpt,
            SourceSelector = $"route:{BuildRoutePath(page.FinalUrl, origin)}",
        };

        return slot.ComponentType switch
        {
            "HeroCarousel" => BuildHeroCarouselNode(section, page, origin, localRoutes),
            "HeroBanner" => BuildHeroBannerNode(section, page),
            "QuickLinkRibbon" => BuildQuickLinkRibbonNode(section, origin, localRoutes),
            "ServiceSearchHero" => BuildServiceSearchHeroNode(section, page, origin, localRoutes),
            "ServiceCategoryGrid" => BuildServiceCategoryGridNode(section, page, origin, localRoutes),
            "ServiceActionGrid" => BuildServiceActionGridNode(section, page, origin, localRoutes),
            "TabbedNewsBoard" => BuildTabbedNewsBoardNode(section, page, origin, localRoutes),
            "SearchBoxPanel" => BuildSearchBoxPanelNode(section, page, origin, localRoutes),
            "FacetFilterPanel" => BuildFacetFilterPanelNode(section, page),
            "ResultList" => BuildResultListNode(section, page, origin, localRoutes),
            "PaginationNav" => BuildPaginationNavNode(section, origin, localRoutes),
            "DashboardFilterBar" => BuildDashboardFilterBarNode(section, page, origin, localRoutes),
            "MetricSummaryGrid" => BuildMetricSummaryGridNode(section, page),
            "ChartPanel" => BuildChartPanelNode(section, page),
            "DataTablePreview" => BuildDataTablePreviewNode(section, page),
            "StepIndicator" => BuildStepIndicatorNode(section, page),
            "StructuredFormPanel" => BuildStructuredFormPanelNode(section, page),
            "ValidationSummary" => BuildValidationSummaryNode(section, page),
            "FormActionBar" => BuildFormActionBarNode(section, origin, localRoutes),
            "ShowcaseHero" => BuildShowcaseHeroNode(section, page, origin, localRoutes),
            "ProductCardGrid" => BuildProductCardGridNode(section, page, origin, localRoutes),
            "ProofStrip" => BuildProofStripNode(section, page),
            "PricingPanel" => BuildPricingPanelNode(section, page, origin, localRoutes),
            "CtaBand" => BuildCtaBandNode(section, page, origin, localRoutes),
            "NewsCardCarousel" => BuildItemCollectionNode("NewsCardCarousel", section, page, origin, localRoutes),
            "NewsGrid" => BuildItemCollectionNode("NewsGrid", section, page, origin, localRoutes),
            "MediaFeatureGrid" => BuildItemCollectionNode("MediaFeatureGrid", section, page, origin, localRoutes),
            "ArticleList" => BuildItemCollectionNode("ArticleList", section, page, origin, localRoutes),
            "ContentArticle" => BuildContentArticleNode(section, page),
            "CardGrid" => BuildCardGridNode(section, page, origin, localRoutes),
            "ContentSection" => BuildContentSectionNode(section, page),
            "AtomicSection" => BuildAtomicSectionNode(section, page, origin, localRoutes),
            _ => BuildAtomicSectionNode(section, page, origin, localRoutes),
        };
    }

    private static ComponentNode BuildHeroCarouselNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var slides = BuildSlides(section, origin, localRoutes);
        if (slides.Count == 0)
        {
            slides.Add(new Dictionary<string, string>
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["media_url"] = section.Media.FirstOrDefault()?.Url ?? string.Empty,
                ["media_alt"] = section.Media.FirstOrDefault()?.Alt ?? string.Empty,
                ["url"] = string.Empty,
                ["source_url"] = string.Empty,
                ["scope"] = "none",
            });
        }

        return new ComponentNode
        {
            Id = BuildNodeId("hero-carousel", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "HeroCarousel",
            Props =
            {
                ["title"] = CleanDisplayText(section.Headline),
                ["body"] = CleanDisplayText(section.Body),
                ["slides"] = slides,
            },
        };
    }

    private static ComponentNode BuildHeroBannerNode(ExtractedSection section, SiteCrawlPage page)
    {
        var media = section.Media.FirstOrDefault();
        return new ComponentNode
        {
            Id = BuildNodeId("hero-banner", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "HeroBanner",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["media_url"] = media?.Url ?? string.Empty,
                ["media_alt"] = media?.Alt ?? string.Empty,
            },
        };
    }

    private static ComponentNode BuildQuickLinkRibbonNode(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("quick-links", section.SourceSelector),
            Type = "QuickLinkRibbon",
            Props =
            {
                ["title"] = section.Headline,
                ["links"] = BuildLinks(section.Actions, origin, localRoutes, 12),
            },
        };
    }

    private static ComponentNode BuildItemCollectionNode(
        string componentType,
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId(componentType, $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = componentType,
            Props =
            {
                ["title"] = section.Headline,
                ["items"] = BuildItems(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildServiceSearchHeroNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("service-search", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ServiceSearchHero",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? page.Title : section.Headline,
                ["body"] = section.Body,
                ["query_placeholder"] = "Search services",
                ["hot_keywords"] = BuildLinks(section.Actions, origin, localRoutes, 8),
                ["actions"] = BuildLinks(section.Actions, origin, localRoutes, 6),
            },
        };
    }

    private static ComponentNode BuildServiceCategoryGridNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("service-categories", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ServiceCategoryGrid",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Services" : section.Headline,
                ["categories"] = BuildServiceCategories(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildServiceActionGridNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("service-actions", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ServiceActionGrid",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Services" : section.Headline,
                ["actions"] = BuildActionLinks(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildTabbedNewsBoardNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var tabLabels = ExtractTabLabels(section).ToList();
        var items = BuildItems(section, origin, localRoutes);
        if (tabLabels.Count == 0)
        {
            tabLabels.Add(string.IsNullOrWhiteSpace(section.Headline) ? "Latest" : section.Headline);
        }

        var labelSet = tabLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contentItems = items
            .Where(item => !IsTabPlaceholderItem(item, labelSet))
            .ToList();
        if (contentItems.Count == 0)
        {
            contentItems = items;
        }

        return new ComponentNode
        {
            Id = BuildNodeId("tabbed-news", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "TabbedNewsBoard",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "News" : section.Headline,
                ["tabs"] = tabLabels
                    .Select((label, index) => new Dictionary<string, object?>
                    {
                        ["label"] = label,
                        ["items"] = index == 0 ? contentItems : new List<Dictionary<string, string>>(),
                    })
                    .ToList(),
            },
        };
    }

    private static IEnumerable<string> ExtractTabLabels(ExtractedSection section)
    {
        return section.Actions
            .Select(action => CleanDisplayText(action.Label))
            .Concat(section.Items
                .Where(item => string.IsNullOrWhiteSpace(item.Body) && string.IsNullOrWhiteSpace(item.MediaUrl))
                .Select(item => CleanDisplayText(item.Title)))
            .Where(IsTabLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8);
    }

    private static bool IsTabPlaceholderItem(Dictionary<string, string> item, IReadOnlySet<string> tabLabels)
    {
        return item.TryGetValue("title", out var title) &&
            tabLabels.Contains(title) &&
            (!item.TryGetValue("body", out var body) || string.IsNullOrWhiteSpace(body)) &&
            (!item.TryGetValue("media_url", out var mediaUrl) || string.IsNullOrWhiteSpace(mediaUrl));
    }

    private static ComponentNode BuildSearchBoxPanelNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("search-box", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "SearchBoxPanel",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Search" : section.Headline,
                ["body"] = section.Body,
                ["query_placeholder"] = "Search keyword",
                ["suggestions"] = BuildLinks(section.Actions, origin, localRoutes, 8),
            },
        };
    }

    private static ComponentNode BuildFacetFilterPanelNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("filters", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "FacetFilterPanel",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Filters" : section.Headline,
                ["filters"] = BuildFilterOptions(section),
            },
        };
    }

    private static ComponentNode BuildResultListNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("results", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ResultList",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Results" : section.Headline,
                ["summary"] = string.IsNullOrWhiteSpace(section.Body) ? page.TextExcerpt : section.Body,
                ["items"] = BuildItems(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildPaginationNavNode(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("pagination", section.SourceSelector),
            Type = "PaginationNav",
            Props =
            {
                ["links"] = BuildLinks(section.Actions, origin, localRoutes, 12),
            },
        };
    }

    private static ComponentNode BuildDashboardFilterBarNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("dashboard-filter", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "DashboardFilterBar",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Report filters" : section.Headline,
                ["filters"] = BuildFilterOptions(section),
                ["actions"] = BuildLinks(section.Actions, origin, localRoutes, 8),
            },
        };
    }

    private static ComponentNode BuildMetricSummaryGridNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("metrics", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "MetricSummaryGrid",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Metrics" : section.Headline,
                ["metrics"] = BuildMetricItems(section),
            },
        };
    }

    private static ComponentNode BuildChartPanelNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("chart", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ChartPanel",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Chart" : section.Headline,
                ["body"] = section.Body,
                ["series"] = BuildSeries(section),
            },
        };
    }

    private static ComponentNode BuildDataTablePreviewNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("data-table", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "DataTablePreview",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Data" : section.Headline,
                ["columns"] = new List<string> { "Item", "Details" },
                ["rows"] = BuildTableRows(section),
            },
        };
    }

    private static ComponentNode BuildStepIndicatorNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("steps", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "StepIndicator",
            Props =
            {
                ["steps"] = BuildSteps(section),
            },
        };
    }

    private static ComponentNode BuildStructuredFormPanelNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("structured-form", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "StructuredFormPanel",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Form" : section.Headline,
                ["fields"] = BuildFields(section, page),
            },
        };
    }

    private static ComponentNode BuildValidationSummaryNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("validation", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ValidationSummary",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Validation" : section.Headline,
                ["messages"] = BuildMessages(section),
            },
        };
    }

    private static ComponentNode BuildFormActionBarNode(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("form-actions", section.SourceSelector),
            Type = "FormActionBar",
            Props =
            {
                ["actions"] = BuildActionLinks(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildShowcaseHeroNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var media = section.Media.FirstOrDefault();
        return new ComponentNode
        {
            Id = BuildNodeId("showcase-hero", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ShowcaseHero",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? page.Title : section.Headline,
                ["body"] = section.Body,
                ["media_url"] = media?.Url ?? string.Empty,
                ["media_alt"] = media?.Alt ?? string.Empty,
                ["actions"] = BuildActionLinks(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildProductCardGridNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("product-cards", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ProductCardGrid",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Products" : section.Headline,
                ["items"] = BuildItems(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildProofStripNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("proof", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ProofStrip",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Proof" : section.Headline,
                ["items"] = BuildMetricItems(section),
            },
        };
    }

    private static ComponentNode BuildPricingPanelNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("pricing", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "PricingPanel",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Pricing" : section.Headline,
                ["plans"] = BuildPricingPlans(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildCtaBandNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("cta", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "CtaBand",
            Props =
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? page.Title : section.Headline,
                ["body"] = section.Body,
                ["actions"] = BuildActionLinks(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildContentArticleNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("content-article", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ContentArticle",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["media"] = section.Media
                    .Take(8)
                    .Select(media => new Dictionary<string, string>
                    {
                        ["url"] = media.Url,
                        ["alt"] = media.Alt,
                    })
                    .ToList(),
            },
        };
    }

    private static ComponentNode BuildContentSectionNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("content", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ContentSection",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["source_selector"] = section.SourceSelector,
            },
        };
    }

    private static ComponentNode BuildCardGridNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var grid = new ComponentNode
        {
            Id = BuildNodeId("grid", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "CardGrid",
            Props =
            {
                ["title"] = section.Headline,
                ["layout"] = section.Role is "news" or "gallery" ? "carousel" : "grid",
            },
        };

        foreach (var item in BuildItems(section, origin, localRoutes))
        {
            grid.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("card", $"{page.FinalUrl}:{section.SourceSelector}:{item["title"]}:{item["url"]}"),
                Type = "FeatureCard",
                Props =
                {
                    ["title"] = item["title"],
                    ["body"] = item["body"],
                    ["url"] = item["url"],
                    ["source_url"] = item["source_url"],
                    ["scope"] = item["scope"],
                    ["media_url"] = item["media_url"],
                    ["media_alt"] = item["media_alt"],
                },
            });
        }

        return grid;
    }

    private static ComponentNode BuildAtomicSectionNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var node = new ComponentNode
        {
            Id = BuildNodeId("atomic", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "AtomicSection",
            Props =
            {
                ["variant"] = section.Role == "hero" ? "hero" : "standard",
                ["source_selector"] = section.SourceSelector,
            },
            Children =
            [
                new ComponentNode
                {
                    Id = BuildNodeId("text", $"{page.FinalUrl}:{section.SourceSelector}:text"),
                    Type = "TextBlock",
                    Props =
                    {
                        ["title"] = section.Headline,
                        ["body"] = section.Body,
                    },
                },
            ],
        };

        foreach (var media in section.Media.Take(2))
        {
            node.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("image", media.Url),
                Type = "ImageBlock",
                Props =
                {
                    ["url"] = media.Url,
                    ["alt"] = media.Alt,
                },
            });
        }

        foreach (var action in section.Actions.Take(4))
        {
            var link = BuildLink(action.Url, origin, localRoutes);
            node.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("action", $"{section.SourceSelector}:{action.Label}:{action.Url}"),
                Type = "ButtonLink",
                Props =
                {
                    ["label"] = action.Label,
                    ["url"] = link["url"],
                    ["source_url"] = link["source_url"],
                    ["scope"] = link["scope"],
                    ["kind"] = action.Kind,
                },
            });
        }

        return node;
    }

    private static ComponentNode BuildFormNode(SiteCrawlPage page, ExtractedForm form)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("form", $"{page.FinalUrl}:{form.Selector}:{form.Action}"),
            Type = "FormBlock",
            Props =
            {
                ["method"] = form.Method,
                ["action"] = form.Action,
                ["fields"] = form.Fields.Select(field => new Dictionary<string, object?>
                {
                    ["name"] = field.Name,
                    ["id"] = field.Id,
                    ["type"] = field.Type,
                    ["label"] = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label,
                    ["required"] = field.Required,
                }).ToList(),
            },
        };
    }

    private static bool ShouldRenderForm(ExtractedForm form)
    {
        if (!IsSearchUtilityForm(form))
        {
            return true;
        }

        return form.Fields.Count(field => !IsHiddenField(field)) > 1;
    }

    private static bool IsSearchUtilityForm(ExtractedForm form)
    {
        var signature = $"{form.Selector} {form.Action}".ToLowerInvariant();
        if (signature.Contains("search", StringComparison.Ordinal) ||
            signature.Contains("ptsearch", StringComparison.Ordinal) ||
            signature.Contains("schkey", StringComparison.Ordinal))
        {
            return true;
        }

        return form.Fields.Any(field =>
            field.Name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
            field.Name.Contains("schkey", StringComparison.OrdinalIgnoreCase) ||
            field.Label.Contains("關鍵字", StringComparison.OrdinalIgnoreCase) ||
            field.Label.Contains("keyword", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHiddenField(ExtractedFormField field)
    {
        return string.Equals(field.Type, "hidden", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddDefaultContent(ComponentNode root, SiteCrawlPage page, string rootOrigin)
    {
        if (string.IsNullOrWhiteSpace(page.TextExcerpt))
        {
            return;
        }

        root.Children.Add(new ComponentNode
        {
            Id = BuildNodeId("content", page.FinalUrl),
            Type = "ContentSection",
            Props =
            {
                ["title"] = page.Title,
                ["body"] = page.TextExcerpt,
                ["source_selector"] = $"route:{BuildRoutePath(page.FinalUrl, rootOrigin)}",
            },
        });
    }

    private static string CleanDisplayText(string value)
    {
        var text = (value ?? string.Empty).Trim();
        return LooksLikeControlText(text) ? string.Empty : text;
    }

    private static bool IsTabLabel(string value)
    {
        var text = (value ?? string.Empty).Trim();
        var normalized = text.ToLowerInvariant();
        return text.Length is >= 2 and <= 24 &&
            !Regex.IsMatch(text, @"\d") &&
            normalized is not ":::" and not "search" and not "more" and not "open" and not "skip" and not "home";
    }

    private static bool LooksLikeControlText(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > 96)
        {
            return false;
        }

        var stripped = Regex.Replace(value, @"[\s.。·•\-–—_:/\\|<>\[\]\(\){}‹›«»]+", string.Empty);
        return stripped.Length == 0;
    }

    private static List<Dictionary<string, string>> BuildSlides(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(8).Select(item =>
            {
                var link = string.IsNullOrWhiteSpace(item.Url) ? EmptyLink() : BuildLink(item.Url, origin, localRoutes);
                return new Dictionary<string, string>
                {
                    ["title"] = CleanDisplayText(item.Title),
                    ["body"] = CleanDisplayText(item.Body),
                    ["media_url"] = item.MediaUrl,
                    ["media_alt"] = CleanDisplayText(item.MediaAlt),
                    ["url"] = link["url"],
                    ["source_url"] = link["source_url"],
                    ["scope"] = link["scope"],
                };
            }).ToList();
        }

        return section.Media.Take(8).Select(media => new Dictionary<string, string>
        {
            ["title"] = CleanDisplayText(string.IsNullOrWhiteSpace(media.Alt) ? section.Headline : media.Alt),
            ["body"] = CleanDisplayText(section.Body),
            ["media_url"] = media.Url,
            ["media_alt"] = CleanDisplayText(media.Alt),
            ["url"] = string.Empty,
            ["source_url"] = string.Empty,
            ["scope"] = "none",
        }).ToList();
    }

    private static List<Dictionary<string, string>> BuildItems(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(24).Select(item =>
            {
                var link = string.IsNullOrWhiteSpace(item.Url) ? EmptyLink() : BuildLink(item.Url, origin, localRoutes);
                return new Dictionary<string, string>
                {
                    ["title"] = CleanDisplayText(item.Title),
                    ["body"] = CleanDisplayText(item.Body),
                    ["url"] = link["url"],
                    ["source_url"] = link["source_url"],
                    ["scope"] = link["scope"],
                    ["media_url"] = item.MediaUrl,
                    ["media_alt"] = CleanDisplayText(item.MediaAlt),
                };
            }).ToList();
        }

        if (section.Media.Count > 0)
        {
            return section.Media.Take(24).Select(media => new Dictionary<string, string>
            {
                ["title"] = CleanDisplayText(string.IsNullOrWhiteSpace(media.Alt) ? section.Headline : media.Alt),
                ["body"] = CleanDisplayText(section.Body),
                ["url"] = string.Empty,
                ["source_url"] = string.Empty,
                ["scope"] = "none",
                ["media_url"] = media.Url,
                ["media_alt"] = CleanDisplayText(media.Alt),
            }).ToList();
        }

        return section.Actions.Take(24).Select(action =>
        {
            var link = BuildLink(action.Url, origin, localRoutes);
            return new Dictionary<string, string>
            {
                ["title"] = action.Label,
                ["body"] = string.Empty,
                ["url"] = link["url"],
                ["source_url"] = link["source_url"],
                ["scope"] = link["scope"],
                ["media_url"] = string.Empty,
                ["media_alt"] = string.Empty,
            };
        }).ToList();
    }

    private static List<Dictionary<string, object?>> BuildServiceCategories(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(16).Select(item =>
            {
                var links = new List<Dictionary<string, string>>();
                if (!string.IsNullOrWhiteSpace(item.Url))
                {
                    var link = BuildLink(item.Url, origin, localRoutes);
                    link["label"] = string.IsNullOrWhiteSpace(item.Title) ? link["label"] : item.Title;
                    links.Add(link);
                }

                return new Dictionary<string, object?>
                {
                    ["title"] = item.Title,
                    ["body"] = item.Body,
                    ["links"] = links,
                };
            }).ToList();
        }

        return section.Actions
            .Chunk(4)
            .Take(6)
            .Select((chunk, index) => new Dictionary<string, object?>
            {
                ["title"] = index == 0 && !string.IsNullOrWhiteSpace(section.Headline)
                    ? section.Headline
                    : $"Service Group {index + 1}",
                ["body"] = string.Empty,
                ["links"] = BuildLinks(chunk, origin, localRoutes, 4),
            })
            .ToList();
    }

    private static List<Dictionary<string, string>> BuildActionLinks(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (section.Actions.Count > 0)
        {
            return section.Actions
                .Where(action => !string.IsNullOrWhiteSpace(action.Label) && !string.IsNullOrWhiteSpace(action.Url))
                .DistinctBy(action => $"{action.Label}\n{action.Url}", StringComparer.Ordinal)
                .Take(16)
                .Select(action =>
                {
                    var link = BuildLink(action.Url, origin, localRoutes);
                    link["label"] = action.Label;
                    link["kind"] = string.IsNullOrWhiteSpace(action.Kind) ? "secondary" : action.Kind;
                    return link;
                })
                .ToList();
        }

        return section.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(item.Url))
            .Take(16)
            .Select(item =>
            {
                var link = BuildLink(item.Url, origin, localRoutes);
                link["label"] = item.Title;
                link["kind"] = "secondary";
                return link;
            })
            .ToList();
    }

    private static List<Dictionary<string, string>> BuildFilterOptions(ExtractedSection section)
    {
        if (section.Actions.Count > 0)
        {
            return section.Actions.Take(12).Select(action => new Dictionary<string, string>
            {
                ["label"] = action.Label,
                ["value"] = string.IsNullOrWhiteSpace(action.Url) ? action.Label : action.Url,
                ["count"] = string.Empty,
            }).ToList();
        }

        if (section.Items.Count > 0)
        {
            return section.Items.Take(12).Select(item => new Dictionary<string, string>
            {
                ["label"] = item.Title,
                ["value"] = string.IsNullOrWhiteSpace(item.Url) ? item.Title : item.Url,
                ["count"] = ExtractFirstNumber(item.Body),
            }).ToList();
        }

        return SplitTokens($"{section.Headline} {section.Body}")
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(token => new Dictionary<string, string>
            {
                ["label"] = token,
                ["value"] = token,
                ["count"] = string.Empty,
            })
            .ToList();
    }

    private static List<Dictionary<string, string>> BuildMetricItems(ExtractedSection section)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(12).Select(item => new Dictionary<string, string>
            {
                ["label"] = item.Title,
                ["value"] = ExtractFirstNumber(item.Body),
                ["detail"] = item.Body,
            }).ToList();
        }

        var tokens = SplitTokens($"{section.Headline} {section.Body}")
            .Where(token => !string.Equals(token, section.Headline, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();
        if (tokens.Count == 0)
        {
            tokens.Add(string.IsNullOrWhiteSpace(section.Headline) ? "Metric" : section.Headline);
        }

        return tokens
            .Chunk(2)
            .Select((chunk, index) => new Dictionary<string, string>
            {
                ["label"] = chunk[0],
                ["value"] = chunk.Length > 1 ? chunk[1] : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["detail"] = section.Body,
            })
            .Take(6)
            .ToList();
    }

    private static List<Dictionary<string, string>> BuildSeries(ExtractedSection section)
    {
        var tokens = SplitTokens(section.Body).Take(16).ToList();
        if (tokens.Count < 2)
        {
            tokens = SplitTokens(section.Headline).Take(4).ToList();
        }

        if (tokens.Count == 0)
        {
            return
            [
                new Dictionary<string, string>
                {
                    ["label"] = "Value",
                    ["value"] = "1",
                },
            ];
        }

        return tokens
            .Chunk(2)
            .Select((chunk, index) => new Dictionary<string, string>
            {
                ["label"] = chunk[0],
                ["value"] = chunk.Length > 1 ? chunk[1] : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> BuildTableRows(ExtractedSection section)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(12).Select(item => new Dictionary<string, object?>
            {
                ["cells"] = new List<string> { item.Title, item.Body },
            }).ToList();
        }

        var tokens = SplitTokens(section.Body).ToList();
        if (tokens.Count == 0)
        {
            tokens.Add(string.IsNullOrWhiteSpace(section.Headline) ? "Item" : section.Headline);
        }

        return tokens
            .Chunk(2)
            .Take(8)
            .Select(chunk => new Dictionary<string, object?>
            {
                ["cells"] = new List<string>
                {
                    chunk[0],
                    chunk.Length > 1 ? chunk[1] : string.Empty,
                },
            })
            .ToList();
    }

    private static List<Dictionary<string, string>> BuildSteps(ExtractedSection section)
    {
        var tokens = SplitTokens($"{section.Headline} {section.Body}")
            .Where(token => !Regex.IsMatch(token, @"^\d+$", RegexOptions.CultureInvariant))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (tokens.Count == 0)
        {
            tokens.Add("Start");
        }

        return tokens.Select((token, index) => new Dictionary<string, string>
        {
            ["label"] = token,
            ["status"] = index == 0 ? "current" : "upcoming",
        }).ToList();
    }

    private static List<Dictionary<string, object?>> BuildFields(ExtractedSection section, SiteCrawlPage page)
    {
        if (page.Forms.Count > 0)
        {
            return page.Forms
                .SelectMany(form => form.Fields)
                .Take(16)
                .Select(field => new Dictionary<string, object?>
                {
                    ["name"] = field.Name,
                    ["id"] = field.Id,
                    ["label"] = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label,
                    ["type"] = string.IsNullOrWhiteSpace(field.Type) ? "text" : field.Type,
                    ["required"] = field.Required,
                })
                .ToList();
        }

        var required = ContainsAny($"{section.Headline} {section.Body}", "required", "must", "必填");
        var tokens = SplitTokens($"{section.Headline} {section.Body}")
            .Where(token => token.Length > 2 && !string.Equals(token, "required", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        if (tokens.Count == 0)
        {
            tokens.Add("Field");
        }

        return tokens.Select(token => new Dictionary<string, object?>
        {
            ["name"] = SanitizeFieldName(token),
            ["id"] = SanitizeFieldName(token),
            ["label"] = token,
            ["type"] = token.Contains("email", StringComparison.OrdinalIgnoreCase) ? "email" : "text",
            ["required"] = required,
        }).ToList();
    }

    private static List<string> BuildMessages(ExtractedSection section)
    {
        var messages = Regex.Split($"{section.Headline}. {section.Body}", @"[\r\n.。]+")
            .Select(message => message.Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return messages.Count == 0 ? ["Review required fields before continuing."] : messages;
    }

    private static List<Dictionary<string, object?>> BuildPricingPlans(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var plans = section.Items.Count > 0
            ? section.Items.Take(6).Select(item =>
            {
                var link = string.IsNullOrWhiteSpace(item.Url) ? EmptyActionLink() : BuildLink(item.Url, origin, localRoutes);
                link["label"] = string.IsNullOrWhiteSpace(item.Title) ? "Choose plan" : item.Title;
                link["kind"] = "primary";
                return new Dictionary<string, object?>
                {
                    ["title"] = item.Title,
                    ["price"] = ExtractPrice(item.Body),
                    ["body"] = item.Body,
                    ["features"] = SplitFeatureText(item.Body),
                    ["action"] = link,
                };
            }).ToList()
            : [];

        if (plans.Count > 0)
        {
            return plans;
        }

        var fallbackAction = section.Actions.Count > 0
            ? BuildActionLinks(section, origin, localRoutes).First()
            : EmptyActionLink();
        return
        [
            new Dictionary<string, object?>
            {
                ["title"] = string.IsNullOrWhiteSpace(section.Headline) ? "Plan" : section.Headline,
                ["price"] = ExtractPrice(section.Body),
                ["body"] = section.Body,
                ["features"] = SplitFeatureText(section.Body),
                ["action"] = fallbackAction,
            },
        ];
    }

    private static Dictionary<string, string> EmptyActionLink()
    {
        return new Dictionary<string, string>
        {
            ["label"] = string.Empty,
            ["url"] = string.Empty,
            ["source_url"] = string.Empty,
            ["scope"] = "none",
            ["kind"] = "secondary",
        };
    }

    private static List<string> SplitFeatureText(string value)
    {
        var features = Regex.Split(value ?? string.Empty, @"[\r\n,;。]+")
            .Select(feature => feature.Trim())
            .Where(feature => !string.IsNullOrWhiteSpace(feature))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return features.Count == 0 ? ["Included"] : features;
    }

    private static List<string> SplitTokens(string value)
    {
        return Regex.Matches(value ?? string.Empty, @"[\p{L}\p{N}%$.-]+", RegexOptions.CultureInvariant)
            .Select(match => match.Value.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
    }

    private static string ExtractFirstNumber(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"\d+(?:[.,]\d+)?%?", RegexOptions.CultureInvariant);
        return match.Success ? match.Value : string.Empty;
    }

    private static string ExtractPrice(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"[$NTDUSD\s]*\d+(?:[.,]\d+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : string.Empty;
    }

    private static string SanitizeFieldName(string value)
    {
        var sanitized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "field" : sanitized;
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Dictionary<string, string>> BuildLinks(
        IEnumerable<ExtractedAction> links,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes,
        int maxLinks)
    {
        return links
            .Where(link => !string.IsNullOrWhiteSpace(link.Label) && !string.IsNullOrWhiteSpace(link.Url))
            .DistinctBy(link => $"{link.Label}\n{link.Url}", StringComparer.Ordinal)
            .Take(maxLinks)
            .Select(link =>
            {
                var built = BuildLink(link.Url, origin, localRoutes);
                built["label"] = link.Label;
                return built;
            })
            .ToList();
    }

    private static Dictionary<string, string> BuildLink(
        string link,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var absolute = NormalizeAbsoluteUrl(link, origin);
        var normalized = NormalizeUrlForLookup(absolute);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            localRoutes.TryGetValue(normalized, out var localRoute))
        {
            return new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(absolute, origin),
                ["url"] = localRoute,
                ["source_url"] = absolute,
                ["scope"] = "internal",
            };
        }

        if (IsSameOrigin(absolute, origin))
        {
            return new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(absolute, origin),
                ["url"] = BuildRoutePath(absolute, origin),
                ["source_url"] = absolute,
                ["scope"] = "internal",
            };
        }

        return new Dictionary<string, string>
        {
            ["label"] = BuildLinkLabel(absolute, origin),
            ["url"] = absolute,
            ["source_url"] = absolute,
            ["scope"] = "external",
        };
    }

    private static Dictionary<string, string> EmptyLink()
    {
        return new Dictionary<string, string>
        {
            ["label"] = string.Empty,
            ["url"] = string.Empty,
            ["source_url"] = string.Empty,
            ["scope"] = "none",
        };
    }

    private static Dictionary<string, string> BuildLocalRouteMap(
        IEnumerable<SiteCrawlPage> pages,
        IEnumerable<SiteCrawlRedirect> redirects,
        string rootOrigin)
    {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var routePath = BuildRoutePath(page.FinalUrl, rootOrigin);
            AddRouteLookupWithAlternateScheme(routes, page.FinalUrl, routePath);
            AddRouteLookupWithAlternateScheme(routes, page.Url, routePath);
        }

        foreach (var redirect in redirects)
        {
            var targetKey = NormalizeUrlForLookup(redirect.ToUrl);
            if (string.IsNullOrWhiteSpace(targetKey) ||
                !routes.TryGetValue(targetKey, out var routePath))
            {
                continue;
            }

            AddRouteLookupWithAlternateScheme(routes, redirect.FromUrl, routePath);
        }

        return routes;
    }

    private static void AddRouteLookupWithAlternateScheme(
        IDictionary<string, string> routes,
        string absoluteUrl,
        string routePath)
    {
        AddRouteLookup(routes, absoluteUrl, routePath);
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var alternateScheme = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? Uri.UriSchemeHttp
            : Uri.UriSchemeHttps;
        var alternate = new UriBuilder(uri)
        {
            Scheme = alternateScheme,
            Port = -1,
        }.Uri.ToString();
        AddRouteLookup(routes, alternate, routePath);
    }

    private static void AddRouteLookup(IDictionary<string, string> routes, string absoluteUrl, string routePath)
    {
        var key = NormalizeUrlForLookup(absoluteUrl);
        if (!string.IsNullOrWhiteSpace(key) && !routes.ContainsKey(key))
        {
            routes[key] = routePath;
        }
    }

    private static string NormalizeAbsoluteUrl(string link, string origin)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
            Uri.TryCreate(originUri, link, out var relative)
            ? relative.ToString()
            : link;
    }

    private static string NormalizeUrlForLookup(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string BuildLinkLabel(string link, string origin)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return link;
        }

        var path = uri.PathAndQuery;
        if (!string.IsNullOrWhiteSpace(origin) &&
            link.StartsWith(origin, StringComparison.OrdinalIgnoreCase))
        {
            return path == "/" ? "Home" : path.Trim('/').Replace('-', ' ').Replace('_', ' ');
        }

        return uri.Host;
    }

    private static bool IsSameOrigin(string link, string origin)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        return linkUri.Scheme.Equals(originUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            linkUri.IdnHost.Equals(originUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            linkUri.Port == originUri.Port;
    }

    private static string ResolveSiteTitle(SiteCrawlResult crawl)
    {
        var rootPage = crawl.Pages.FirstOrDefault(page => page.Depth == 0) ?? crawl.Pages.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rootPage?.Title))
        {
            return rootPage.Title;
        }

        return string.IsNullOrWhiteSpace(crawl.Root.NormalizedStartUrl)
            ? "Generated Site"
            : crawl.Root.NormalizedStartUrl;
    }

    private static string BuildRoutePath(string finalUrl, string rootOrigin)
    {
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri))
        {
            return "/";
        }

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            var rootQuerySlug = BuildQuerySlug(uri.Query);
            path = string.IsNullOrWhiteSpace(rootQuerySlug) ? "/" : $"/{rootQuerySlug}";
        }
        else
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".aspx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".php", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^extension.Length];
            }

            path = path.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(path))
            {
                path = "/";
            }

            var querySlug = BuildQuerySlug(uri.Query);
            if (!string.IsNullOrWhiteSpace(querySlug))
            {
                path = $"{path}/{querySlug}";
            }
        }

        if (string.IsNullOrWhiteSpace(rootOrigin) ||
            string.Equals(NormalizeOrigin(uri), rootOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return "/sites/" + BuildHostRouteSegment(uri) + (path == "/" ? "/" : path);
    }

    private static string NormalizeOrigin(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6 && !host.StartsWith("[", StringComparison.Ordinal))
        {
            host = $"[{host.Trim('[', ']')}]";
        }

        var origin = $"{uri.Scheme.ToLowerInvariant()}://{host}";
        return uri.IsDefaultPort ? origin : $"{origin}:{uri.Port}";
    }

    private static string BuildHostRouteSegment(Uri uri)
    {
        var host = uri.IdnHost.Trim().TrimEnd('.').ToLowerInvariant();
        return uri.IsDefaultPort ? host : $"{host}-{uri.Port}";
    }

    private static string BuildQuerySlug(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var tokens = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Split('=', 2, StringSplitOptions.RemoveEmptyEntries))
            .Select(token => Regex.Replace(Uri.UnescapeDataString(token), "[^a-zA-Z0-9]+", "-").Trim('-'))
            .Where(token => !string.IsNullOrWhiteSpace(token));

        return string.Join('-', tokens);
    }

    private static string BuildNodeId(string prefix, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{System.Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static ComponentRequest CloneComponentRequest(ComponentRequest request)
    {
        return new ComponentRequest
        {
            RequestId = request.RequestId,
            Role = request.Role,
            ComponentType = request.ComponentType,
            Reason = request.Reason,
            SourcePageUrl = request.SourcePageUrl,
            SourceSelector = request.SourceSelector,
        };
    }

    private static ComponentLibraryManifest CloneManifest(ComponentLibraryManifest manifest)
    {
        return new ComponentLibraryManifest
        {
            LibraryId = manifest.LibraryId,
            Version = manifest.Version,
            Components = manifest.Components.Select(component => new ComponentDefinition
            {
                Type = component.Type,
                Description = component.Description,
                SupportedRoles = component.SupportedRoles.ToList(),
                PropsSchema = ClonePropsSchema(component.PropsSchema),
                Generated = component.Generated,
            }).ToList(),
        };
    }

    private static ComponentPropsSchema ClonePropsSchema(ComponentPropsSchema schema)
    {
        return new ComponentPropsSchema
        {
            Required = schema.Required.ToList(),
            Properties = schema.Properties.ToDictionary(
                pair => pair.Key,
                pair => ClonePropSchema(pair.Value),
                StringComparer.Ordinal),
        };
    }

    private static ComponentPropSchema ClonePropSchema(ComponentPropSchema schema)
    {
        return new ComponentPropSchema
        {
            Type = schema.Type,
            Items = schema.Items is null ? null : ClonePropSchema(schema.Items),
            Required = schema.Required.ToList(),
            Properties = schema.Properties.ToDictionary(
                pair => pair.Key,
                pair => ClonePropSchema(pair.Value),
                StringComparer.Ordinal),
        };
    }
}
