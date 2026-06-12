using System.Text.Json;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class ComponentLibraryLoader
{
    public const string DefaultLibraryId = "bricks4agent.default";
    private const string DefaultManifestPath = "component-libraries/bricks4agent.default/manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> SupportedSchemaTypes = new(StringComparer.Ordinal)
    {
        "array",
        "boolean",
        "object",
        "string",
    };

    public ComponentLibraryManifest Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return LoadDefault();
        }

        var manifestPath = ResolveConfiguredPath(path);
        return LoadManifestFile(manifestPath);
    }

    public ComponentLibraryManifest LoadDefault()
    {
        foreach (var candidate in GetDefaultManifestCandidates())
        {
            if (File.Exists(candidate))
            {
                return LoadManifestFile(candidate);
            }
        }

        throw new InvalidOperationException(
            $"Default component library manifest was not found. Expected {DefaultManifestPath} near the worker output or repository root.");
    }

    private static ComponentLibraryManifest LoadManifestFile(string path)
    {
        ComponentLibraryManifest? manifest;
        try
        {
            using var stream = File.OpenRead(path);
            manifest = JsonSerializer.Deserialize<ComponentLibraryManifest>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Component library manifest is not valid JSON: {path}", ex);
        }

        if (manifest is null)
        {
            throw new InvalidOperationException($"Component library manifest is empty: {path}");
        }

        Normalize(manifest);
        var errors = Validate(manifest);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Component library manifest is invalid: {path}. {string.Join(" ", errors)}");
        }

        return manifest;
    }

    private static string ResolveConfiguredPath(string path)
    {
        var resolved = Path.GetFullPath(path, Directory.GetCurrentDirectory());
        if (Directory.Exists(resolved))
        {
            resolved = Path.Combine(resolved, "manifest.json");
        }

        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException($"Component library manifest was not found: {resolved}");
        }

        return resolved;
    }

    private static IEnumerable<string> GetDefaultManifestCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, DefaultManifestPath);
        yield return Path.Combine(Directory.GetCurrentDirectory(), DefaultManifestPath);
        yield return Path.Combine(
            Directory.GetCurrentDirectory(),
            "packages",
            "csharp",
            "workers",
            "site-crawler-worker",
            DefaultManifestPath);
    }

    private static void Normalize(ComponentLibraryManifest manifest)
    {
        manifest.LibraryId ??= string.Empty;
        manifest.Version ??= string.Empty;
        manifest.Components ??= [];

        foreach (var component in manifest.Components)
        {
            component.Type ??= string.Empty;
            component.Description ??= string.Empty;
            component.SupportedRoles ??= [];
            component.PropsSchema ??= new ComponentPropsSchema();
            NormalizePropsSchema(component.PropsSchema);
        }
    }

    private static void NormalizePropsSchema(ComponentPropsSchema schema)
    {
        schema.Required ??= [];
        schema.Properties ??= new Dictionary<string, ComponentPropSchema>();

        foreach (var property in schema.Properties.Values)
        {
            NormalizePropSchema(property);
        }
    }

    private static void NormalizePropSchema(ComponentPropSchema schema)
    {
        schema.Type ??= string.Empty;
        schema.Required ??= [];
        schema.Properties ??= new Dictionary<string, ComponentPropSchema>();

        foreach (var property in schema.Properties.Values)
        {
            NormalizePropSchema(property);
        }

        if (schema.Items is not null)
        {
            NormalizePropSchema(schema.Items);
        }
    }

    private static List<string> Validate(ComponentLibraryManifest manifest)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.LibraryId))
        {
            errors.Add("library_id is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            errors.Add("version is required.");
        }

        if (manifest.Components.Count == 0)
        {
            errors.Add("components must contain at least one component.");
        }

        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in manifest.Components)
        {
            if (string.IsNullOrWhiteSpace(component.Type))
            {
                errors.Add("component.type is required.");
                continue;
            }

            if (!seenTypes.Add(component.Type))
            {
                errors.Add($"component type '{component.Type}' is duplicated.");
            }

            if (component.PropsSchema.Properties.Count == 0)
            {
                errors.Add($"component '{component.Type}' props_schema.properties must contain at least one property.");
            }

            ValidateRequiredKeys(
                component.PropsSchema.Required,
                component.PropsSchema.Properties,
                $"component '{component.Type}' props_schema",
                errors);

            foreach (var (propertyName, propertySchema) in component.PropsSchema.Properties)
            {
                ValidatePropSchema(propertySchema, $"component '{component.Type}' prop '{propertyName}'", errors);
            }
        }

        return errors;
    }

    private static void ValidatePropSchema(ComponentPropSchema schema, string path, List<string> errors)
    {
        if (!SupportedSchemaTypes.Contains(schema.Type))
        {
            errors.Add($"{path} has unsupported type '{schema.Type}'.");
            return;
        }

        if (string.Equals(schema.Type, "array", StringComparison.Ordinal) && schema.Items is null)
        {
            errors.Add($"{path} is an array and must define items.");
        }

        if (string.Equals(schema.Type, "object", StringComparison.Ordinal))
        {
            if (schema.Properties.Count == 0)
            {
                errors.Add($"{path} is an object and must define properties.");
            }

            ValidateRequiredKeys(schema.Required, schema.Properties, path, errors);
        }

        if (schema.Items is not null)
        {
            ValidatePropSchema(schema.Items, $"{path}.items", errors);
        }

        foreach (var (propertyName, propertySchema) in schema.Properties)
        {
            ValidatePropSchema(propertySchema, $"{path}.{propertyName}", errors);
        }
    }

    private static void ValidateRequiredKeys(
        IEnumerable<string> required,
        IReadOnlyDictionary<string, ComponentPropSchema> properties,
        string path,
        List<string> errors)
    {
        foreach (var key in required.Where(key => !properties.ContainsKey(key)))
        {
            errors.Add($"{path} requires '{key}' but does not define that property.");
        }
    }
}
