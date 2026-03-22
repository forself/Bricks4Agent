namespace BrokerCore.Contracts;

public sealed class AzureIisDeploymentResult
{
    public bool Success { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string PublishOutputPath { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string DeploymentScriptPath { get; set; } = string.Empty;
    public string ScriptPreview { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";

    public static AzureIisDeploymentResult Ok(
        string requestId,
        string targetId,
        string stage,
        string message,
        string publishOutputPath = "",
        string packagePath = "",
        string deploymentScriptPath = "",
        string scriptPreview = "",
        string detailsJson = "{}")
        => new()
        {
            Success = true,
            RequestId = requestId,
            TargetId = targetId,
            Stage = stage,
            Message = message,
            PublishOutputPath = publishOutputPath,
            PackagePath = packagePath,
            DeploymentScriptPath = deploymentScriptPath,
            ScriptPreview = scriptPreview,
            DetailsJson = detailsJson
        };

    public static AzureIisDeploymentResult Fail(string requestId, string targetId, string stage, string message)
        => new()
        {
            Success = false,
            RequestId = requestId,
            TargetId = targetId,
            Stage = stage,
            Message = message
        };
}
