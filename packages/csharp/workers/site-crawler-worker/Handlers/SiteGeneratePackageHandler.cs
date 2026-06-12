using System.Text.Json;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;
using WorkerSdk;

namespace SiteCrawlerWorker.Handlers;

public sealed class SiteGeneratePackageHandler : ICapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SiteGeneratorConverter converter;
    private readonly StaticSitePackageGenerator packageGenerator;
    private readonly SiteGenerationQualityAnalyzer qualityAnalyzer = new();
    private readonly ILogger<SiteGeneratePackageHandler> logger;

    public SiteGeneratePackageHandler(
        SiteGeneratorConverter converter,
        StaticSitePackageGenerator packageGenerator,
        ILogger<SiteGeneratePackageHandler> logger)
    {
        this.converter = converter;
        this.packageGenerator = packageGenerator;
        this.logger = logger;
    }

    public string CapabilityId => "site.generate_package";

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId,
        string route,
        string payload,
        string scope,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(payload);
            var root = UnwrapArgs(document.RootElement);

            var siteDocument = TryDeserializeProperty<GeneratorSiteDocument>(root, "site_document");
            if (siteDocument is null)
            {
                var crawlResult = TryDeserializeProperty<SiteCrawlResult>(root, "crawl_result");
                if (crawlResult is null)
                {
                    return Task.FromResult<(bool, string?, string?)>(
                        (false, null, "crawl_result or site_document is required."));
                }

                siteDocument = converter.Convert(crawlResult);
            }

            NormalizeDocumentProps(siteDocument);

            var options = new StaticSitePackageOptions
            {
                OutputDirectory = TryGetString(root, "output_directory") ?? string.Empty,
                PackageName = TryGetString(root, "package_name") ?? BuildDefaultPackageName(requestId),
                EnforceQualityGate = TryGetBoolean(root, "enforce_quality_gate") ?? true,
                CreateArchive = TryGetBoolean(root, "create_archive") ?? false,
                ArchivePath = TryGetString(root, "archive_path") ?? string.Empty,
            };

            if (options.EnforceQualityGate)
            {
                var quality = qualityAnalyzer.Analyze(siteDocument);
                if (!quality.IsPassed)
                {
                    return Task.FromResult<(bool, string?, string?)>((
                        false,
                        JsonSerializer.Serialize(new Dictionary<string, object?>
                        {
                            ["quality_report"] = quality,
                        }, JsonOptions),
                        $"Site generation quality gate failed: {string.Join("; ", quality.Errors)}"));
                }
            }

            var result = packageGenerator.Generate(siteDocument, options);
            if (options.EnforceQualityGate && !result.VerificationReport.IsPassed)
            {
                TryDeletePackageArtifacts(result);
                return Task.FromResult<(bool, string?, string?)>((
                    false,
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["quality_report"] = result.QualityReport,
                        ["verification_report"] = result.VerificationReport,
                    }, JsonOptions),
                    $"Site package verification failed: {string.Join("; ", result.VerificationReport.Errors)}"));
            }

            return Task.FromResult<(bool, string?, string?)>(
                (true, JsonSerializer.Serialize(result, JsonOptions), null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            return Task.FromResult<(bool, string?, string?)>((false, null, exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to generate site package for request {RequestId}", requestId);
            return Task.FromResult<(bool, string?, string?)>((false, null, exception.Message));
        }
    }

    private static JsonElement UnwrapArgs(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("args", out var args) &&
            args.ValueKind == JsonValueKind.Object
                ? args
                : root;
    }

    private static T? TryDeserializeProperty<T>(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? property.Deserialize<T>(JsonOptions)
                : default;
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool? TryGetBoolean(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : null;
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

    private static string BuildDefaultPackageName(string requestId)
    {
        return string.IsNullOrWhiteSpace(requestId) ? "generated-site" : requestId;
    }

    private static void TryDeletePackageArtifacts(StaticSitePackageResult result)
    {
        try
        {
            if (Directory.Exists(result.OutputDirectory))
            {
                Directory.Delete(result.OutputDirectory, recursive: true);
            }

            if (!string.IsNullOrWhiteSpace(result.ArchivePath) && File.Exists(result.ArchivePath))
            {
                File.Delete(result.ArchivePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
