namespace BrokerCore.Contracts;

public class BrowserExecutionResult
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }

    public string ToolId { get; set; } = string.Empty;
    public string ActionLevelReached { get; set; } = string.Empty;
    public string FinalUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? StructuredDataJson { get; set; }
    public string? SessionLeaseId { get; set; }
    public string? EvidenceRef { get; set; }
    public string? ErrorMessage { get; set; }

    public static BrowserExecutionResult Ok(
        string requestId,
        string toolId,
        string actionLevelReached,
        string finalUrl,
        string? title = null,
        string? contentText = null,
        string? structuredDataJson = null,
        string? sessionLeaseId = null,
        string? evidenceRef = null)
        => new()
        {
            RequestId = requestId,
            Success = true,
            ToolId = toolId,
            ActionLevelReached = actionLevelReached,
            FinalUrl = finalUrl,
            Title = title,
            ContentText = contentText,
            StructuredDataJson = structuredDataJson,
            SessionLeaseId = sessionLeaseId,
            EvidenceRef = evidenceRef
        };

    public static BrowserExecutionResult Fail(
        string requestId,
        string toolId,
        string actionLevelReached,
        string finalUrl,
        string errorMessage,
        string? sessionLeaseId = null,
        string? evidenceRef = null)
        => new()
        {
            RequestId = requestId,
            Success = false,
            ToolId = toolId,
            ActionLevelReached = actionLevelReached,
            FinalUrl = finalUrl,
            ErrorMessage = errorMessage,
            SessionLeaseId = sessionLeaseId,
            EvidenceRef = evidenceRef
        };
}
