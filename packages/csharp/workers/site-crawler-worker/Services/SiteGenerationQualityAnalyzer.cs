using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class SiteGenerationQualityAnalyzer
{
    private readonly ComponentSchemaValidator schemaValidator;

    public SiteGenerationQualityAnalyzer()
        : this(new ComponentSchemaValidator())
    {
    }

    public SiteGenerationQualityAnalyzer(ComponentSchemaValidator schemaValidator)
    {
        this.schemaValidator = schemaValidator;
    }

    public SiteGenerationQualityReport Analyze(
        GeneratorSiteDocument document,
        SiteGenerationQualityPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        policy ??= new SiteGenerationQualityPolicy();
        var report = new SiteGenerationQualityReport
        {
            RouteCount = document.Routes.Count,
            ComponentRequestCount = document.ComponentRequests.Count,
            GeneratedComponentCount = document.ComponentLibrary.Components.Count(component => component.Generated),
        };

        var knownTypes = document.ComponentLibrary.Components
            .Select(component => component.Type)
            .ToHashSet(StringComparer.Ordinal);
        var allNodes = document.Routes.SelectMany(route => Flatten(route.Root)).ToList();

        report.ComponentNodeCount = allNodes.Count;
        report.ComponentTypes = allNodes
            .Select(node => node.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        report.UnknownComponentTypes = report.ComponentTypes
            .Where(type => !knownTypes.Contains(type))
            .ToList();

        var schemaResult = schemaValidator.Validate(document);
        foreach (var error in schemaResult.Errors)
        {
            report.Errors.Add($"schema: {error}");
        }

        if (!policy.AllowComponentRequests && document.ComponentRequests.Count > 0)
        {
            report.Errors.Add($"document has {document.ComponentRequests.Count} component request(s); component library coverage is incomplete.");
        }

        if (!policy.AllowGeneratedComponents && report.GeneratedComponentCount > 0)
        {
            report.Errors.Add($"document declares {report.GeneratedComponentCount} generated component(s); strict generation must use the library only.");
        }

        if (policy.RequireUniqueRoutePaths)
        {
            foreach (var duplicate in document.Routes
                .GroupBy(route => route.Path, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1))
            {
                report.Errors.Add($"duplicate route path '{duplicate.Key}'.");
            }
        }

        if (policy.RequirePageShellRoot)
        {
            for (var index = 0; index < document.Routes.Count; index++)
            {
                if (!string.Equals(document.Routes[index].Root.Type, "PageShell", StringComparison.Ordinal))
                {
                    report.Errors.Add($"routes[{index}] root must be PageShell.");
                }
            }
        }

        foreach (var node in allNodes)
        {
            ValidateHeroContent(node, report);
        }

        if (report.RouteCount == 0)
        {
            report.Errors.Add("document has no routes.");
        }

        return report;
    }

    private static void ValidateHeroContent(ComponentNode node, SiteGenerationQualityReport report)
    {
        if (node.Type is not ("HeroCarousel" or "HeroBanner"))
        {
            return;
        }

        var textParts = new List<string>
        {
            GetStringProp(node, "title"),
            GetStringProp(node, "body"),
        };
        textParts.AddRange(GetSlideText(node));
        var text = string.Join(' ', textParts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (LooksLikeMixedPageContent(text))
        {
            report.Errors.Add($"{node.Type} '{node.Id}' appears to contain mixed page content instead of a focused hero region.");
        }
    }

    private static string GetStringProp(ComponentNode node, string key)
    {
        return node.Props.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    }

    private static IEnumerable<string> GetSlideText(ComponentNode node)
    {
        if (!node.Props.TryGetValue("slides", out var value) || value is null)
        {
            yield break;
        }

        if (value is IEnumerable<Dictionary<string, string>> stringSlides)
        {
            foreach (var slide in stringSlides)
            {
                yield return string.Join(' ', ReadDictionaryValue(slide, "title"), ReadDictionaryValue(slide, "body"));
            }
        }
        else if (value is IEnumerable<Dictionary<string, object?>> objectSlides)
        {
            foreach (var slide in objectSlides)
            {
                yield return string.Join(' ', ReadDictionaryValue(slide, "title"), ReadDictionaryValue(slide, "body"));
            }
        }
    }

    private static string ReadDictionaryValue<TKey>(IReadOnlyDictionary<string, TKey> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    }

    private static bool LooksLikeMixedPageContent(string text)
    {
        if (text.Length < 700)
        {
            return false;
        }

        var signals = new[]
        {
            "activity board",
            "announcement",
            "announcements",
            "news center",
            "campus links",
            "活動看板",
            "訊息中心",
            "新聞中心",
            "校園連結",
        };
        return signals.Count(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase)) >= 2;
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
}
