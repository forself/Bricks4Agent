using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public interface IAzureIisDeploymentRequestBuilder
{
    AzureIisDeploymentRequestBuildResult TryBuild(string toolId, AzureIisDeploymentBuildInput input);
}

public sealed class AzureIisDeploymentRequestBuilder : IAzureIisDeploymentRequestBuilder
{
    private readonly IToolSpecRegistry _registry;
    private readonly BrokerDb _db;

    public AzureIisDeploymentRequestBuilder(IToolSpecRegistry registry, BrokerDb db)
    {
        _registry = registry;
        _db = db;
    }

    public AzureIisDeploymentRequestBuildResult TryBuild(string toolId, AzureIisDeploymentBuildInput input)
    {
        var spec = _registry.Get(toolId);
        if (spec == null)
            return AzureIisDeploymentRequestBuildResult.Fail("tool_spec_not_found");

        if (!string.Equals(spec.Kind, "deployment", StringComparison.OrdinalIgnoreCase))
            return AzureIisDeploymentRequestBuildResult.Fail("tool_spec_not_deployment");

        if (string.IsNullOrWhiteSpace(input.RequestId) ||
            string.IsNullOrWhiteSpace(input.CapabilityId) ||
            string.IsNullOrWhiteSpace(input.Route) ||
            string.IsNullOrWhiteSpace(input.PrincipalId) ||
            string.IsNullOrWhiteSpace(input.TaskId) ||
            string.IsNullOrWhiteSpace(input.SessionId) ||
            string.IsNullOrWhiteSpace(input.TargetId) ||
            string.IsNullOrWhiteSpace(input.ProjectPath))
        {
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_request_input_incomplete");
        }

        var target = _db.Get<AzureIisDeploymentTarget>(input.TargetId);
        if (target == null || !string.Equals(target.Status, "active", StringComparison.OrdinalIgnoreCase))
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_target_not_found");

        if (!string.Equals(target.Provider, "azure_vm_iis", StringComparison.Ordinal))
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_target_provider_mismatch");

        if (!string.Equals(target.Transport, "winrm_powershell", StringComparison.Ordinal))
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_target_transport_not_supported");

        var deploymentMode = NormalizeDeploymentMode(target.DeploymentMode);
        if (deploymentMode == null)
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_target_mode_not_supported");

        var applicationPath = NormalizeApplicationPath(target.ApplicationPath);
        if (string.Equals(deploymentMode, "iis_application", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(applicationPath))
        {
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_application_path_required");
        }

        if (!Path.IsPathRooted(input.ProjectPath))
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_project_path_must_be_absolute");

        var normalizedProjectPath = Path.GetFullPath(input.ProjectPath);
        if (!File.Exists(normalizedProjectPath) && !Directory.Exists(normalizedProjectPath))
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_project_path_not_found");

        var projectFile = ResolveProjectFile(normalizedProjectPath);
        if (projectFile == null)
            return AzureIisDeploymentRequestBuildResult.Fail("deployment_project_file_not_found");

        var outputRoot = Path.Combine(Path.GetTempPath(), "b4a-deploy", input.RequestId);
        var publishOutputPath = Path.Combine(outputRoot, "publish");
        var packagePath = Path.Combine(outputRoot, "publish.zip");

        return AzureIisDeploymentRequestBuildResult.Ok(new AzureIisDeploymentRequest
        {
            RequestId = input.RequestId,
            ToolId = toolId,
            CapabilityId = input.CapabilityId,
            Route = input.Route,
            PrincipalId = input.PrincipalId,
            TaskId = input.TaskId,
            SessionId = input.SessionId,
            TargetId = target.TargetId,
            Provider = target.Provider,
            Transport = target.Transport,
            VmHost = target.VmHost,
            Port = target.Port,
            UseSsl = target.UseSsl,
            SiteName = target.SiteName,
            DeploymentMode = deploymentMode,
            ApplicationPath = applicationPath,
            AppPoolName = target.AppPoolName,
            PhysicalPath = target.PhysicalPath,
            HealthCheckPath = NormalizeHealthCheckPath(target.HealthCheckPath),
            HealthCheckBaseUrl = target.HealthCheckBaseUrl?.Trim() ?? string.Empty,
            SecretRef = target.SecretRef,
            ProjectPath = normalizedProjectPath,
            ProjectFile = projectFile,
            Configuration = string.IsNullOrWhiteSpace(input.Configuration) ? "Release" : input.Configuration,
            RuntimeIdentifier = string.IsNullOrWhiteSpace(input.RuntimeIdentifier) ? null : input.RuntimeIdentifier,
            SelfContained = input.SelfContained,
            CleanupTarget = input.CleanupTarget,
            RestartSite = input.RestartSite,
            PublishOutputPath = publishOutputPath,
            PackagePath = packagePath,
            ScopeJson = string.IsNullOrWhiteSpace(input.ScopeJson) ? "{}" : input.ScopeJson,
            MetadataJson = string.IsNullOrWhiteSpace(input.MetadataJson) ? "{}" : input.MetadataJson
        });
    }

    private static string? NormalizeDeploymentMode(string? deploymentMode)
    {
        var normalized = (deploymentMode ?? "site_root").Trim().ToLowerInvariant();
        return normalized switch
        {
            "site_root" => "site_root",
            "iis_application" => "iis_application",
            _ => null
        };
    }

    private static string NormalizeApplicationPath(string? applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
            return string.Empty;

        var trimmed = applicationPath.Trim();
        if (!trimmed.StartsWith('/'))
            trimmed = "/" + trimmed;

        while (trimmed.Contains("//", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("//", "/", StringComparison.Ordinal);
        }

        return trimmed.Length > 1
            ? trimmed.TrimEnd('/')
            : trimmed;
    }

    private static string NormalizeHealthCheckPath(string? healthCheckPath)
    {
        if (string.IsNullOrWhiteSpace(healthCheckPath))
            return string.Empty;

        var trimmed = healthCheckPath.Trim();
        return trimmed.StartsWith('/')
            ? trimmed
            : "/" + trimmed;
    }

    private static string? ResolveProjectFile(string normalizedProjectPath)
    {
        if (File.Exists(normalizedProjectPath) &&
            string.Equals(Path.GetExtension(normalizedProjectPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedProjectPath;
        }

        if (!Directory.Exists(normalizedProjectPath))
            return null;

        var projectFiles = Directory.GetFiles(normalizedProjectPath, "*.csproj", SearchOption.TopDirectoryOnly);
        return projectFiles.Length == 1 ? projectFiles[0] : null;
    }
}

public sealed class AzureIisDeploymentBuildInput
{
    public string RequestId { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string Configuration { get; set; } = "Release";
    public string? RuntimeIdentifier { get; set; }
    public bool SelfContained { get; set; }
    public bool CleanupTarget { get; set; } = true;
    public bool RestartSite { get; set; } = true;
    public string ScopeJson { get; set; } = "{}";
    public string MetadataJson { get; set; } = "{}";
}

public sealed class AzureIisDeploymentRequestBuildResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public AzureIisDeploymentRequest? Request { get; set; }

    public static AzureIisDeploymentRequestBuildResult Ok(AzureIisDeploymentRequest request)
        => new()
        {
            Success = true,
            Request = request
        };

    public static AzureIisDeploymentRequestBuildResult Fail(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
