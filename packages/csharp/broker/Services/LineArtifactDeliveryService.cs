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
    public string OverallStatus { get; set; } = string.Empty;
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
            }
        }

        var driveOk = driveResult?.Success == true;
        var overallStatus = driveOk || !request.UploadToGoogleDrive ? "completed" : "partial";

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
            Message = driveOk ? "ok" : (driveResult?.Message ?? "drive_not_requested"),
            DeliveryMode = driveOk ? "google_drive" : "local_only",
            FileName = fileName,
            Format = format,
            FilePath = filePath,
            DocumentsRoot = managedPaths.DocumentsRoot,
            UploadedToGoogleDrive = driveOk,
            GoogleDriveFileId = driveResult?.FileId ?? string.Empty,
            GoogleDriveWebViewLink = driveResult?.WebViewLink ?? string.Empty,
            GoogleDriveDownloadLink = driveResult?.DownloadLink ?? string.Empty,
            DriveError = driveOk ? string.Empty : (driveResult?.Message ?? string.Empty),
            OverallStatus = overallStatus,
            NotificationId = notification?.NotificationId ?? string.Empty
        });

        return new LineArtifactDeliveryResult
        {
            Success = true,
            Message = driveOk ? "ok" : "file_created_drive_failed",
            OverallStatus = overallStatus,
            UserId = request.UserId,
            DocumentsRoot = managedPaths.DocumentsRoot,
            FilePath = filePath,
            FileName = fileName,
            UploadedToGoogleDrive = driveOk,
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

    internal static string BuildNotificationBody(
        string fileName,
        string filePath,
        GoogleDriveShareResult? driveResult)
    {
        var lines = new List<string>();

        if (driveResult?.Success == true)
        {
            lines.Add("您的文件已準備完成");
            lines.Add(string.Empty);
            lines.Add($"文件名稱：{fileName}");

            if (!string.IsNullOrWhiteSpace(driveResult.DownloadLink))
            {
                lines.Add(string.Empty);
                lines.Add("點擊下載：");
                lines.Add(driveResult.DownloadLink);
            }

            if (!string.IsNullOrWhiteSpace(driveResult.WebViewLink))
            {
                lines.Add(string.Empty);
                lines.Add("線上預覽：");
                lines.Add(driveResult.WebViewLink);
            }
        }
        else
        {
            lines.Add("您的文件已準備完成");
            lines.Add(string.Empty);
            lines.Add($"文件名稱：{fileName}");
            lines.Add(string.Empty);
            lines.Add("檔案已儲存，但雲端上傳未完成。");
            lines.Add("管理員將協助提供下載連結。");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public async Task<LineArtifactDeliveryResult> RetryDriveUploadAsync(
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        var artifact = _workspaceService.ReadArtifactById(artifactId);
        if (artifact == null)
            return Fail("artifact not found.");

        if (artifact.OverallStatus != "partial")
            return Fail($"artifact status is '{artifact.OverallStatus}', not 'partial'. Nothing to retry.");

        if (string.IsNullOrWhiteSpace(artifact.FilePath) || !File.Exists(artifact.FilePath))
            return Fail("local file no longer exists.");

        var driveResult = await _googleDriveShareService.ShareFileAsync(new GoogleDriveShareRequest
        {
            FilePath = artifact.FilePath,
            FileName = artifact.FileName,
            FolderId = string.Empty,
            ShareMode = "anyone_with_link",
            IdentityMode = "user_delegated",
            Channel = artifact.Channel,
            UserId = artifact.UserId
        }, cancellationToken);

        if (!driveResult.Success)
        {
            artifact.DriveError = driveResult.Message;
            _workspaceService.RecordArtifact(artifact);

            return new LineArtifactDeliveryResult
            {
                Success = false,
                Message = driveResult.Message,
                OverallStatus = "partial",
                UserId = artifact.UserId,
                FilePath = artifact.FilePath,
                FileName = artifact.FileName,
                GoogleDrive = driveResult
            };
        }

        artifact.UploadedToGoogleDrive = true;
        artifact.GoogleDriveFileId = driveResult.FileId;
        artifact.GoogleDriveWebViewLink = driveResult.WebViewLink;
        artifact.GoogleDriveDownloadLink = driveResult.DownloadLink;
        artifact.DriveError = string.Empty;
        artifact.DeliveryMode = "google_drive";
        artifact.OverallStatus = "completed";

        var notification = _workspaceService.QueueLineNotification(
            artifact.UserId,
            "文件已完成",
            BuildNotificationBody(artifact.FileName, artifact.FilePath, driveResult));

        artifact.NotificationId = notification.NotificationId;
        _workspaceService.RecordArtifact(artifact);

        return new LineArtifactDeliveryResult
        {
            Success = true,
            Message = "ok",
            OverallStatus = "completed",
            UserId = artifact.UserId,
            FilePath = artifact.FilePath,
            FileName = artifact.FileName,
            UploadedToGoogleDrive = true,
            GoogleDrive = driveResult,
            Notification = notification,
            Artifact = artifact
        };
    }

    private static LineArtifactDeliveryResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}
