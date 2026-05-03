using System.IO.Compression;
using System.Text.Json;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class StaticSitePackageVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] RequiredRelativeFiles =
    [
        "index.html",
        "runtime.js",
        "styles.css",
        "site.json",
        "components/manifest.json",
        "README.md",
    ];

    private readonly SiteGenerationQualityAnalyzer qualityAnalyzer;

    public StaticSitePackageVerifier()
        : this(new SiteGenerationQualityAnalyzer())
    {
    }

    public StaticSitePackageVerifier(SiteGenerationQualityAnalyzer qualityAnalyzer)
    {
        this.qualityAnalyzer = qualityAnalyzer;
    }

    public StaticSitePackageVerificationReport Verify(StaticSitePackageResult package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var report = new StaticSitePackageVerificationReport
        {
            RequiredFiles = RequiredRelativeFiles.ToList(),
            HasArchive = !string.IsNullOrWhiteSpace(package.ArchivePath),
        };

        if (string.IsNullOrWhiteSpace(package.OutputDirectory))
        {
            report.Errors.Add("output_directory is required.");
            return report;
        }

        if (!Directory.Exists(package.OutputDirectory))
        {
            report.Errors.Add($"output_directory does not exist: {package.OutputDirectory}");
            return report;
        }

        VerifyPackageFiles(package, report);
        var document = ReadSiteDocument(package, report);
        if (document is not null)
        {
            NormalizeDocumentProps(document);
            var quality = qualityAnalyzer.Analyze(document);
            report.RouteCount = quality.RouteCount;
            report.ComponentNodeCount = quality.ComponentNodeCount;
            report.Errors.AddRange(quality.Errors.Select(error => $"quality: {error}"));
            report.Warnings.AddRange(quality.Warnings.Select(warning => $"quality: {warning}"));
        }

        VerifyArchive(package, report);
        return report;
    }

    private static void VerifyPackageFiles(
        StaticSitePackageResult package,
        StaticSitePackageVerificationReport report)
    {
        foreach (var relativePath in RequiredRelativeFiles)
        {
            var path = Path.Combine(package.OutputDirectory, relativePath);
            if (!File.Exists(path))
            {
                report.Errors.Add($"required package file is missing: {relativePath}");
            }
        }

        VerifyExpectedPath(package.EntryPoint, Path.Combine(package.OutputDirectory, "index.html"), "entry_point", report);
        VerifyExpectedPath(package.SiteJsonPath, Path.Combine(package.OutputDirectory, "site.json"), "site_json_path", report);
        VerifyExpectedPath(
            package.ManifestPath,
            Path.Combine(package.OutputDirectory, "components", "manifest.json"),
            "manifest_path",
            report);
    }

    private static void VerifyExpectedPath(
        string actualPath,
        string expectedPath,
        string fieldName,
        StaticSitePackageVerificationReport report)
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            report.Errors.Add($"{fieldName} is required.");
            return;
        }

        if (!Path.GetFullPath(actualPath).Equals(Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
        {
            report.Errors.Add($"{fieldName} must point to {expectedPath}.");
        }
    }

    private static GeneratorSiteDocument? ReadSiteDocument(
        StaticSitePackageResult package,
        StaticSitePackageVerificationReport report)
    {
        if (!File.Exists(package.SiteJsonPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GeneratorSiteDocument>(
                File.ReadAllText(package.SiteJsonPath),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            report.Errors.Add($"site.json is invalid JSON: {exception.Message}");
            return null;
        }
    }

    private static void VerifyArchive(
        StaticSitePackageResult package,
        StaticSitePackageVerificationReport report)
    {
        if (string.IsNullOrWhiteSpace(package.ArchivePath))
        {
            report.Warnings.Add("archive_path is empty; no portable zip was verified.");
            return;
        }

        if (!File.Exists(package.ArchivePath))
        {
            report.Errors.Add($"archive_path does not exist: {package.ArchivePath}");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(package.ArchivePath);
            report.ArchiveEntries = archive.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToList();

            var entries = report.ArchiveEntries.ToHashSet(StringComparer.Ordinal);
            foreach (var required in RequiredRelativeFiles.Select(path => path.Replace('\\', '/')))
            {
                if (!entries.Contains(required))
                {
                    report.Errors.Add($"archive is missing required entry: {required}");
                }
            }
        }
        catch (InvalidDataException exception)
        {
            report.Errors.Add($"archive is invalid: {exception.Message}");
        }
    }

    private static void NormalizeDocumentProps(GeneratorSiteDocument document)
    {
        foreach (var route in document.Routes)
        {
            NormalizeNodeProps(route.Root);
        }
    }

    private static void NormalizeNodeProps(ComponentNode node)
    {
        node.Props = node.Props.ToDictionary(
            pair => pair.Key,
            pair => NormalizePropValue(pair.Value),
            StringComparer.Ordinal);

        foreach (var child in node.Children)
        {
            NormalizeNodeProps(child);
        }
    }

    private static object? NormalizePropValue(object? value)
    {
        return value switch
        {
            JsonElement element => NormalizeJsonElement(element),
            Dictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizePropValue(pair.Value),
                StringComparer.Ordinal),
            IEnumerable<object?> values when value is not string => values
                .Select(NormalizePropValue)
                .ToList(),
            _ => value,
        };
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(NormalizeJsonElement)
                .ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => NormalizeJsonElement(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Null => null,
            _ => null,
        };
    }
}
