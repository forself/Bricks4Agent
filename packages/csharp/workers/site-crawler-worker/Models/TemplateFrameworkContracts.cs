using System.Text.Json.Serialization;

namespace SiteCrawlerWorker.Models;

public sealed class TemplateFrameworkManifest
{
    [JsonPropertyName("templates")]
    public List<TemplateDefinition> Templates { get; set; } = new();
}

public sealed class TemplateDefinition
{
    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("supported_site_kinds")]
    public List<string> SupportedSiteKinds { get; set; } = new();

    [JsonPropertyName("page_types")]
    public Dictionary<string, TemplatePageTypeDefinition> PageTypes { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TemplatePageTypeDefinition
{
    [JsonPropertyName("slots")]
    public List<TemplateSlotDefinition> Slots { get; set; } = new();
}

public sealed class TemplateSlotDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("accepts")]
    public List<string> Accepts { get; set; } = new();

    [JsonPropertyName("fallback")]
    public string Fallback { get; set; } = "AtomicSection";
}

public sealed class SiteIntentModel
{
    [JsonPropertyName("site_kind")]
    public string SiteKind { get; set; } = "unknown";

    [JsonPropertyName("pages")]
    public List<SiteIntentPage> Pages { get; set; } = new();

    [JsonPropertyName("global_header")]
    public ExtractedHeader GlobalHeader { get; set; } = new();

    [JsonPropertyName("global_footer")]
    public ExtractedFooter GlobalFooter { get; set; } = new();

    [JsonPropertyName("theme_hints")]
    public ExtractedThemeTokens ThemeHints { get; set; } = new();
}

public sealed class SiteIntentPage
{
    [JsonPropertyName("page_url")]
    public string PageUrl { get; set; } = string.Empty;

    [JsonPropertyName("page_type")]
    public string PageType { get; set; } = "unknown";

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("blocks")]
    public List<SiteIntentBlock> Blocks { get; set; } = new();
}

public sealed class SiteIntentBlock
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "unknown";

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public ExtractedSection Section { get; set; } = new();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = new();
}

public sealed class TemplatePlan
{
    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("pages")]
    public List<TemplatePagePlan> Pages { get; set; } = new();

    [JsonPropertyName("component_requests")]
    public List<ComponentRequest> ComponentRequests { get; set; } = new();
}

public sealed class TemplatePagePlan
{
    [JsonPropertyName("page_url")]
    public string PageUrl { get; set; } = string.Empty;

    [JsonPropertyName("page_type")]
    public string PageType { get; set; } = "unknown";

    [JsonPropertyName("slots")]
    public List<TemplateSlotPlan> Slots { get; set; } = new();
}

public sealed class TemplateSlotPlan
{
    [JsonPropertyName("slot_name")]
    public string SlotName { get; set; } = string.Empty;

    [JsonPropertyName("component_type")]
    public string ComponentType { get; set; } = string.Empty;

    [JsonPropertyName("fallback_component_type")]
    public string FallbackComponentType { get; set; } = string.Empty;

    [JsonPropertyName("block")]
    public SiteIntentBlock? Block { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = new();
}
