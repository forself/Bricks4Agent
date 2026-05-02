using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Broker.Services;

public sealed class HighLevelSiteRebuildResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public int MaxDepth { get; set; }
    public int PagesCrawled { get; set; }
    public int RoutesGenerated { get; set; }
    public string GeneratedSiteRoot { get; set; } = string.Empty;
    public string PackageFilePath { get; set; } = string.Empty;
    public string PackageFileName { get; set; } = string.Empty;
    public LineArtifactDeliveryResult? Delivery { get; set; }
}

public sealed class HighLevelSiteRebuildService
{
    private const int DefaultMaxDepth = 1;
    private const int SiteRebuildMaxPages = int.MaxValue;
    private const int DefaultTimeoutSeconds = 120;
    private static readonly Regex UrlPattern = new(@"https?://[^\s#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DepthPattern = new(@"(?:深度|depth)\s*[:：]?\s*(\d+)|(\d+)\s*層", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HighLevelLineWorkspaceService workspaceService;
    private readonly LineArtifactDeliveryService artifactDeliveryService;
    private readonly IPageFetcher pageFetcher;
    private readonly ILogger<HighLevelSiteRebuildService> logger;

    public HighLevelSiteRebuildService(
        HighLevelLineWorkspaceService workspaceService,
        LineArtifactDeliveryService artifactDeliveryService,
        ILogger<HighLevelSiteRebuildService> logger)
        : this(workspaceService, artifactDeliveryService, new HttpPageFetcher(), logger)
    {
    }

    public HighLevelSiteRebuildService(
        HighLevelLineWorkspaceService workspaceService,
        LineArtifactDeliveryService artifactDeliveryService,
        IPageFetcher pageFetcher,
        ILogger<HighLevelSiteRebuildService> logger)
    {
        this.workspaceService = workspaceService;
        this.artifactDeliveryService = artifactDeliveryService;
        this.pageFetcher = pageFetcher;
        this.logger = logger;
    }

    public async Task<HighLevelSiteRebuildResult> GenerateAndDeliverAsync(
        HighLevelTaskDraft draft,
        HighLevelUserProfile profile,
        string relatedTaskId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(draft.TaskType, "site_rebuild", StringComparison.OrdinalIgnoreCase))
            return Fail("draft is not a site_rebuild task.");

        var sourceUrl = TryExtractSourceUrl(draft.OriginalMessage);
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return Fail("source URL is required for site_rebuild.");

        if (string.IsNullOrWhiteSpace(draft.ManagedPaths.ProjectRoot))
            return Fail("project root is not available.");

        var managedPaths = workspaceService.GetManagedPaths(profile.UserId, ensureExists: true);
        if (managedPaths is null)
            return Fail("managed paths not found.");

        Directory.CreateDirectory(draft.ManagedPaths.ProjectRoot);
        Directory.CreateDirectory(managedPaths.DocumentsRoot);

        var maxDepth = ParseMaxDepth(draft.OriginalMessage);
        var crawl = await CrawlAsync(sourceUrl, maxDepth, cancellationToken);
        if (crawl.Pages.Count == 0)
            return Fail("site crawl returned no pages.");

        var document = new SiteGeneratorConverter(new ComponentLibraryLoader().LoadDefault()).Convert(crawl);
        var generatedRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "generated-site");
        if (Directory.Exists(generatedRoot))
            Directory.Delete(generatedRoot, recursive: true);

        var package = new StaticSitePackageGenerator().Generate(document, new StaticSitePackageOptions
        {
            OutputDirectory = generatedRoot,
            PackageName = "site",
        });

        VerifyGeneratedPackage(package);

        var packageFileName = BuildPackageFileName(draft);
        var packageFilePath = Path.Combine(managedPaths.DocumentsRoot, packageFileName);
        if (File.Exists(packageFilePath))
            File.Delete(packageFilePath);

        ZipFile.CreateFromDirectory(package.OutputDirectory, packageFilePath, CompressionLevel.SmallestSize, includeBaseDirectory: false, Encoding.UTF8);

