using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

/// <summary>
/// Crawl result -> generator document. The active path delegates to the template pipeline
/// (intent -> template match -> compile). The component vocabulary is the fixed library manifest;
/// there is no per-crawl component fabrication.
/// </summary>
/// <remarks>
/// Consolidation note (2026-06-16, ComponentLibraryConsolidation): the legacy per-section
/// converter (BuildRoute/BuildSectionNode/EnsureGeneratedComponent) was removed — it fabricated
/// `Generated{Role}Section` types into the manifest at convert-time ("shoot then draw the target"),
/// and was dead code (Convert delegates to TemplateCompiler). The intended end state is to compose
/// the canonical `ui_components` library instead of this generator-side schema vocabulary.
/// </remarks>
public sealed class SiteGeneratorConverter
{
    private readonly ComponentLibraryManifest baseManifest;

    public SiteGeneratorConverter(ComponentLibraryManifest componentLibrary)
    {
        baseManifest = CloneManifest(componentLibrary);
    }

    public GeneratorSiteDocument Convert(SiteCrawlResult crawl)
    {
        ArgumentNullException.ThrowIfNull(crawl);

        var manifest = CloneManifest(baseManifest);
        var intent = new SiteIntentExtractor().Extract(crawl);
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var plan = new TemplateMatcher(templates, manifest).Match(intent);
        return new TemplateCompiler(manifest).Compile(crawl, intent, plan);
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
                BComponent = component.BComponent,
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
