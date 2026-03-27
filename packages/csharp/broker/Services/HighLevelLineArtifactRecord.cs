namespace Broker.Services;

public sealed class HighLevelLineArtifactRecord
{
    public string ArtifactId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string Channel { get; set; } = "line";
    public string UserId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string RelatedTaskType { get; set; } = string.Empty;
    public string RelatedDraftId { get; set; } = string.Empty;
    public string RelatedTaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DeliveryMode { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string DocumentsRoot { get; set; } = string.Empty;
    public bool UploadedToGoogleDrive { get; set; }
    public string DriveIdentityMode { get; set; } = string.Empty;
    public string DriveCredentialChannel { get; set; } = string.Empty;
    public string DriveCredentialUserId { get; set; } = string.Empty;
    public string DriveShareMode { get; set; } = string.Empty;
    public string GoogleDriveFileId { get; set; } = string.Empty;
    public string GoogleDriveWebViewLink { get; set; } = string.Empty;
    public string GoogleDriveDownloadLink { get; set; } = string.Empty;
    public string NotificationId { get; set; } = string.Empty;
    public string DriveError { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
