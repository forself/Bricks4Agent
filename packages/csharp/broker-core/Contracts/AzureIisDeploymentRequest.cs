namespace BrokerCore.Contracts;

public sealed class AzureIisDeploymentRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public string VmHost { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string DeploymentMode { get; set; } = "site_root";
    public string ApplicationPath { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string HealthCheckPath { get; set; } = string.Empty;
    public string SecretRef { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string ProjectFile { get; set; } = string.Empty;
    public string Configuration { get; set; } = "Release";
    public string? RuntimeIdentifier { get; set; }
    public bool SelfContained { get; set; }
    public bool CleanupTarget { get; set; } = true;
    public bool RestartSite { get; set; } = true;
    public string PublishOutputPath { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string ScopeJson { get; set; } = "{}";
    public string MetadataJson { get; set; } = "{}";
}
