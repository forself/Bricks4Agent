namespace SiteCrawlerWorker.Services;

/// <summary>
/// The framed, closed set of canonical <c>ui_components</c> (B) class names that the
/// generator vocabulary is allowed to bind to.
/// </summary>
/// <remarks>
/// Consolidation (2026-06-16, ComponentLibraryConsolidation Stage 1): B is the canonical,
/// human-framed component library. Each generator section type in the manifest must declare a
/// <c>b_component</c> that is a member of this set, so the generator vocabulary is a projection
/// of B's closed set rather than a corpus-sampled "罐頭" vocabulary of its own. A binding outside
/// this set is rejected at manifest load time (fail-closed) — the generator never invents a
/// component, and never targets a B component that does not exist.
///
/// This list is the deliberate subset of B used for site-section assembly (atoms + composites +
/// sections), plus the structural page root <c>PageShell</c>. It is verified against the real
/// classes under <c>packages/javascript/browser/ui_components</c>; adding a name here without the
/// corresponding B class is a defect.
/// </remarks>
public static class BComponentRegistry
{
    /// <summary>Structural page root (document shell); not a content component.</summary>
    public const string PageShell = "PageShell";

    private static readonly HashSet<string> Members = new(StringComparer.Ordinal)
    {
        // structural
        PageShell,
        // sections (sections/)
        "PageHeader", "PageFooter", "BannerSection", "ContentSection",
        // composites (common/, form/, layout/)
        "CardGrid", "FeatureCard", "ResultList", "List", "DescriptionList",
        "FilterBar", "StatGrid", "StepIndicator", "DropdownMenu", "Form",
        "TagInput", "EditableTable", "DataTable", "SearchForm", "Pagination",
        "Alert", "TabContainer",
        // atoms (common/)
        "Text", "Heading", "Link", "Icon", "ImageViewer", "MediaPlayer",
    };

    public static bool Contains(string? bComponent)
        => !string.IsNullOrWhiteSpace(bComponent) && Members.Contains(bComponent);

    public static IReadOnlyCollection<string> All => Members;
}
