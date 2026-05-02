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

            var options = new StaticSitePackageOptions
            {
                OutputDirectory = TryGetString(root, "output_directory") ?? string.Empty,
                PackageName = TryGetString(root, "package_name") ?? BuildDefaultPackageName(requestId),
            };

            var result = packageGenerator.Generate(siteDocument, options);
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

    private static string BuildDefaultPackageName(string requestId)
    {
        return string.IsNullOrWhiteSpace(requestId) ? "generated-site" : requestId;
    }
}
