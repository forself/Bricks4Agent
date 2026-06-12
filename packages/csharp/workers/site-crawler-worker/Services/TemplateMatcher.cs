using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class TemplateMatcher
{
    private readonly TemplateFrameworkManifest templates;
    private readonly HashSet<string> availableComponents;

    public TemplateMatcher(TemplateFrameworkManifest templates, ComponentLibraryManifest componentLibrary)
    {
        this.templates = templates ?? throw new ArgumentNullException(nameof(templates));
        ArgumentNullException.ThrowIfNull(componentLibrary);
        availableComponents = componentLibrary.Components
            .Select(component => component.Type)
            .ToHashSet(StringComparer.Ordinal);
    }

    public TemplatePlan Match(SiteIntentModel intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var template = SelectTemplate(intent);
        var plan = new TemplatePlan
        {
            TemplateId = template.TemplateId,
        };

        foreach (var page in intent.Pages)
        {
            var pageType = template.PageTypes.TryGetValue(page.PageType, out var definition)
                ? page.PageType
                : template.PageTypes.ContainsKey("unknown")
                    ? "unknown"
                    : template.PageTypes.Keys.First();
            var pageDefinition = template.PageTypes[pageType];
            var pagePlan = new TemplatePagePlan
            {
                PageUrl = page.PageUrl,
                PageType = pageType,
            };
            var usedBlockIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var slot in pageDefinition.Slots)
            {
                var block = SelectBlockForSlot(page, slot.Name, usedBlockIds);
                if (block is null && !slot.Required)
                {
                    continue;
                }

                if (block is not null)
                {
                    usedBlockIds.Add(block.Id);
                }

                pagePlan.Slots.Add(BuildSlotPlan(plan, page, slot, block));
            }

            plan.Pages.Add(pagePlan);
        }

        return plan;
    }

    private TemplateDefinition SelectTemplate(SiteIntentModel intent)
    {
        return templates.Templates
            .OrderByDescending(template => ScoreTemplate(template, intent))
            .ThenBy(template => string.Equals(template.TemplateId, "hero_news_portal", StringComparison.Ordinal) ? 0 : 1)
            .First();
    }

    private static int ScoreTemplate(TemplateDefinition template, SiteIntentModel intent)
    {
        var score = 0;
        var allSlots = intent.Pages
            .SelectMany(page => page.Blocks.Select(block => block.Slot))
            .ToHashSet(StringComparer.Ordinal);

        if (string.Equals(template.TemplateId, "search_service_portal", StringComparison.Ordinal))
        {
            score += allSlots.Contains("search") ? 35 : 0;
            score += allSlots.Contains("service_categories") ? 25 : 0;
            score += allSlots.Contains("service_actions") ? 12 : 0;
            score += allSlots.Contains("tabbed_news") ? 12 : 0;
        }
        else if (string.Equals(template.TemplateId, "service_action_portal", StringComparison.Ordinal))
        {
            score += allSlots.Contains("service_actions") ? 35 : 0;
            score += allSlots.Contains("hero") ? 18 : 0;
            score += allSlots.Contains("tabbed_news") ? 16 : 0;
            score += allSlots.Contains("service_categories") ? 10 : 0;
        }
        else if (string.Equals(template.TemplateId, "hero_news_portal", StringComparison.Ordinal))
        {
            score += allSlots.Contains("hero") ? 30 : 0;
            score += allSlots.Contains("news") ? 24 : 0;
            score += allSlots.Contains("quick_links") ? 12 : 0;
            score += allSlots.Contains("features") ? 10 : 0;
        }
        else if (string.Equals(template.TemplateId, "search_results_portal", StringComparison.Ordinal))
        {
            score += allSlots.Contains("search_box") ? 35 : 0;
            score += allSlots.Contains("filter_panel") ? 24 : 0;
            score += allSlots.Contains("result_list") ? 35 : 0;
            score += allSlots.Contains("pagination") ? 10 : 0;
        }
        else if (string.Equals(template.TemplateId, "report_dashboard", StringComparison.Ordinal))
        {
            score += allSlots.Contains("filter_bar") ? 16 : 0;
            score += allSlots.Contains("metric_summary") ? 40 : 0;
            score += allSlots.Contains("chart_panel") ? 30 : 0;
            score += allSlots.Contains("data_table") ? 20 : 0;
        }
        else if (string.Equals(template.TemplateId, "input_flow", StringComparison.Ordinal))
        {
            score += allSlots.Contains("step_indicator") ? 22 : 0;
            score += allSlots.Contains("form_fields") ? 40 : 0;
            score += allSlots.Contains("validation_summary") ? 12 : 0;
            score += allSlots.Contains("action_bar") ? 18 : 0;
        }
        else if (string.Equals(template.TemplateId, "commercial_showcase", StringComparison.Ordinal))
        {
            score += allSlots.Contains("showcase_hero") ? 35 : 0;
            score += allSlots.Contains("product_cards") ? 28 : 0;
            score += allSlots.Contains("proof_strip") ? 12 : 0;
            score += allSlots.Contains("pricing_panel") ? 18 : 0;
            score += allSlots.Contains("cta_band") ? 16 : 0;
        }

        foreach (var page in intent.Pages)
        {
            var pageType = template.PageTypes.ContainsKey(page.PageType)
                ? page.PageType
                : template.PageTypes.ContainsKey("unknown")
                    ? "unknown"
                    : template.PageTypes.Keys.First();
            var slotNames = template.PageTypes[pageType].Slots.Select(slot => slot.Name).ToHashSet(StringComparer.Ordinal);
            var pageSlots = page.Blocks.Select(block => block.Slot).ToHashSet(StringComparer.Ordinal);
            score += pageSlots.Count(slotNames.Contains) * 5;
            score -= template.PageTypes[pageType].Slots.Count(slot => slot.Required && !pageSlots.Contains(slot.Name)) * 3;
        }

        return score;
    }

    private TemplateSlotPlan BuildSlotPlan(
        TemplatePlan plan,
        SiteIntentPage page,
        TemplateSlotDefinition slot,
        SiteIntentBlock? block)
    {
        var preferredTypes = GetPreferredTypes(slot, block).ToList();
        var preferred = preferredTypes.FirstOrDefault() ?? slot.Fallback;
        var chosen = preferredTypes.FirstOrDefault(availableComponents.Contains);
        var fallback = availableComponents.Contains(slot.Fallback)
            ? slot.Fallback
            : slot.Accepts.FirstOrDefault(availableComponents.Contains) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(chosen))
        {
            chosen = string.IsNullOrWhiteSpace(fallback)
                ? availableComponents.FirstOrDefault(type => type == "AtomicSection") ?? availableComponents.First()
                : fallback;
            AddComponentRequest(
                plan,
                slot.Name,
                preferred,
                "component_gap:no accepted component is declared in the loaded manifest",
                page.PageUrl,
                block?.Section.SourceSelector ?? string.Empty);
        }
        else if (!string.Equals(chosen, preferred, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(preferred))
        {
            AddComponentRequest(
                plan,
                slot.Name,
                preferred,
                $"preferred_component_missing:fell back to {chosen}",
                page.PageUrl,
                block?.Section.SourceSelector ?? string.Empty);
        }

        return new TemplateSlotPlan
        {
            SlotName = slot.Name,
            ComponentType = chosen,
            FallbackComponentType = fallback,
            Block = block,
            Confidence = block?.Confidence ?? 0.5,
            Reasons = block?.Reasons.ToList() ?? [$"required_slot:{slot.Name}"],
        };
    }

    private static SiteIntentBlock? SelectBlockForSlot(
        SiteIntentPage page,
        string slotName,
        IReadOnlySet<string> usedBlockIds)
    {
        var available = page.Blocks
            .Where(block => !usedBlockIds.Contains(block.Id))
            .ToList();

        var direct = available.FirstOrDefault(block =>
            string.Equals(block.Slot, slotName, StringComparison.Ordinal) &&
            (slotName != "content" || HasRenderableContent(page, block)));
        if (direct is not null)
        {
            return direct;
        }

        return slotName switch
        {
            "features" => available.FirstOrDefault(block =>
                (block.Kind is "media_feature_grid" or "news_grid" or "news_carousel") &&
                HasVisualMedia(block)),
            "content" => SelectContentBlock(page, available),
            "tabbed_news" => available.FirstOrDefault(block =>
                block.Kind is "tabbed_news" or "news_grid" or "news_carousel" or "article_list"),
            _ => null,
        };
    }

    private static SiteIntentBlock? SelectContentBlock(SiteIntentPage page, IReadOnlyList<SiteIntentBlock> available)
    {
        return available.FirstOrDefault(block =>
                (block.Kind is "article_list" or "news_grid" or "news_carousel" or "tabbed_news") &&
                HasRenderableCollection(block)) ??
            available.FirstOrDefault(block =>
                (block.Kind is "content_article" or "form") &&
                HasRenderableContent(page, block));
    }

    private static bool HasVisualMedia(SiteIntentBlock block)
    {
        return block.Section.Media.Count > 0 ||
            block.Section.Items.Any(item => !string.IsNullOrWhiteSpace(item.MediaUrl));
    }

    private static bool HasRenderableCollection(SiteIntentBlock block)
    {
        return block.Section.Items.Count > 0 || block.Section.Actions.Count > 0 || HasVisualMedia(block);
    }

    private static bool HasRenderableContent(SiteIntentPage page, SiteIntentBlock block)
    {
        if (IsHomeDecorativeTitleOnlyBlock(page, block))
        {
            return false;
        }

        if (block.Section.Media.Count > 0 || block.Section.Items.Count > 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(block.Section.Headline))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(block.Section.Body))
        {
            return false;
        }

        return !string.Equals(page.PageType, "home", StringComparison.Ordinal);
    }

    private static bool IsHomeDecorativeTitleOnlyBlock(SiteIntentPage page, SiteIntentBlock block)
    {
        if (!string.Equals(page.PageType, "home", StringComparison.Ordinal) ||
            block.Section.Items.Count > 0 ||
            block.Section.Actions.Count > 0 ||
            block.Section.Media.Count == 0)
        {
            return false;
        }

        var hasDecorativeMedia = block.Section.Media.All(media =>
            media.Url.Contains("boardTitle", StringComparison.OrdinalIgnoreCase) ||
            media.Url.Contains("title", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(block.Section.Headline) &&
                string.Equals(media.Alt.Trim(), block.Section.Headline.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (!hasDecorativeMedia)
        {
            return false;
        }

        var body = block.Section.Body.Trim();
        return body.Length == 0 ||
            (body.Length <= 64 && body.All(character => !char.IsLetter(character) || char.IsUpper(character)));
    }

    private static IEnumerable<string> GetPreferredTypes(TemplateSlotDefinition slot, SiteIntentBlock? block)
    {
        if (block is null)
        {
            return slot.Accepts;
        }

        var preferred = (slot.Name, block.Kind) switch
        {
            ("header", _) => ["MegaHeader", "SiteHeader"],
            ("hero", "hero_banner") => ["HeroBanner", "HeroCarousel", "AtomicSection"],
            ("hero", _) => ["HeroCarousel", "HeroBanner", "AtomicSection"],
            ("search", _) => ["ServiceSearchHero", "HeroBanner", "AtomicSection"],
            ("service_categories", _) => ["ServiceCategoryGrid", "CardGrid", "AtomicSection"],
            ("service_actions", _) => ["ServiceActionGrid", "QuickLinkRibbon", "CardGrid", "AtomicSection"],
            ("tabbed_news", _) => ["TabbedNewsBoard", "NewsGrid", "ArticleList", "CardGrid"],
            ("search_box", _) => ["SearchBoxPanel", "ServiceSearchHero", "AtomicSection"],
            ("filter_panel", _) => ["FacetFilterPanel", "AtomicSection"],
            ("result_list", _) => ["ResultList", "ArticleList", "CardGrid", "AtomicSection"],
            ("pagination", _) => ["PaginationNav", "QuickLinkRibbon", "AtomicSection"],
            ("filter_bar", _) => ["DashboardFilterBar", "FacetFilterPanel", "AtomicSection"],
            ("metric_summary", _) => ["MetricSummaryGrid", "AtomicSection"],
            ("chart_panel", _) => ["ChartPanel", "MediaFeatureGrid", "AtomicSection"],
            ("data_table", _) => ["DataTablePreview", "ArticleList", "AtomicSection"],
            ("step_indicator", _) => ["StepIndicator", "AtomicSection"],
            ("form_fields", _) => ["StructuredFormPanel", "FormBlock", "AtomicSection"],
            ("validation_summary", _) => ["ValidationSummary", "ContentSection", "AtomicSection"],
            ("action_bar", _) => ["FormActionBar", "QuickLinkRibbon", "AtomicSection"],
            ("showcase_hero", _) => ["ShowcaseHero", "HeroBanner", "AtomicSection"],
            ("product_cards", _) => ["ProductCardGrid", "MediaFeatureGrid", "CardGrid", "AtomicSection"],
            ("proof_strip", _) => ["ProofStrip", "MetricSummaryGrid", "AtomicSection"],
            ("pricing_panel", _) => ["PricingPanel", "ProductCardGrid", "CardGrid", "AtomicSection"],
            ("cta_band", _) => ["CtaBand", "QuickLinkRibbon", "AtomicSection"],
            ("quick_links", _) => ["QuickLinkRibbon", "CardGrid", "AtomicSection"],
            ("news", "tabbed_news") => ["TabbedNewsBoard", "NewsGrid", "ArticleList", "CardGrid"],
            ("news", "news_grid") => ["NewsGrid", "NewsCardCarousel", "CardGrid"],
            ("news", "article_list") => ["ArticleList", "NewsGrid", "CardGrid", "AtomicSection"],
            ("news", _) => ["NewsCardCarousel", "NewsGrid", "CardGrid"],
            ("features", _) => ["MediaFeatureGrid", "CardGrid", "AtomicSection"],
            ("content", "article_list") => ["ArticleList", "NewsGrid", "CardGrid", "AtomicSection"],
            ("content", "news_grid") => HasVisualMedia(block)
                ? ["NewsGrid", "ArticleList", "CardGrid", "AtomicSection"]
                : ["ArticleList", "NewsGrid", "CardGrid", "AtomicSection"],
            ("content", "news_carousel") => ["NewsGrid", "ArticleList", "CardGrid", "AtomicSection"],
            ("content", "tabbed_news") => ["TabbedNewsBoard", "ArticleList", "NewsGrid", "CardGrid", "AtomicSection"],
            ("content", "content_article") => ["ContentArticle", "ContentSection", "AtomicSection"],
            ("footer", _) => ["InstitutionFooter", "SiteFooter"],
            _ => slot.Accepts,
        };

        var accepted = slot.Accepts.ToHashSet(StringComparer.Ordinal);
        return preferred.Where(accepted.Contains).Concat(slot.Accepts).Distinct(StringComparer.Ordinal);
    }

    private static void AddComponentRequest(
        TemplatePlan plan,
        string role,
        string componentType,
        string reason,
        string sourcePageUrl,
        string sourceSelector)
    {
        if (string.IsNullOrWhiteSpace(componentType))
        {
            return;
        }

        if (plan.ComponentRequests.Any(request =>
                string.Equals(request.Role, role, StringComparison.Ordinal) &&
                string.Equals(request.ComponentType, componentType, StringComparison.Ordinal) &&
                string.Equals(request.SourcePageUrl, sourcePageUrl, StringComparison.Ordinal)))
        {
            return;
        }

        plan.ComponentRequests.Add(new ComponentRequest
        {
            RequestId = $"component-request-{plan.ComponentRequests.Count + 1}",
            Role = role,
            ComponentType = componentType,
            Reason = reason,
            SourcePageUrl = sourcePageUrl,
            SourceSelector = sourceSelector,
        });
    }
}
