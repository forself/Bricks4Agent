using System.Text.Json;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;
using WorkerSdk;

namespace SiteCrawlerWorker.Handlers;

public sealed class SiteReconstructPackageHandler : ICapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SiteCrawlerService crawler;
    private readonly SiteGeneratorConverter converter;
    private readonly StaticSitePackageGenerator packageGenerator;
    private readonly SiteGenerationQualityAnalyzer qualityAnalyzer = new();
    private readonly ILogger<SiteReconstructPackageHandler> logger;

    public SiteReconstructPackageHandler(
        SiteCrawlerService crawler,
        SiteGeneratorConverter converter,
        StaticSitePackageGenerator packageGenerator,
        ILogger<SiteReconstructPackageHandler> logger)
    {
        this.crawler = crawler;
        this.converter = converter;
        this.packageGenerator = packageGenerator;
        this.logger = logger;
    }

    public string CapabilityId => "site.reconstruct_package";

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId,
        string route,
        string payload,
        string scope,
        CancellationToken ct)
    {
        SiteReconstructPackageRequest request;
        try
        {
            request = DeserializeRequest(payload);
        }
        catch (JsonException exception)
        {
            return (false, null, exception.Message);
        }

        request.Scope ??= new SiteCrawlScope();
        request.Capture ??= new SiteCrawlCaptureOptions();
        request.Budgets ??= new SiteCrawlBudgets();

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            request.RequestId = requestId;
        }

        if (string.IsNullOrWhiteSpace(request.StartUrl))
        {
            return (false, null, "start_url is required.");
        }

        try
        {
            var crawl = await crawler.CrawlAsync(new SiteCrawlRequest
            {
                RequestId = request.RequestId,
                StartUrl = request.StartUrl,
                Scope = request.Scope,
                Capture = request.Capture,
                Budgets = request.Budgets,
            }, ct);
            var document = converter.Convert(crawl);
            var quality = qualityAnalyzer.Analyze(document);
            if (request.EnforceQualityGate && !quality.IsPassed)
            {
                return (
                    false,
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["crawl_run_id"] = crawl.CrawlRunId,
                        ["page_count"] = crawl.Pages.Count,
                        ["excluded_count"] = crawl.Excluded.Count,
                        ["quality_report"] = quality,
                    }, JsonOptions),
                    $"Site generation quality gate failed: {string.Join("; ", quality.Errors)}");
            }

            var package = packageGenerator.Generate(document, new StaticSitePackageOptions
            {
                OutputDirectory = request.OutputDirectory,
                PackageName = string.IsNullOrWhiteSpace(request.PackageName)
                    ? BuildDefaultPackageName(request.RequestId)
                    : request.PackageName,
                EnforceQualityGate = request.EnforceQualityGate,
                CreateArchive = request.CreateArchive,
                ArchivePath = request.ArchivePath,
            });
            if (request.EnforceQualityGate && !package.VerificationReport.IsPassed)
            {
                TryDeletePackageArtifacts(package);
                return (
                    false,
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["crawl_run_id"] = crawl.CrawlRunId,
                        ["page_count"] = crawl.Pages.Count,
                        ["excluded_count"] = crawl.Excluded.Count,
                        ["quality_report"] = package.QualityReport,
                        ["verification_report"] = package.VerificationReport,
                    }, JsonOptions),
                    $"Site package verification failed: {string.Join("; ", package.VerificationReport.Errors)}");
            }

            return (
                true,
                JsonSerializer.Serialize(new SiteReconstructPackageResult
                {
                    CrawlRunId = crawl.CrawlRunId,
                    PageCount = crawl.Pages.Count,
                    ExcludedCount = crawl.Excluded.Count,
                    Package = package,
                }, JsonOptions),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to reconstruct site package for request {RequestId}", requestId);
            return (false, null, exception.Message);
        }
    }

    private static SiteReconstructPackageRequest DeserializeRequest(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var requestElement = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("args", out var argsElement) &&
            argsElement.ValueKind == JsonValueKind.Object
                ? argsElement
                : root;

        return requestElement.Deserialize<SiteReconstructPackageRequest>(JsonOptions) ??
            new SiteReconstructPackageRequest();
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
