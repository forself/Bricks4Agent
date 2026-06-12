using System.Text.Json;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class TemplateFrameworkLoader
{
    private const string DefaultTemplateFileName = "visual_patterns.json";
    private const string LegacyTemplateFileName = "institutional_site.json";
    private const string DefaultTemplatePath = $"template-framework/{DefaultTemplateFileName}";
    private const string LegacyDefaultTemplatePath = $"template-framework/{LegacyTemplateFileName}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public TemplateFrameworkManifest Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return LoadDefault();
        }

        var templatePath = ResolveConfiguredPath(path);
        return LoadManifestFile(templatePath);
    }

    public TemplateFrameworkManifest LoadDefault()
    {
        foreach (var candidate in GetDefaultTemplateCandidates())
        {
            if (File.Exists(candidate))
            {
                return LoadManifestFile(candidate);
            }
        }

        throw new InvalidOperationException(
            $"Default template framework file was not found. Expected {DefaultTemplatePath} near the worker output or repository root.");
    }

    private static TemplateFrameworkManifest LoadManifestFile(string path)
    {
        TemplateFrameworkManifest? manifest;
        try
        {
            using var stream = File.OpenRead(path);
            manifest = JsonSerializer.Deserialize<TemplateFrameworkManifest>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Template framework file is not valid JSON: {path}", ex);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException($"Template framework file is empty: {path}");
        }

        Normalize(manifest);
        var errors = Validate(manifest);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Template framework file is invalid: {path}. {string.Join(" ", errors)}");
        }

        return manifest;
    }

    private static string ResolveConfiguredPath(string path)
    {
        var resolved = Path.GetFullPath(path, Directory.GetCurrentDirectory());
        if (Directory.Exists(resolved))
        {
            var visualPatternPath = Path.Combine(resolved, DefaultTemplateFileName);
            resolved = File.Exists(visualPatternPath)
                ? visualPatternPath
                : Path.Combine(resolved, LegacyTemplateFileName);
        }

        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException($"Template framework file was not found: {resolved}");
        }

        return resolved;
    }

    private static IEnumerable<string> GetDefaultTemplateCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, DefaultTemplatePath);
        yield return Path.Combine(AppContext.BaseDirectory, LegacyDefaultTemplatePath);
        yield return Path.Combine(Directory.GetCurrentDirectory(), DefaultTemplatePath);
        yield return Path.Combine(Directory.GetCurrentDirectory(), LegacyDefaultTemplatePath);
        yield return Path.Combine(
            Directory.GetCurrentDirectory(),
            "packages",
            "csharp",
            "workers",
            "site-crawler-worker",
            DefaultTemplatePath);
        yield return Path.Combine(
            Directory.GetCurrentDirectory(),
            "packages",
            "csharp",
            "workers",
            "site-crawler-worker",
            LegacyDefaultTemplatePath);
    }

    private static void Normalize(TemplateFrameworkManifest manifest)
    {
        manifest.Templates ??= [];

        foreach (var template in manifest.Templates)
        {
            template.TemplateId ??= string.Empty;
            template.PatternTags ??= [];
            template.SupportedSiteKinds ??= [];
            if (template.PatternTags.Count == 0 && template.SupportedSiteKinds.Count > 0)
            {
                template.PatternTags.AddRange(template.SupportedSiteKinds);
            }

            template.PageTypes ??= new Dictionary<string, TemplatePageTypeDefinition>(StringComparer.Ordinal);

            foreach (var pageType in template.PageTypes.Values)
            {
                pageType.Slots ??= [];
                foreach (var slot in pageType.Slots)
                {
                    slot.Name ??= string.Empty;
                    slot.Accepts ??= [];
                    slot.Fallback = string.IsNullOrWhiteSpace(slot.Fallback) ? "AtomicSection" : slot.Fallback;
                }
            }
        }
    }

    private static List<string> Validate(TemplateFrameworkManifest manifest)
    {
        var errors = new List<string>();
        if (manifest.Templates.Count == 0)
        {
            errors.Add("templates must contain at least one template.");
        }

        var seenTemplateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in manifest.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.TemplateId))
            {
                errors.Add("template_id is required.");
                continue;
            }

            if (!seenTemplateIds.Add(template.TemplateId))
            {
                errors.Add($"template_id '{template.TemplateId}' is duplicated.");
            }

            if (template.PatternTags.Count == 0)
            {
                errors.Add($"template '{template.TemplateId}' must define at least one pattern_tags metadata value.");
            }

            if (template.PageTypes.Count == 0)
            {
                errors.Add($"template '{template.TemplateId}' must define page_types.");
            }

            foreach (var (pageTypeName, pageType) in template.PageTypes)
            {
                if (string.IsNullOrWhiteSpace(pageTypeName))
                {
                    errors.Add($"template '{template.TemplateId}' contains an empty page type name.");
                }

                if (pageType.Slots.Count == 0)
                {
                    errors.Add($"template '{template.TemplateId}' page type '{pageTypeName}' must define slots.");
                }

                var seenSlots = new HashSet<string>(StringComparer.Ordinal);
                foreach (var slot in pageType.Slots)
                {
                    if (string.IsNullOrWhiteSpace(slot.Name))
                    {
                        errors.Add($"template '{template.TemplateId}' page type '{pageTypeName}' slot name is required.");
                        continue;
                    }

                    if (!seenSlots.Add(slot.Name))
                    {
                        errors.Add($"template '{template.TemplateId}' page type '{pageTypeName}' slot '{slot.Name}' is duplicated.");
                    }

                    if (slot.Accepts.Count == 0)
                    {
                        errors.Add($"template '{template.TemplateId}' page type '{pageTypeName}' slot '{slot.Name}' must define at least one accepted component.");
                    }
                }
            }
        }

        return errors;
    }
}
