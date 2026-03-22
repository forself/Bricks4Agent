using BaseOrm;

namespace BrokerCore.Models;

[Table("azure_iis_deployment_targets")]
public class AzureIisDeploymentTarget
{
    [Key(AutoIncrement = false)]
    [Column("target_id")]
    public string TargetId { get; set; } = string.Empty;

    [Column("display_name")]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Column("provider")]
    [MaxLength(100)]
    public string Provider { get; set; } = "azure_vm_iis";

    [Column("vm_host")]
    [MaxLength(300)]
    public string VmHost { get; set; } = string.Empty;

    [Column("port")]
    public int Port { get; set; } = 5985;

    [Column("use_ssl")]
    public bool UseSsl { get; set; }

    [Column("transport")]
    [MaxLength(100)]
    public string Transport { get; set; } = "winrm_powershell";

    [Column("site_name")]
    [MaxLength(200)]
    public string SiteName { get; set; } = string.Empty;

    [Column("deployment_mode")]
    [MaxLength(50)]
    public string DeploymentMode { get; set; } = "site_root";

    [Column("application_path")]
    [MaxLength(300)]
    public string ApplicationPath { get; set; } = string.Empty;

    [Column("app_pool_name")]
    [MaxLength(200)]
    public string AppPoolName { get; set; } = string.Empty;

    [Column("physical_path")]
    [MaxLength(500)]
    public string PhysicalPath { get; set; } = string.Empty;

    [Column("health_check_path")]
    [MaxLength(300)]
    public string HealthCheckPath { get; set; } = string.Empty;

    [Column("secret_ref")]
    [MaxLength(200)]
    public string SecretRef { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "active";

    [Column("metadata_json")]
    public string MetadataJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
