using System.Text.Json;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;
using WorkerSdk;

namespace SiteCrawlerWorker.Handlers;

public sealed class SiteCrawlSourceHandler : ICapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly SiteCrawlerService crawler;
    private readonly ILogger<SiteCrawlSourceHandler> logger;

    public SiteCrawlSourceHandler(SiteCrawlerService crawler, ILogger<SiteCrawlSourceHandler> logger)
    {
        this.crawler = crawler;
        this.logger = logger;
    }

    public string CapabilityId => "site.crawl_source";

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId,
        string route,
        string payload,
        string scope,
        CancellationToken ct)
    {
        SiteCrawlRequest request;
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
            var result = await crawler.CrawlAsync(request, ct);
            return (true, JsonSerializer.Serialize(result, JsonOptions), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to crawl site source for request {RequestId}", requestId);
            return (false, null, exception.Message);
        }
    }

    private static SiteCrawlRequest DeserializeRequest(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var requestElement = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("args", out var argsElement) &&
            argsElement.ValueKind == JsonValueKind.Object
                ? argsElement
                : root;

        return requestElement.Deserialize<SiteCrawlRequest>(JsonOptions) ?? new SiteCrawlRequest();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        };
    }
}
