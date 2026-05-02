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

            foreach (var slot in pageDefinition.Slots)
            {
                var block = SelectBlockForSlot(page, slot.Name);
                if (block is null && !slot.Required)
                {
                    continue;
                }

                pagePlan.Slots.Add(BuildSlotPlan(plan, page, slot, block));
            }

            plan.Pages.Add(pagePlan);
        }

        return plan;
    }

    private TemplateDefinition SelectTemplate(SiteIntentModel intent)
    {
        return templates.Templates.FirstOrDefault(template =>
                template.SupportedSiteKinds.Contains(intent.SiteKind, StringComparer.OrdinalIgnoreCase)) ??
            templates.Templates.FirstOrDefault(template =>
                string.Equals(template.TemplateId, "institutional_site", StringComparison.Ordinal)) ??
            templates.Templates.First();
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

    private static SiteIntentBlock? SelectBlockForSlot(SiteIntentPage page, string slotName)
    {
        return page.Blocks.FirstOrDefault(block =>
                string.Equals(block.Slot, slotName, StringComparison.Ordinal)) ??
            (slotName == "content"
                ? page.Blocks.FirstOrDefault(block => block.Kind is "content_article" or "article_list" or "form")
                : null);
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
            ("quick_links", _) => ["QuickLinkRibbon", "CardGrid", "AtomicSection"],
            ("news", "news_grid") => ["NewsGrid", "NewsCardCarousel", "CardGrid"],
            ("news", "article_list") => ["ArticleList", "NewsGrid", "CardGrid", "AtomicSection"],
            ("news", _) => ["NewsCardCarousel", "NewsGrid", "CardGrid"],
            ("features", _) => ["MediaFeatureGrid", "CardGrid", "AtomicSection"],
            ("content", "article_list") => ["ArticleList", "NewsGrid", "CardGrid", "AtomicSection"],
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
