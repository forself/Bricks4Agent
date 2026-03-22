namespace BrokerCore.Contracts;

public class BrowserExecutionRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string ToolId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;

    public string IdentityMode { get; set; } = string.Empty;
    public string CredentialBinding { get; set; } = string.Empty;
    public string SessionBindingMode { get; set; } = string.Empty;
    public string SessionReuseScope { get; set; } = string.Empty;
    public string SiteBindingMode { get; set; } = string.Empty;
    public string[] AllowedSiteClasses { get; set; } = Array.Empty<string>();
    public string MaxActionLevel { get; set; } = string.Empty;
    public string[] RequiresHumanConfirmationOn { get; set; } = Array.Empty<string>();

    public string PrincipalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;

    public string? SiteBindingId { get; set; }
    public string? UserGrantId { get; set; }
    public string? SystemBindingId { get; set; }
    public string? SessionLeaseId { get; set; }

    public string StartUrl { get; set; } = string.Empty;
    public string IntendedActionLevel { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string ScopeJson { get; set; } = "{}";
}