        var uploadToGoogleDrive = artifactDeliveryService.CanUploadToGoogleDrive(profile.UserId, "shared_delegated");
        var delivery = await artifactDeliveryService.DeliverExistingFileAsync(new LineExistingArtifactDeliveryRequest
        {
            UserId = profile.UserId,
            FilePath = packageFilePath,
            FileName = packageFileName,
            UploadToGoogleDrive = uploadToGoogleDrive,
            IdentityMode = "shared_delegated",
            ShareMode = string.Empty,
            SendLineNotification = true,
            NotificationTitle = "網站重製包已產生",
            Source = "high_level_site_rebuild",
            RelatedTaskType = draft.TaskType,
            RelatedDraftId = draft.DraftId,
            RelatedTaskId = relatedTaskId,
        }, cancellationToken);

        return new HighLevelSiteRebuildResult
        {
            Success = delivery.Success,
            Message = delivery.Success
                ? (delivery.UploadedToGoogleDrive ? "site_rebuild_packaged_and_uploaded" : "site_rebuild_packaged_locally_only")
                : delivery.Message,
            SourceUrl = sourceUrl,
            MaxDepth = maxDepth,
            PagesCrawled = crawl.Pages.Count,
            RoutesGenerated = document.Routes.Count,
            GeneratedSiteRoot = package.OutputDirectory,
            PackageFilePath = packageFilePath,
            PackageFileName = packageFileName,
            Delivery = delivery,
        };
    }

    public static string? TryExtractSourceUrl(string message)
    {
        var match = UrlPattern.Match(message ?? string.Empty);
        if (!match.Success)
            return null;

        return match.Value.TrimEnd('.', ',', ';', '，', '。', '；');
    }

    public static int ParseMaxDepth(string message)
    {
        var match = DepthPattern.Match(message ?? string.Empty);
        if (!match.Success)
            return DefaultMaxDepth;

        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return int.TryParse(value, out var depth)
            ? Math.Clamp(depth, 0, 5)
            : DefaultMaxDepth;
    }

    private async Task<SiteCrawlResult> CrawlAsync(string sourceUrl, int maxDepth, CancellationToken cancellationToken)
    {
        var crawler = new SiteCrawlerService(pageFetcher, new DeterministicSiteExtractor());
        return await crawler.CrawlAsync(new SiteCrawlRequest
        {
            RequestId = $"site-rebuild-{Guid.NewGuid():N}"[..24],
            StartUrl = sourceUrl,
            Scope = new SiteCrawlScope
            {
                Kind = "link_depth",
                MaxDepth = maxDepth,
                SameOriginOnly = true,
                PathPrefixLock = true,
            },
            Capture = new SiteCrawlCaptureOptions
            {
                Html = false,
                RenderedDom = false,
                Css = false,
                Scripts = false,
                Assets = false,
                Screenshots = false,
            },
            Budgets = new SiteCrawlBudgets
            {
                MaxPages = SiteRebuildMaxPages,
                MaxTotalBytes = 15 * 1024 * 1024,
                MaxAssetBytes = 0,
                WallClockTimeoutSeconds = DefaultTimeoutSeconds,
            },
        }, cancellationToken);
    }

    private static void VerifyGeneratedPackage(StaticSitePackageResult package)
    {
        foreach (var path in new[] { package.EntryPoint, package.SiteJsonPath, package.ManifestPath })
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"generated site package is missing {path}");
        }
    }

    private static string BuildPackageFileName(HighLevelTaskDraft draft)
    {
        var name = string.IsNullOrWhiteSpace(draft.ProjectFolderName) ? "site-rebuild" : draft.ProjectFolderName.Trim();
        return $"{name}-site-package.zip";
    }

    private HighLevelSiteRebuildResult Fail(string message)
    {
        logger.LogWarning("Site rebuild generation failed: {Message}", message);
        return new HighLevelSiteRebuildResult
        {
            Success = false,
            Message = message,
        };
    }
}
