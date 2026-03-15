namespace BrokerCore.Data;

public class DevelopmentSeedOptions
{
    public bool Enabled { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Development Principal";
    public string ActorType { get; set; } = "AI";
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = "analysis";
    public string ScopeDescriptor { get; set; } = "{}";
    public string RuntimeDescriptor { get; set; } = "{}";
    public string AssignedRoleId { get; set; } = "role_reader";
}
