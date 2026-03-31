using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;

namespace Broker.Services;

public sealed class ProjectInterviewTemplateCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _catalogPath;
    private IReadOnlyList<ProjectInterviewTemplateCandidate>? _cachedCandidates;

    public ProjectInterviewTemplateCatalogService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["ProjectInterview:TemplateCatalogPath"]
            ?? @"..\..\javascript\browser\templates\catalog.json";

        _catalogPath = ResolveCatalogPath(configuredPath, environment.ContentRootPath);
    }

    public IReadOnlyList<ProjectInterviewTemplateCandidate> NarrowByScale(string projectScale)
    {
        var normalizedScale = projectScale.Trim().ToLowerInvariant();
        return LoadCatalog()
            .Where(candidate => candidate.SupportedProjectScales.Any(scale => string.Equals(scale, normalizedScale, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private IReadOnlyList<ProjectInterviewTemplateCandidate> LoadCatalog()
    {
        if (_cachedCandidates != null)
            return _cachedCandidates;

        var catalogJson = File.ReadAllText(_catalogPath);
        var catalog = JsonSerializer.Deserialize<ProjectInterviewTemplateCatalogDocument>(catalogJson, JsonOptions)
            ?? throw new InvalidOperationException("Project interview template catalog could not be deserialized.");

        var catalogDirectory = Path.GetDirectoryName(_catalogPath)
            ?? throw new InvalidOperationException("Project interview template catalog directory is missing.");

        _cachedCandidates = catalog.Families.Select(entry =>
        {
            var manifestPath = Path.GetFullPath(entry.ManifestPath, catalogDirectory);
            var manifestJson = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ProjectInterviewTemplateManifest>(manifestJson, JsonOptions)
                ?? throw new InvalidOperationException($"Template manifest '{manifestPath}' could not be deserialized.");

            return new ProjectInterviewTemplateCandidate(
                manifest.TemplateId,
                manifest.Title,
                manifest.Summary,
                manifest.SupportedProjectScales,
                manifestPath);
        }).ToArray();

        return _cachedCandidates;
    }

    private static string ResolveCatalogPath(string configuredPath, string contentRootPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        var contentRootRelative = Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
        if (File.Exists(contentRootRelative))
            return contentRootRelative;

        return Path.GetFullPath(configuredPath, Directory.GetCurrentDirectory());
    }
}

public sealed record ProjectInterviewTemplateCatalogDocument(
    [property: JsonPropertyName("families")] IReadOnlyList<ProjectInterviewTemplateCatalogEntry> Families);

public sealed record ProjectInterviewTemplateCatalogEntry(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("supported_project_scales")] IReadOnlyList<string> SupportedProjectScales,
    [property: JsonPropertyName("manifest_path")] string ManifestPath);

public sealed record ProjectInterviewTemplateManifest(
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("supported_project_scales")] IReadOnlyList<string> SupportedProjectScales,
    [property: JsonPropertyName("required_sections")] IReadOnlyList<string> RequiredSections,
    [property: JsonPropertyName("optional_modules")] IReadOnlyList<string> OptionalModules,
    [property: JsonPropertyName("supported_styles")] IReadOnlyList<string> SupportedStyles,
    [property: JsonPropertyName("supported_component_sets")] IReadOnlyList<string> SupportedComponentSets);

public sealed record ProjectInterviewTemplateCandidate(
    string TemplateId,
    string Title,
    string Summary,
    IReadOnlyList<string> SupportedProjectScales,
    string ManifestPath);
