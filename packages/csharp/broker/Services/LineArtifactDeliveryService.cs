using System.Text;
using BrokerCore.Contracts;

namespace Broker.Services;

public sealed class LineArtifactDeliveryRequest
{
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool UploadToGoogleDrive { get; set; } = true;
    public string IdentityMode { get; set; } = "user_delegated";
    public string FolderId { get; set; } = string.Empty;
    public string ShareMode { get; set; } = "anyone_with_link";
    public bool SendLineNotification { get; set; } = true;
    public string NotificationTitle { get; set; } = string.Empty;
    public string Source { get; set; } = "local_admin";
    public string RelatedTaskType { get; set; } = string.Empty;
    public string RelatedDraftId { get; set; } = string.Empty;
    public string RelatedTaskId { get; set; } = string.Empty;
}

public sealed class LineArtifactDeliveryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DocumentsRoot { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool UploadedToGoogleDrive { get; set; }
    public GoogleDriveShareResult? GoogleDrive { get; set; }
    public HighLevelLineNotification? Notification { get; set; }
    public HighLevelLineArtifactRecord? Artifact { get; set; }
}

public sealed class LineArtifactDeliveryService
{
    private static readonly HashSet<string> TextFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "json", "html", "csv"
    };

    private readonly HighLevelLineWorkspaceService _workspaceService;
    private readonly GoogleDriveShareService _googleDriveShareService;
    private readonly ILogger<LineArtifactDeliveryService> _logger;

    public LineArtifactDeliveryService(
        HighLevelLineWorkspaceService workspaceService,
        GoogleDriveShareService googleDriveShareService,
        ILogger<LineArtifactDeliveryService> logger)
    {
        _workspaceService = workspaceService;
        _googleDriveShareService = googleDriveShareService;
        _logger = logger;
    }

    public async Task<LineArtifactDeliveryResult> GenerateAndDeliverAsync(
        LineArtifactDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return Fail("user_id is required.");

        var profile = _workspaceService.GetUserProfile(request.UserId);
        if (profile == null)
            return Fail("LINE user profile not found.");

        var managedPaths = _workspaceService.GetManagedPaths(request.UserId, ensureExists: true);
        if (managedPaths == null)
            return Fail("managed paths not found.");

        var format = ResolveFormat(request.Format, request.FileName);
        if (!TextFormats.Contains(format))
            return Fail("unsupported format. Supported formats: txt, md, json, html, csv.");

        var fileName = ResolveFileName(request.FileName, format);
        if (string.IsNullOrWhiteSpace(fileName))
            return Fail("file name is required.");

        var filePath = Path.Combine(managedPaths.DocumentsRoot, fileName);
        Directory.CreateDirectory(managedPaths.DocumentsRoot);
        await File.WriteAllTextAsync(filePath, request.Content ?? string.Empty, new UTF8Encoding(false), cancellationToken);

        GoogleDriveShareResult? driveResult = null;
        if (request.UploadToGoogleDrive)
        {
            driveResult = await _googleDriveShareService.ShareFileAsync(new GoogleDriveShareRequest
            {
                FilePath = filePath,
                FileName = fileName,
                FolderId = request.FolderId,
                ShareMode = request.ShareMode,
                IdentityMode = request.IdentityMode,
                Channel = "line",
                UserId = request.UserId
            }, cancellationToken);

            if (!driveResult.Success)
            {
                _logger.LogWarning(
                    "Artifact created for LINE user {UserId} but Google Drive upload failed: {Message}",
                    request.UserId,
                    driveResult.Message);

                return new LineArtifactDeliveryResult
                {
                    Success = false,
                    Message = driveResult.Message,
                    UserId = request.UserId,
                    DocumentsRoot = managedPaths.DocumentsRoot,
                    FilePath = filePath,
                    FileName = fileName,
                    UploadedToGoogleDrive = false,
                    GoogleDrive = driveResult
                };
            }
        }

        HighLevelLineNotification? notification = null;
        if (request.SendLineNotification)
        {
            notification = _workspaceService.QueueLineNotification(
                request.UserId,
                string.IsNullOrWhiteSpace(request.NotificationTitle) ? "文件已完成" : request.NotificationTitle.Trim(),
                BuildNotificationBody(fileName, filePath, driveResult));
        }

        var artifact = _workspaceService.RecordArtifact(new HighLevelLineArtifactRecord
        {
            Channel = "line",
            UserId = request.UserId,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "local_admin" : request.Source.Trim(),
            RelatedTaskType = request.RelatedTaskType?.Trim() ?? string.Empty,
            RelatedDraftId = request.RelatedDraftId?.Trim() ?? string.Empty,
            RelatedTaskId = request.RelatedTaskId?.Trim() ?? string.Empty,
            Success = true,
            Message = "ok",
            DeliveryMode = driveResult?.Success == true ? "google_drive" : "local_only",
            FileName = fileName,
            Format = format,
            FilePath = filePath,
            DocumentsRoot = managedPaths.DocumentsRoot,
            UploadedToGoogleDrive = driveResult?.Success == true,
            GoogleDriveFileId = driveResult?.FileId ?? string.Empty,
            GoogleDriveWebViewLink = driveResult?.WebViewLink ?? string.Empty,
            GoogleDriveDownloadLink = driveResult?.DownloadLink ?? string.Empty,
            NotificationId = notification?.NotificationId ?? string.Empty
        });

        return new LineArtifactDeliveryResult
        {
            Success = true,
            Message = "ok",
            UserId = request.UserId,
            DocumentsRoot = managedPaths.DocumentsRoot,
            FilePath = filePath,
            FileName = fileName,
            UploadedToGoogleDrive = driveResult?.Success == true,
            GoogleDrive = driveResult,
            Notification = notification,
            Artifact = artifact
        };
    }

    private static string ResolveFormat(string format, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(format))
            return format.Trim().TrimStart('.').ToLowerInvariant();

        var extension = Path.GetExtension(fileName).Trim().TrimStart('.');
        return string.IsNullOrWhiteSpace(extension) ? "txt" : extension.ToLowerInvariant();
    }

    private static string ResolveFileName(string fileName, string format)
    {
        var trimmed = (fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = $"artifact-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.{format}";

        if (string.IsNullOrWhiteSpace(Path.GetExtension(trimmed)))
            trimmed += "." + format;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(invalid, '_');

        return trimmed;
    }

    private static string BuildNotificationBody(
        string fileName,
        string filePath,
        GoogleDriveShareResult? driveResult)
    {
        var lines = new List<string>
        {
            $"文件：{fileName}",
            $"本機路徑：{filePath}"
        };

        if (driveResult?.Success == true)
        {
            lines.Add(string.Empty);
            lines.Add("Google Drive 連結：");
            if (!string.IsNullOrWhiteSpace(driveResult.WebViewLink))
                lines.Add(driveResult.WebViewLink);
            if (!string.IsNullOrWhiteSpace(driveResult.DownloadLink))
                lines.Add(driveResult.DownloadLink);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static LineArtifactDeliveryResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}
