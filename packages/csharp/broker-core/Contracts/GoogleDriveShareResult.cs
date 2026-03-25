namespace BrokerCore.Contracts;

public sealed class GoogleDriveShareResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string ShareMode { get; set; } = string.Empty;
    public string WebViewLink { get; set; } = string.Empty;
    public string DownloadLink { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
}
